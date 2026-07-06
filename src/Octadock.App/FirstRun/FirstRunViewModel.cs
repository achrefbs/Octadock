using System.Runtime.Versioning;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.FirstRun;

/// <summary>
/// View model for the one-time first-run wizard. Explains capture permissions and
/// limitations, offers launch-at-login, and marks
/// <c>General.FirstRunCompleted = true</c> so the wizard never shows again.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class FirstRunViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly IStartupRegistration _startup;
    private readonly ICaptureExclusion _captureExclusion;
    private readonly ILogger<FirstRunViewModel> _logger;

    [ObservableProperty]
    private bool _launchAtLogin = true;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>True when the user chose "I have a license key" so the caller opens Account &amp; Billing.</summary>
    public bool WantsLicenseEntry { get; private set; }

    /// <summary>Creates the first-run view model.</summary>
    public FirstRunViewModel(
        ISettingsService settings,
        IStartupRegistration startup,
        ICaptureExclusion captureExclusion,
        ILogger<FirstRunViewModel> logger)
    {
        _settings = settings;
        _startup = startup;
        _captureExclusion = captureExclusion;
        _logger = logger;
        _launchAtLogin = settings.Current.General.LaunchAtLogin;
    }

    /// <summary>Whether the OS supports excluding Octadock's own windows from captures.</summary>
    public bool CaptureExclusionSupported => _captureExclusion.IsSupported;

    /// <summary>A one-line note about capture-exclusion support.</summary>
    public string CaptureExclusionNote => CaptureExclusionSupported
        ? "Your Windows version supports hiding Octadock's own overlays from captures."
        : "On this Windows build, Octadock overlays may appear in captures. Update to Windows 10 2004+ for full exclusion.";

    /// <summary>Raised when the wizard is finished so the window can close.</summary>
    public event EventHandler? Completed;

    /// <summary>Finishes first-run and signals the caller to open Account &amp; Billing for key entry.</summary>
    [RelayCommand]
    private async Task EnterLicenseKeyAsync()
    {
        WantsLicenseEntry = true;
        await FinishAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        try
        {
            StatusMessage = null;

            await _settings.UpdateAsync(s => s with
            {
                General = s.General with
                {
                    FirstRunCompleted = true,
                    LaunchAtLogin = LaunchAtLogin,
                },
            }).ConfigureAwait(true);

            ApplyLaunchAtLogin(LaunchAtLogin);
            Completed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to complete first-run setup.");
            StatusMessage = "Could not save setup. Please try again.";
        }
    }

    private void ApplyLaunchAtLogin(bool enabled)
    {
        try
        {
            if (enabled && !_startup.IsEnabled())
            {
                _startup.Enable();
            }
            else if (!enabled)
            {
                _startup.Disable();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set launch-at-login during first run.");
        }
    }
}
