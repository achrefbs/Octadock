using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using Octadock.Platform.Windows.Input;

namespace Octadock.App.Services;

/// <summary>
/// Test seam over the Win32/WPF primitives dictation insertion relies on:
/// whole-clipboard snapshot/restore (every format, not just text), the
/// clipboard sequence number that detects a foreign write after ours, the
/// foreground window that receives the paste, and the synthetic Ctrl+V
/// itself. The production implementation is
/// <see cref="Win32DictationInsertionBackend"/>; controller tests substitute a
/// fake to prove insertion honesty (no false success, no duplicate paste,
/// exact clipboard restoration).
/// </summary>
internal interface IDictationInsertionBackend
{
    /// <summary>Snapshots the entire clipboard (every format), or null when another app holds it open.</summary>
    IDataObject? SnapshotClipboard();

    /// <summary>Restores a previous snapshot over Octadock's transcript write.</summary>
    void RestoreClipboard(IDataObject snapshot);

    /// <summary>Clipboard sequence number used to detect foreign writes; 0 when unsupported.</summary>
    uint GetClipboardSequence();

    /// <summary>The current foreground window handle, or 0 when no window has focus.</summary>
    nint GetForegroundWindowHandle();

    /// <summary>
    /// Best-effort foreground restore after a review interaction (the pill held
    /// focus while the user edited); false when Windows refused.
    /// </summary>
    bool SetForegroundWindowHandle(nint hwnd);

    /// <summary>Injects one Ctrl+V; true only when every input event was accepted.</summary>
    bool SendPaste();

    /// <summary>Waits (bounded) for physically held modifiers to clear before pasting.</summary>
    void WaitForModifierRelease(TimeSpan timeout);

    /// <summary>How long to wait for the paste to land before restoring the snapshot.</summary>
    TimeSpan ClipboardRestoreDelay { get; }
}

/// <summary>Production <see cref="IDictationInsertionBackend"/> over the WPF clipboard and SendInput.</summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class Win32DictationInsertionBackend : IDictationInsertionBackend
{
    /// <inheritdoc />
    public TimeSpan ClipboardRestoreDelay { get; } = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public IDataObject? SnapshotClipboard()
    {
        try
        {
            return global::System.Windows.Clipboard.GetDataObject();
        }
        catch
        {
            return null; // Another app holds the clipboard open; skip restore.
        }
    }

    /// <inheritdoc />
    public void RestoreClipboard(IDataObject snapshot)
        => global::System.Windows.Clipboard.SetDataObject(snapshot, copy: true);

    /// <inheritdoc />
    public uint GetClipboardSequence() => GetClipboardSequenceNumber();

    /// <inheritdoc />
    public nint GetForegroundWindowHandle() => GetForegroundWindow();

    /// <inheritdoc />
    public bool SetForegroundWindowHandle(nint hwnd) => hwnd != 0 && SetForegroundWindow(hwnd);

    /// <inheritdoc />
    public bool SendPaste() => KeyboardInjector.SendPaste();

    /// <inheritdoc />
    public void WaitForModifierRelease(TimeSpan timeout) => KeyboardInjector.WaitForModifierRelease(timeout);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint hWnd);
}
