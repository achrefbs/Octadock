using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Imaging;

namespace Octadock.App.Clipboard;

/// <summary>
/// <see cref="IClipboardSnapshotSource"/> over the WPF clipboard. Checks the
/// standard clipboard-privacy formats used by password managers, skips
/// Octadock-origin writes (so restoring a clip never re-records it), and stamps
/// the snapshot with the foreground window's process/title for provenance.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WpfClipboardSnapshotSource : IClipboardSnapshotSource
{
    /// <summary>Set by password managers and privacy tools to opt out of clipboard monitors.</summary>
    internal const string ExcludeFromMonitorFormat = "ExcludeClipboardContentFromMonitorProcessing";

    /// <summary>Windows clipboard-history opt-out format (DWORD 0 means "do not keep").</summary>
    internal const string CanIncludeInHistoryFormat = "CanIncludeInClipboardHistory";

    /// <summary>Cloud-clipboard opt-out format; treated as a privacy signal too.</summary>
    internal const string CanUploadToCloudFormat = "CanUploadToCloudClipboard";

    private readonly IClipboardService _clipboard;
    private readonly ILogger<WpfClipboardSnapshotSource> _logger;

    /// <summary>Creates the snapshot source.</summary>
    public WpfClipboardSnapshotSource(IClipboardService clipboard, ILogger<WpfClipboardSnapshotSource> logger)
    {
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ClipboardSnapshot? TryRead(bool includeImages)
    {
        if (IsOwnWrite())
        {
            return null;
        }

        if (OnSta(HasPrivacyOptOut))
        {
            LogSkippedPrivateContent();
            return null;
        }

        (string? sourceProcess, string? sourceWindow) = ReadForegroundInfo();

        string? text = _clipboard.TryGetText();
        if (!string.IsNullOrWhiteSpace(text))
        {
            return new ClipboardSnapshot
            {
                Text = text,
                SourceProcess = sourceProcess,
                SourceWindow = sourceWindow,
            };
        }

        if (includeImages)
        {
            EncodedImage? image = _clipboard.TryGetImage();
            if (image is not null)
            {
                return new ClipboardSnapshot
                {
                    Image = image,
                    SourceProcess = sourceProcess,
                    SourceWindow = sourceWindow,
                };
            }
        }

        return null;
    }

    /// <summary>
    /// True when any standard privacy format asks monitors to ignore the current
    /// clipboard content. Unreadable opt-out values are treated as opted out.
    /// </summary>
    private bool HasPrivacyOptOut()
    {
        try
        {
            IDataObject? data = System.Windows.Clipboard.GetDataObject();
            if (data is null)
            {
                return false;
            }

            if (data.GetDataPresent(ExcludeFromMonitorFormat, autoConvert: false))
            {
                return true;
            }

            if (data.GetDataPresent(CanIncludeInHistoryFormat, autoConvert: false) &&
                !ReadsAsNonZeroDword(data, CanIncludeInHistoryFormat))
            {
                return true;
            }

            if (data.GetDataPresent(CanUploadToCloudFormat, autoConvert: false) &&
                !ReadsAsNonZeroDword(data, CanUploadToCloudFormat))
            {
                // Cloud opt-out alone does not forbid local history, but many
                // password managers set all three; treat an explicit zero as a
                // privacy signal and stay out of the way.
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            LogPrivacyFormatCheckFailed(ex);
            // Fail closed: if we cannot inspect the formats we do not record.
            return true;
        }
    }

    private static bool ReadsAsNonZeroDword(IDataObject data, string format)
    {
        try
        {
            object? value = data.GetData(format, autoConvert: false);
            if (value is System.IO.MemoryStream stream)
            {
                Span<byte> buffer = stackalloc byte[4];
                stream.Position = 0;
                int read = stream.Read(buffer);
                return read == 4 && BitConverter.ToUInt32(buffer) != 0;
            }

            return value is uint u ? u != 0 : value is int i && i != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>True when the clipboard owner window belongs to this process.</summary>
    private static bool IsOwnWrite()
    {
        IntPtr owner = GetClipboardOwner();
        if (owner == IntPtr.Zero)
        {
            return false;
        }

        _ = GetWindowThreadProcessId(owner, out uint pid);
        return pid == (uint)Environment.ProcessId;
    }

    private (string? Process, string? Window) ReadForegroundInfo()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero)
            {
                return (null, null);
            }

            string? title = ReadWindowTitle(hwnd);

            _ = GetWindowThreadProcessId(hwnd, out uint pid);
            string? process = null;
            if (pid != 0 && pid != (uint)Environment.ProcessId)
            {
                using var p = Process.GetProcessById((int)pid);
                process = p.ProcessName + ".exe";
            }

            return (process, title);
        }
        catch (Exception ex)
        {
            LogForegroundInfoFailed(ex);
            return (null, null);
        }
    }

    private static string? ReadWindowTitle(IntPtr hwnd)
    {
        int length = GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return null;
        }

        var buffer = new char[length + 1];
        int copied = GetWindowText(hwnd, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : null;
    }

    private static T OnSta<T>(Func<T> func)
    {
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetClipboardOwner();

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetWindowTextW")]
    private static extern int GetWindowText(IntPtr hwnd, [Out] char[] text, int maxCount);

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Skipping clipboard content marked private by the source application.")]
    private partial void LogSkippedPrivateContent();

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Could not inspect clipboard privacy formats; skipping this clip.")]
    private partial void LogPrivacyFormatCheckFailed(Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug,
        Message = "Could not resolve the foreground window for clip provenance.")]
    private partial void LogForegroundInfoFailed(Exception exception);
}
