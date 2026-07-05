using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Octadock.Core.Hotkeys;

namespace Octadock.Platform.Windows.Hotkeys;

/// <summary>
/// Watches the dictation gesture with a low-level keyboard hook so hold-to-talk
/// can observe both key-down and key-up (RegisterHotKey only reports downs).
/// Runs only in the "hold"/"both" activation modes — installing a global
/// WH_KEYBOARD_LL hook is opt-in by design. The hook callback does near-zero
/// work: match the terminal key, check modifiers, swallow the chord, and post
/// the event elsewhere. Injected input (our own Ctrl+V paste) is ignored so
/// inserting a transcript can never re-trigger dictation. A watchdog timer
/// covers key-ups the hook never sees (e.g. released over an elevated window
/// or the secure desktop).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DictationGestureMonitor : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint LlkhfInjected = 0x10;
    private const uint WmQuit = 0x0012;

    private readonly ILogger<DictationGestureMonitor> _logger;
    private readonly object _gate = new();
    private readonly LowLevelKeyboardProc _hookProc; // Kept alive for the hook's lifetime.

    private Thread? _hookThread;
    private uint _hookThreadId;
    private nint _hookHandle;
    private volatile bool _running;
    private volatile uint _virtualKey;
    private volatile int _modifiers; // ModifierKeys as int for volatile access.
    private SynchronizationContext? _syncContext;
    private Timer? _watchdog;
    private bool _down;

    /// <summary>Creates the monitor; no hook is installed until <see cref="Configure"/>.</summary>
    public DictationGestureMonitor(ILogger<DictationGestureMonitor> logger)
    {
        _logger = logger;
        _hookProc = HookCallback;
    }

    /// <summary>Raised when the gesture chord goes down (posted to the configuring thread's context).</summary>
    public event EventHandler? GestureDown;

    /// <summary>Raised when the gesture's terminal key is released (or the watchdog detects it).</summary>
    public event EventHandler? GestureUp;

    /// <summary>True while the hook is installed.</summary>
    public bool IsRunning => _running;

    /// <summary>
    /// Sets (or updates) the watched gesture and installs the hook if needed.
    /// Call from the UI thread so gesture events marshal back to it.
    /// </summary>
    public void Configure(HotkeyGesture gesture)
    {
        lock (_gate)
        {
            _syncContext = SynchronizationContext.Current ?? _syncContext;
            _virtualKey = gesture.VirtualKey;
            _modifiers = (int)gesture.Modifiers;

            if (gesture.IsEmpty)
            {
                StopNoLock();
                return;
            }

            if (_running)
            {
                return; // Hook already installed; it reads the new gesture per event.
            }

            _running = true;
            _hookThread = new Thread(HookThreadMain)
            {
                Name = "Octadock.DictationGesture",
                IsBackground = true,
            };
            _hookThread.SetApartmentState(ApartmentState.STA);
            _hookThread.Start();
        }
    }

    /// <summary>Uninstalls the hook (returning to plain hotkey behavior).</summary>
    public void Stop()
    {
        lock (_gate)
        {
            StopNoLock();
        }
    }

    private void StopNoLock()
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        if (_hookThreadId != 0)
        {
            PostThreadMessageW(_hookThreadId, WmQuit, nuint.Zero, nint.Zero);
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _hookThread = null;
        _hookThreadId = 0;
        StopWatchdog();
        _down = false;
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        _hookHandle = SetWindowsHookExW(
            WhKeyboardLl, _hookProc, GetModuleHandleW(nint.Zero), 0);
        if (_hookHandle == nint.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            _logger.LogWarning("Failed to install the dictation key hook (error {Error}).", error);
            _running = false;
            return;
        }

        _logger.LogInformation("Dictation push-to-talk hook installed.");

        // LL hook callbacks are delivered through this thread's message loop.
        while (_running && GetMessage(out MSGNATIVE msg, nint.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = nint.Zero;
        _logger.LogInformation("Dictation push-to-talk hook removed.");
    }

    private nint HookCallback(int code, nuint wParam, nint lParam)
    {
        if (code < 0)
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);

        // Never react to (or swallow) input we synthesized ourselves — the
        // paste at the end of dictation must not re-enter the gesture.
        if ((data.Flags & LlkhfInjected) != 0 || data.VkCode != _virtualKey)
        {
            return CallNextHookEx(_hookHandle, code, wParam, lParam);
        }

        int message = (int)wParam;
        bool isDown = message is WmKeyDown or WmSysKeyDown;
        bool isUp = message is WmKeyUp or WmSysKeyUp;

        if (isDown)
        {
            if (_down)
            {
                return 1; // Key-repeat while held: swallow, no re-raise.
            }

            if (!RequiredModifiersDown((ModifierKeys)_modifiers))
            {
                return CallNextHookEx(_hookHandle, code, wParam, lParam);
            }

            _down = true;
            StartWatchdog();
            Raise(GestureDown);
            return 1; // Swallow so the terminal key never types into the app.
        }

        if (isUp && _down)
        {
            _down = false;
            StopWatchdog();
            Raise(GestureUp);
            return 1;
        }

        return CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private static bool RequiredModifiersDown(ModifierKeys required)
    {
        static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

        if (required.HasFlag(ModifierKeys.Control) && !IsDown(0x11))
        {
            return false;
        }

        if (required.HasFlag(ModifierKeys.Shift) && !IsDown(0x10))
        {
            return false;
        }

        if (required.HasFlag(ModifierKeys.Alt) && !IsDown(0x12))
        {
            return false;
        }

        if (required.HasFlag(ModifierKeys.Win) && !IsDown(0x5B) && !IsDown(0x5C))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// While the key is "down", polls its real state: if the up event was
    /// swallowed elsewhere (elevated window, secure desktop), synthesize the
    /// release so hold-to-talk cannot get stuck listening.
    /// </summary>
    private void StartWatchdog()
    {
        StopWatchdog();
        _watchdog = new Timer(
            _ =>
            {
                if (_down && (GetAsyncKeyState((int)_virtualKey) & 0x8000) == 0)
                {
                    _down = false;
                    StopWatchdog();
                    _logger.LogDebug("Dictation gesture release recovered by the watchdog.");
                    Raise(GestureUp);
                }
            },
            null,
            dueTime: TimeSpan.FromMilliseconds(200),
            period: TimeSpan.FromMilliseconds(200));
    }

    private void StopWatchdog()
    {
        _watchdog?.Dispose();
        _watchdog = null;
    }

    private void Raise(EventHandler? handler)
    {
        if (handler is null)
        {
            return;
        }

        SynchronizationContext? context = _syncContext;
        if (context is not null)
        {
            context.Post(_ => handler(this, EventArgs.Empty), null);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => handler(this, EventArgs.Empty));
        }
    }

    /// <inheritdoc />
    public void Dispose() => Stop();

    private delegate nint LowLevelKeyboardProc(int code, nuint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public nuint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSGNATIVE
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(int idHook, LowLevelKeyboardProc lpfn, nint hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(nint hhk);

    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSGNATIVE lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSGNATIVE lpMsg);

    [DllImport("user32.dll")]
    private static extern nint DispatchMessage(ref MSGNATIVE lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessageW(uint idThread, uint msg, nuint wParam, nint lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandleW(nint lpModuleName);
}
