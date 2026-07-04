using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Hotkeys;
using Octadock.Core.Settings;
using Octadock.Platform.Windows.Interop;

namespace Octadock.Platform.Windows.Hotkeys;

/// <summary>
/// Registers global hotkeys via <c>RegisterHotKey</c> against a dedicated
/// message-only window running its own pump on a background STA thread. When a
/// <c>WM_HOTKEY</c> arrives, the mapped <see cref="HotkeyAction"/> is raised on
/// the most recent non-null <see cref="SynchronizationContext"/> observed during
/// registration, satisfying the "raised on the UI thread" contract even when the
/// singleton was constructed before WPF installed its dispatcher context.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyService : IHotkeyService, IDisposable
{
    private const string WindowClassName = "Octadock.HotkeyWindow";

    private readonly ILogger<HotkeyService> _logger;
    private readonly object _gate = new();

    // Maps action -> assigned hotkey id, and id -> action for reverse lookup on WM_HOTKEY.
    private readonly ConcurrentDictionary<HotkeyAction, int> _actionToId = new();
    private readonly ConcurrentDictionary<int, HotkeyAction> _idToAction = new();
    private readonly ConcurrentDictionary<HotkeyAction, HotkeyGesture> _actionToGesture = new();

    private WndProc? _wndProcDelegate; // kept alive for the window's lifetime
    private nint _hwnd;
    private ushort _classAtom;
    private nint _hInstance;
    private uint _threadId;
    private Thread? _pumpThread;
    private readonly ManualResetEventSlim _ready = new(false);
    private SynchronizationContext? _syncContext;
    private volatile bool _disposed;
    private int _nextId = 1;

    /// <summary>Creates the hotkey service and starts its message pump thread.</summary>
    public HotkeyService(ILogger<HotkeyService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        CaptureCurrentSynchronizationContext();
        StartPumpThread();
    }

    /// <inheritdoc />
    public event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;

    /// <inheritdoc />
    public IReadOnlyList<HotkeyRegistration> RegisterAll(ShortcutSettings shortcuts)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);

        lock (_gate)
        {
            ThrowIfDisposed();
            CaptureCurrentSynchronizationContext();

            var requested = shortcuts.Enumerate().ToArray();
            var previous = SnapshotRegistrations();
            var results = new List<HotkeyRegistration>();

            UnregisterAllNoLock();

            foreach ((HotkeyAction action, HotkeyGesture gesture) in requested)
            {
                try
                {
                    results.Add(RegisterNoLock(action, gesture));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to register hotkey for {Action} ({Gesture}).", action, gesture);
                    results.Add(new HotkeyRegistration(action, gesture, Success: false, Error: ex.Message));
                }
            }

            if (results.All(result => result.Success))
            {
                return results;
            }

            if (previous.Count > 0)
            {
                UnregisterAllNoLock();
                RestoreRegistrations(previous);
            }

            return results;
        }
    }

    /// <inheritdoc />
    public HotkeyRegistration Register(HotkeyAction action, HotkeyGesture gesture)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            CaptureCurrentSynchronizationContext();

            // Replace any prior registration for this action first. Empty means
            // "unassigned", so it should actively clear a previous hotkey.
            HotkeyGesture? previousGesture = _actionToGesture.TryGetValue(action, out HotkeyGesture current)
                ? current
                : null;
            UnregisterNoLock(action);

            try
            {
                HotkeyRegistration result = RegisterNoLock(action, gesture);
                if (!result.Success && previousGesture is { } rollbackGesture)
                {
                    RestoreRegistrations([new ActiveHotkeyRegistration(action, rollbackGesture)]);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to register hotkey for {Action} ({Gesture}).", action, gesture);
                if (previousGesture is { } rollbackGesture)
                {
                    RestoreRegistrations([new ActiveHotkeyRegistration(action, rollbackGesture)]);
                }

                return new HotkeyRegistration(action, gesture, Success: false, Error: ex.Message);
            }
        }
    }

    private HotkeyRegistration RegisterNoLock(HotkeyAction action, HotkeyGesture gesture)
    {
        if (gesture.IsEmpty)
        {
            return new HotkeyRegistration(action, gesture, Success: true);
        }

        if (_hwnd == nint.Zero)
        {
            const string unavailable = "The hotkey message window is unavailable.";
            _logger.LogWarning("Cannot register hotkey for {Action} ({Gesture}): {Error}", action, gesture, unavailable);
            return new HotkeyRegistration(action, gesture, Success: false, Error: unavailable);
        }

        int id = Interlocked.Increment(ref _nextId);
        uint mods = ToModFlags(gesture.Modifiers) | NativeConstants.MOD_NOREPEAT;
        uint vk = gesture.VirtualKey;

        // RegisterHotKey must run on the thread that owns the window.
        bool success = false;
        int lastError = 0;
        bool invoked = InvokeOnPump(() =>
        {
            success = User32.RegisterHotKey(_hwnd, id, mods, vk);
            lastError = success ? 0 : Marshal.GetLastPInvokeError();
        });

        if (!invoked)
        {
            const string unavailable = "The hotkey message pump did not process the registration.";
            return new HotkeyRegistration(action, gesture, Success: false, Error: unavailable);
        }

        if (success)
        {
            _actionToId[action] = id;
            _idToAction[id] = action;
            _actionToGesture[action] = gesture;
            return new HotkeyRegistration(action, gesture, Success: true);
        }

        string error = lastError == NativeConstants.ERROR_HOTKEY_ALREADY_REGISTERED
            ? $"The shortcut {gesture} is already in use by another application."
            : $"Failed to register {gesture} (error {lastError}).";
        _logger.LogWarning("RegisterHotKey failed for {Action} ({Gesture}): {Error}", action, gesture, error);
        return new HotkeyRegistration(action, gesture, Success: false, Error: error);
    }

    /// <inheritdoc />
    public void Unregister(HotkeyAction action)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            UnregisterNoLock(action);
        }
    }

    /// <inheritdoc />
    public void UnregisterAll()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            foreach (HotkeyAction action in _actionToId.Keys.ToArray())
            {
                UnregisterNoLock(action);
            }
        }
    }

    private void StartPumpThread()
    {
        _pumpThread = new Thread(PumpThreadMain)
        {
            Name = "Octadock.Hotkeys",
            IsBackground = true,
        };
        _pumpThread.SetApartmentState(ApartmentState.STA);
        _pumpThread.Start();

        // Wait until the window exists (or creation failed) before returning.
        _ready.Wait(TimeSpan.FromSeconds(5));
    }

    private void PumpThreadMain()
    {
        _threadId = Kernel32.GetCurrentThreadId();
        _hInstance = Kernel32.GetModuleHandleW(nint.Zero);
        _wndProcDelegate = WindowProc;

        try
        {
            RegisterWindowClass();
            CreateMessageWindow();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create the hotkey message window.");
            _ready.Set();
            return;
        }

        _ready.Set();

        // Standard blocking message loop. WM_OCTADOCK_QUIT or WM_QUIT ends it.
        while (!_disposed)
        {
            int result = User32.GetMessage(out MSG msg, nint.Zero, 0, 0);
            if (result <= 0)
            {
                break; // 0 = WM_QUIT, -1 = error
            }

            if (msg.Message == NativeConstants.WM_OCTADOCK_QUIT)
            {
                break;
            }

            // Thread messages (Hwnd == 0) posted via PostThreadMessage do not reach
            // a window procedure, so drain queued cross-thread work here.
            if (msg.Message == NativeConstants.WM_APP && msg.Hwnd == nint.Zero)
            {
                DrainPendingActions();
                continue;
            }

            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }

        CleanupWindow();
    }

    private void RegisterWindowClass()
    {
        nint classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);
        try
        {
            var wndClass = new WNDCLASSEXW
            {
                CbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                LpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate!),
                HInstance = _hInstance,
                LpszClassName = classNamePtr,
            };

            _classAtom = User32.RegisterClassEx(ref wndClass);
            if (_classAtom == 0)
            {
                int err = Marshal.GetLastPInvokeError();
                // 1410 == ERROR_CLASS_ALREADY_EXISTS; tolerate re-registration.
                if (err != 1410)
                {
                    throw new InvalidOperationException($"RegisterClassEx failed (error {err}).");
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }
    }

    private void CreateMessageWindow()
    {
        // Reference the class by name (valid whether we just registered it or it
        // already existed). HWND_MESSAGE makes it a message-only window that only
        // receives messages (no painting, not enumerated, never activated).
        nint classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);
        int createError = 0;
        try
        {
            _hwnd = User32.CreateWindowEx(
                0,
                classNamePtr,
                "Octadock Hotkeys",
                0,
                0,
                0,
                0,
                0,
                NativeConstants.HWND_MESSAGE,
                nint.Zero,
                _hInstance,
                nint.Zero);
            createError = _hwnd == nint.Zero ? Marshal.GetLastPInvokeError() : 0;
        }
        finally
        {
            Marshal.FreeHGlobal(classNamePtr);
        }

        if (_hwnd == nint.Zero)
        {
            throw new InvalidOperationException($"CreateWindowEx failed (error {createError}).");
        }
    }

    private nint WindowProc(nint hWnd, uint msg, nuint wParam, nint lParam)
    {
        if (msg == NativeConstants.WM_HOTKEY)
        {
            int id = (int)wParam;
            if (_idToAction.TryGetValue(id, out HotkeyAction action))
            {
                RaiseHotkey(action);
            }

            return nint.Zero;
        }

        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void RaiseHotkey(HotkeyAction action)
    {
        EventHandler<HotkeyPressedEventArgs>? handler = HotkeyPressed;
        if (handler is null)
        {
            return;
        }

        var args = new HotkeyPressedEventArgs(action);
        SynchronizationContext? syncContext = _syncContext;
        if (syncContext is not null)
        {
            syncContext.Post(_ => handler(this, args), null);
        }
        else
        {
            handler(this, args);
        }
    }

    /// <summary>Runs <paramref name="action"/> synchronously on the pump thread.</summary>
    private bool InvokeOnPump(Action action)
    {
        if (_disposed || _hwnd == nint.Zero)
        {
            return false;
        }

        // If we are already on the pump thread, run inline.
        if (Kernel32.GetCurrentThreadId() == _threadId)
        {
            action();
            return true;
        }

        using var done = new ManualResetEventSlim(false);
        ExceptionDispatchInfo? captured = null;
        int state = 0; // 0 queued, 1 running/ran, 2 abandoned before running

        // Marshal the call by posting a message the WndProc could handle; simplest
        // robust approach: use a queued callback executed via a dedicated message.
        // We reuse a simple thread-safe queue drained on WM_APP.
        _pendingActions.Enqueue(() =>
        {
            if (Interlocked.CompareExchange(ref state, 1, 0) != 0)
            {
                // C-5: abandoned by a timed-out caller — its event is already
                // (or is about to be) disposed and nobody is waiting. Touching
                // it here could throw ObjectDisposedException on the pump
                // thread and kill the pump loop.
                return;
            }

            try
            {
                action();
            }
            catch (Exception ex)
            {
                captured = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                try
                {
                    done.Set();
                }
                catch (ObjectDisposedException)
                {
                    // C-5: the caller timed out mid-action and disposed the
                    // event; there is no waiter left to signal.
                }
            }
        });

        // Nudge the pump to drain the queue.
        if (!User32.PostThreadMessageW(_threadId, NativeConstants.WM_APP, nuint.Zero, nint.Zero))
        {
            int error = Marshal.GetLastPInvokeError();
            Interlocked.CompareExchange(ref state, 2, 0);
            _logger.LogWarning("Failed to post a hotkey operation to the pump thread (error {Error}).", error);
            return false;
        }

        // WM_APP is dispatched to the thread queue (not a window), so it surfaces
        // via GetMessage with hwnd == 0; drain here in the pump loop is handled by
        // DrainPendingActions being invoked from the loop. Wait for completion.
        if (!done.Wait(TimeSpan.FromSeconds(5)))
        {
            Interlocked.CompareExchange(ref state, 2, 0);
            _logger.LogWarning("Timed out marshalling a hotkey operation to the pump thread.");
            return false;
        }

        if (captured is not null)
        {
            captured.Throw();
        }

        return state == 1;
    }

    private readonly ConcurrentQueue<Action> _pendingActions = new();

    private void DrainPendingActions()
    {
        while (_pendingActions.TryDequeue(out Action? work))
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                // C-5: the pump loop must survive a faulting queued operation;
                // a single throw here would otherwise take down global hotkeys
                // for the rest of the session.
                _logger.LogWarning(ex, "A queued hotkey operation threw on the pump thread.");
            }
        }
    }

    private void CleanupWindow()
    {
        try
        {
            if (_hwnd != nint.Zero)
            {
                // Unregister any surviving hotkeys defensively.
                foreach (int id in _idToAction.Keys.ToArray())
                {
                    User32.UnregisterHotKey(_hwnd, id);
                }

                User32.DestroyWindow(_hwnd);
                _hwnd = nint.Zero;
            }

            if (_classAtom != 0 && _hInstance != nint.Zero)
            {
                User32.UnregisterClass(_classAtom, _hInstance);
                _classAtom = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during hotkey window cleanup.");
        }
    }

    private static uint ToModFlags(ModifierKeys modifiers)
    {
        uint flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            flags |= NativeConstants.MOD_ALT;
        }

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            flags |= NativeConstants.MOD_CONTROL;
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            flags |= NativeConstants.MOD_SHIFT;
        }

        if (modifiers.HasFlag(ModifierKeys.Win))
        {
            flags |= NativeConstants.MOD_WIN;
        }

        return flags;
    }

    private IReadOnlyList<ActiveHotkeyRegistration> SnapshotRegistrations()
    {
        var snapshot = new List<ActiveHotkeyRegistration>();
        foreach ((HotkeyAction action, HotkeyGesture gesture) in _actionToGesture.ToArray())
        {
            if (_actionToId.ContainsKey(action))
            {
                snapshot.Add(new ActiveHotkeyRegistration(action, gesture));
            }
        }

        return snapshot;
    }

    private void RestoreRegistrations(IReadOnlyList<ActiveHotkeyRegistration> registrations)
    {
        foreach (ActiveHotkeyRegistration registration in registrations)
        {
            try
            {
                HotkeyRegistration result = RegisterNoLock(registration.Action, registration.Gesture);
                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Failed to restore previous hotkey for {Action} ({Gesture}): {Error}",
                        registration.Action,
                        registration.Gesture,
                        result.Error ?? "registration failed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to restore previous hotkey for {Action} ({Gesture}).",
                    registration.Action,
                    registration.Gesture);
            }
        }
    }

    private void UnregisterNoLock(HotkeyAction action)
    {
        if (_actionToId.TryRemove(action, out int id))
        {
            _idToAction.TryRemove(id, out _);
            _actionToGesture.TryRemove(action, out _);
            InvokeOnPump(() => User32.UnregisterHotKey(_hwnd, id));
        }
    }

    private void UnregisterAllNoLock()
    {
        foreach (HotkeyAction action in _actionToId.Keys.ToArray())
        {
            UnregisterNoLock(action);
        }
    }

    private void CaptureCurrentSynchronizationContext()
    {
        SynchronizationContext? context = SynchronizationContext.Current;
        if (context is not null)
        {
            _syncContext = context;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            UnregisterAllInternal();

            if (_threadId != 0)
            {
                // Wake the pump so it observes _disposed and exits.
                User32.PostThreadMessageW(_threadId, NativeConstants.WM_OCTADOCK_QUIT, nuint.Zero, nint.Zero);
            }

            _pumpThread?.Join(TimeSpan.FromSeconds(3));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error disposing the hotkey service.");
        }
        finally
        {
            _ready.Dispose();
        }
    }

    private void UnregisterAllInternal()
    {
        // Best-effort, without marshalling (we are tearing down).
        foreach (int id in _idToAction.Keys.ToArray())
        {
            try
            {
                User32.UnregisterHotKey(_hwnd, id);
            }
            catch
            {
                // ignore during teardown
            }
        }

        _actionToId.Clear();
        _idToAction.Clear();
        _actionToGesture.Clear();
    }

    private sealed record ActiveHotkeyRegistration(HotkeyAction Action, HotkeyGesture Gesture);
}
