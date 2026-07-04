using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace Octadock.App.Clipboard;

/// <summary>Raises an event whenever the Windows clipboard content changes.</summary>
public interface IClipboardMonitor : IDisposable
{
    /// <summary>Fired on the UI thread for every <c>WM_CLIPBOARDUPDATE</c>.</summary>
    event EventHandler? ClipboardChanged;

    /// <summary>Starts listening. Safe to call repeatedly.</summary>
    void Start();

    /// <summary>Stops listening. Safe to call repeatedly.</summary>
    void Stop();
}

/// <summary>
/// <see cref="IClipboardMonitor"/> over a message-only window registered with
/// <c>AddClipboardFormatListener</c>. Windows delivers <c>WM_CLIPBOARDUPDATE</c>
/// for every clipboard change without the fragile legacy viewer chain.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class ClipboardMonitor : IClipboardMonitor
{
    private const int WmClipboardUpdate = 0x031D;
    private static readonly IntPtr MessageOnlyParent = new(-3); // HWND_MESSAGE

    private readonly ILogger<ClipboardMonitor> _logger;
    private HwndSource? _source;
    private bool _listening;

    /// <summary>Creates the monitor.</summary>
    public ClipboardMonitor(ILogger<ClipboardMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public event EventHandler? ClipboardChanged;

    /// <inheritdoc />
    public void Start() => OnUi(() =>
    {
        if (_listening)
        {
            return;
        }

        try
        {
            _source ??= CreateMessageWindow();
            if (!AddClipboardFormatListener(_source.Handle))
            {
                LogListenerRegistrationFailed(Marshal.GetLastPInvokeError());
                return;
            }

            _listening = true;
        }
        catch (Exception ex)
        {
            LogMonitorStartFailed(ex);
        }
    });

    /// <inheritdoc />
    public void Stop() => OnUi(() =>
    {
        if (!_listening || _source is null)
        {
            return;
        }

        _ = RemoveClipboardFormatListener(_source.Handle);
        _listening = false;
    });

    /// <inheritdoc />
    public void Dispose() => OnUi(() =>
    {
        Stop();
        _source?.Dispose();
        _source = null;
    });

    private HwndSource CreateMessageWindow()
    {
        var parameters = new HwndSourceParameters("OctadockClipboardMonitor")
        {
            Width = 0,
            Height = 0,
            PositionX = 0,
            PositionY = 0,
            WindowStyle = 0,
            ParentWindow = MessageOnlyParent,
        };

        var source = new HwndSource(parameters);
        source.AddHook(WndProc);
        return source;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmClipboardUpdate)
        {
            try
            {
                ClipboardChanged?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                LogClipboardChangedHandlerThrew(ex);
            }

            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void OnUi(Action action)
    {
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AddClipboardFormatListener(IntPtr hwnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool RemoveClipboardFormatListener(IntPtr hwnd);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "AddClipboardFormatListener failed with Win32 error {Error}; clipboard history is inactive.")]
    private partial void LogListenerRegistrationFailed(int error);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning,
        Message = "The clipboard monitor could not start.")]
    private partial void LogMonitorStartFailed(Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Warning,
        Message = "A clipboard-changed handler threw.")]
    private partial void LogClipboardChangedHandlerThrew(Exception exception);
}
