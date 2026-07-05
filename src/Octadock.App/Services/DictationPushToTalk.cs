using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Hotkeys;
using Octadock.Core.Settings;
using Octadock.Core.Speech;
using Octadock.Platform.Windows.Hotkeys;

namespace Octadock.App.Services;

/// <summary>
/// Owns the dictation activation mode. In "toggle" the plain Dictation hotkey
/// does the work and no keyboard hook exists. In "hold"/"both" this hands the
/// gesture to <see cref="DictationGestureMonitor"/> (unregistering the hotkey
/// so both never fire together) and drives the controller from key
/// down/up pairs using <see cref="DictationGestureInterpreter"/>'s
/// tap-versus-hold policy.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public sealed class DictationPushToTalk : IDisposable
{
    private readonly DictationController _controller;
    private readonly DictationGestureMonitor _monitor;
    private readonly IHotkeyService _hotkeys;
    private readonly ISettingsService _settings;
    private readonly ILogger<DictationPushToTalk> _logger;

    private string _mode = SpeechSettings.DefaultActivationMode;
    private DateTimeOffset _downAt;
    private bool _startedByThisPress;
    private Task _startTask = Task.CompletedTask;

    public DictationPushToTalk(
        DictationController controller,
        DictationGestureMonitor monitor,
        IHotkeyService hotkeys,
        ISettingsService settings,
        ILogger<DictationPushToTalk> logger)
    {
        _controller = controller;
        _monitor = monitor;
        _hotkeys = hotkeys;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Applies the current mode and follows settings changes. Call once at startup (UI thread).</summary>
    public void Initialize()
    {
        _monitor.GestureDown += OnGestureDown;
        _monitor.GestureUp += OnGestureUp;
        Apply(_settings.Current);

        // Posted (not inline): the settings save path re-registers all hotkeys
        // in the same dispatcher frame, so applying afterwards keeps this class
        // the last word on who owns the dictation gesture.
        _settings.Changed += (_, e) =>
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(e.Settings));
    }

    private void Apply(OctadockSettings settings)
    {
        string mode = NormalizeMode(settings.Speech.ActivationMode);
        HotkeyGesture gesture = settings.Shortcuts.Dictation;
        _mode = mode;

        if (mode == SpeechSettings.ActivationModeToggle)
        {
            _monitor.Stop();
            // Re-claim the plain hotkey in case a previous hold/both session
            // unregistered it (Register replaces any existing registration).
            _hotkeys.Register(HotkeyAction.Dictation, gesture);
            return;
        }

        _hotkeys.Unregister(HotkeyAction.Dictation);
        _monitor.Configure(gesture);
        _logger.LogInformation(
            "Dictation activation mode '{Mode}' active on {Gesture}.", mode, gesture);
    }

    private void OnGestureDown(object? sender, EventArgs e)
    {
        _downAt = DateTimeOffset.UtcNow;
        _startedByThisPress = false;
        if (DictationGestureInterpreter.OnPress(_controller.IsListening) == DictationGestureAction.Start)
        {
            _startedByThisPress = true;
            _startTask = _controller.ToggleAsync();
        }
    }

    private async void OnGestureUp(object? sender, EventArgs e)
    {
        try
        {
            // A very quick release can beat the start still in flight; let the
            // start finish so stop/discard act on settled state.
            await _startTask.ConfigureAwait(true);

            TimeSpan heldFor = DateTimeOffset.UtcNow - _downAt;
            DictationGestureAction action = DictationGestureInterpreter.OnRelease(
                _mode, _startedByThisPress, heldFor, _controller.IsListening);
            switch (action)
            {
                case DictationGestureAction.StopAndInsert:
                    await _controller.ToggleAsync().ConfigureAwait(true);
                    break;
                case DictationGestureAction.Discard:
                    await _controller.DiscardAsync().ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Dictation gesture release handling failed.");
        }
    }

    private static string NormalizeMode(string? mode)
    {
        if (string.Equals(mode, SpeechSettings.ActivationModeHold, StringComparison.OrdinalIgnoreCase))
        {
            return SpeechSettings.ActivationModeHold;
        }

        if (string.Equals(mode, SpeechSettings.ActivationModeBoth, StringComparison.OrdinalIgnoreCase))
        {
            return SpeechSettings.ActivationModeBoth;
        }

        return SpeechSettings.ActivationModeToggle;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _monitor.GestureDown -= OnGestureDown;
        _monitor.GestureUp -= OnGestureUp;
        _monitor.Dispose();
    }
}
