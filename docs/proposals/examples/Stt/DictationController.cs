// Example implementation for the STT roadmap.
// Target location: src/Octadock.App/Stt/ — the end-to-end dictation flow.
//
// Toggle mode (phase 1): chord starts recording + shows the waveform HUD;
// same chord stops, transcribes, and inserts. Toggle first because WM_HOTKEY
// has no key-up event — hold-to-talk needs a low-level hook (phase 2).
//
// Insert modes:
//   PasteAtCursor — save clipboard → set transcript → simulate Ctrl+V →
//                   restore clipboard. Falls back to ClipboardOnly when the
//                   foreground window rejects synthetic input (elevated apps).
//   ClipboardOnly — transcript on the clipboard + a notification.
//   ShelfItem     — transcript docked on the Capture Shelf as a text item.

using System.Windows;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Stt;

public sealed class DictationController
{
    private readonly ISpeechToTextProvider _stt;                       // Via factory in real wiring.
    private readonly Platform.Windows.Audio.AudioCaptureService _audio;
    private readonly IClipboardService _clipboard;
    private readonly INotificationService _notifications;
    private readonly ISettingsService _settings;
    private readonly ILogger<DictationController> _logger;
    private readonly object _gate = new();

    private bool _recording;
    private DictationHudWindow? _hud;                                  // ToolWindowBase + waveform; capture-excluded for free.

    public DictationController(
        ISpeechToTextProvider stt,
        Platform.Windows.Audio.AudioCaptureService audio,
        IClipboardService clipboard,
        INotificationService notifications,
        ISettingsService settings,
        ILogger<DictationController> logger)
    {
        _stt = stt;
        _audio = audio;
        _clipboard = clipboard;
        _notifications = notifications;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Wired to HotkeyAction.Dictate in App.HandleHotkeyPressedAsync.</summary>
    public Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_recording)
            {
                StartRecording();
                return Task.CompletedTask;
            }

            _recording = false;
        }

        return StopAndInsertAsync(cancellationToken);
    }

    private void StartRecording()
    {
        try
        {
            _audio.Start();
        }
        catch (Platform.Windows.Audio.MicrophoneAccessDeniedException)
        {
            _notifications.Notify(
                "Microphone blocked",
                "Windows privacy settings block Octadock. Opening the setting…",
                NotificationKind.Warning);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "ms-settings:privacy-microphone") { UseShellExecute = true });
            return;
        }

        _recording = true;
        _hud ??= new DictationHudWindow(() => _audio.LastPeak);
        _hud.ShowNearCursor(); // Small waveform pill; Esc cancels, chord stops.
    }

    private async Task StopAndInsertAsync(CancellationToken cancellationToken)
    {
        AudioBuffer utterance = _audio.Stop();
        _hud?.ShowTranscribing(); // "…" state; first use also covers factory warm-up.

        try
        {
            var options = new SttOptions
            {
                // Language + replacements come from Settings → Speech to text.
                Language = null,
                Replacements = DefaultCodeDictionary,
            };

            SttResult result = await Task.Run(
                () => _stt.TranscribeAsync(utterance, options, cancellationToken), cancellationToken)
                .ConfigureAwait(true); // UI thread for clipboard + SendInput.

            if (result.IsEmpty)
            {
                _notifications.Notify("Dictation", "No speech detected.", NotificationKind.Info);
                return;
            }

            Insert(result.Text, SttInsertMode.PasteAtCursor); // Mode from settings in real wiring.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dictation failed.");
            _notifications.Notify("Dictation failed", ex.Message, NotificationKind.Error);
        }
        finally
        {
            _hud?.Hide();
        }
    }

    private void Insert(string text, SttInsertMode mode)
    {
        switch (mode)
        {
            case SttInsertMode.PasteAtCursor:
                // Save → set → Ctrl+V → restore. The restore delay lets the target
                // app read the clipboard before we put the old contents back.
                IDataObject? saved = TrySnapshotClipboard();
                _clipboard.SetText(text);
                bool pasted = SendCtrlV();
                if (!pasted)
                {
                    // UIPI (elevated window) or no focused control: degrade gracefully.
                    _notifications.Notify(
                        "Dictation", "Copied to clipboard (the focused window rejected input).",
                        NotificationKind.Info);
                    return; // Skip restore: the transcript IS the clipboard now.
                }

                if (saved is not null)
                {
                    _ = RestoreClipboardLaterAsync(saved);
                }

                break;

            case SttInsertMode.ClipboardOnly:
                _clipboard.SetText(text);
                _notifications.Notify("Dictation", "Transcript copied to clipboard.", NotificationKind.Success);
                break;

            case SttInsertMode.ShelfItem:
                // _shelf.AddTextItemAsync(text) — reuses the AddShelfItem path.
                break;
        }
    }

    /// <summary>Ctrl+V via SendInput. Returns false when the OS swallows it (UIPI).</summary>
    private static bool SendCtrlV()
    {
        // P/Invoke sketch — real code goes in Platform.Windows.Input.KeyboardInjector:
        //   INPUT[4]: Ctrl down, V down, V up, Ctrl up  →  SendInput(4, inputs, sizeof(INPUT))
        //   return SendInput(...) == 4;
        return Platform.Windows.Input.KeyboardInjector.SendPaste();
    }

    private static async Task RestoreClipboardLaterAsync(IDataObject saved)
    {
        await Task.Delay(300).ConfigureAwait(true); // Let the paste land first.
        try
        {
            Clipboard.SetDataObject(saved, copy: true);
        }
        catch
        {
            // Clipboard contention is non-fatal; the transcript simply stays on it.
        }
    }

    private static IDataObject? TrySnapshotClipboard()
    {
        try
        {
            return Clipboard.GetDataObject();
        }
        catch
        {
            return null; // Another app holds the clipboard open; skip restore.
        }
    }

    /// <summary>Starter dictation dictionary; user-editable in settings.</summary>
    private static readonly IReadOnlyList<KeyValuePair<string, string>> DefaultCodeDictionary =
    [
        new("arrow function", "=>"),
        new("triple equals", "==="),
        new("not equals", "!="),
        new("open brace", "{"),
        new("close brace", "}"),
        new("pipe operator", "|>"),
        new("async await", "async/await"),
    ];
}
