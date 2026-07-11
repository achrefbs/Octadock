using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;

namespace Octadock.Core.Licensing;

/// <summary>A feature the trial/license gate can block (POST_EXPIRY_VERB_MATRIX.md).</summary>
public enum GatedFeature
{
    Capture,
    Ocr,
    Dictation,
    ReadAloud,
    FilePreview,
    Pin,
    Recording,
    AddShelfItem,
    Annotate,
    TextTools,
    AiActions,

    /// <summary>Creating/adding to a Context package (viewing/exporting existing is never gated).</summary>
    Context,
}

/// <summary>
/// The trial/license gate the service seams consult (WS5, R16). Injected as an
/// interface so seam services can be unit-tested with a trivial allow/deny fake.
/// </summary>
public interface ILicenseGate
{
    /// <summary>The current resolved license state (for ambient status surfaces).</summary>
    LicenseState State { get; }

    /// <summary>True when full use is allowed (active trial or a valid license).</summary>
    bool AllowsFullUse { get; }

    /// <summary>Raised whenever a gated feature is refused (drives ambient UI, e.g. the dock badge).</summary>
    event EventHandler<LicenseState>? Refused;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="feature"/> may run now. When it may not,
    /// raises the announced expiry prompt and returns <c>false</c>.
    /// </summary>
    bool Allow(GatedFeature feature);
}

/// <summary>
/// The single trial/license gate the service seams consult (WS5, R16/R31). It is
/// enforced at the service seams — never at scattered UI call sites — so every entry
/// point (dock, hotkey, <c>octadock://</c>, CLI, Explorer association) obeys the same
/// rule. It blocks NEW content/compute after the trial ends (or a license is revoked)
/// but never blocks viewing/exporting data the user already has, and routes every
/// refusal to one non-modal, screen-reader-announced notification that deep-links to
/// Account &amp; Billing — so a silent gate can never read as "the app broke".
/// </summary>
public sealed class LicenseGate : ILicenseGate
{
    /// <summary>How often a refusal may raise a toast, so a held hotkey can't spam.</summary>
    private static readonly TimeSpan PromptThrottle = TimeSpan.FromSeconds(5);

    private readonly LicenseStateService _license;
    private readonly INotificationService _notifications;
    private readonly IWindowPresenter _presenter;
    private readonly IClock _clock;
    private readonly ILogger<LicenseGate>? _logger;
    private readonly object _promptGate = new();
    private DateTimeOffset _lastPrompt = DateTimeOffset.MinValue;

    /// <summary>Raised whenever a gated feature is refused (drives ambient UI, e.g. the dock badge).</summary>
    public event EventHandler<LicenseState>? Refused;

    public LicenseGate(
        LicenseStateService license,
        INotificationService notifications,
        IWindowPresenter presenter,
        IClock clock,
        ILogger<LicenseGate>? logger = null)
    {
        _license = license ?? throw new ArgumentNullException(nameof(license));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger;
    }

    /// <summary>The current resolved license state (for ambient status surfaces).</summary>
    public LicenseState State => _license.Current();

    /// <summary>True when full use is allowed (active trial or a valid license).</summary>
    public bool AllowsFullUse => _license.Current().AllowsFullUse;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="feature"/> may run now. When it may not
    /// (trial ended / revoked / clock frozen) it raises the announced expiry prompt and
    /// returns <c>false</c>; the caller returns without doing the work.
    /// </summary>
    public bool Allow(GatedFeature feature)
    {
        LicenseState state = _license.Current();
        if (state.AllowsFullUse)
        {
            return true;
        }

        Refuse(state, feature);
        return false;
    }

    private void Refuse(LicenseState state, GatedFeature feature)
    {
        _logger?.LogInformation("Blocked {Feature}: license mode is {Mode}.", feature, state.Mode);
        Refused?.Invoke(this, state);

        if (!ShouldPrompt())
        {
            return;
        }

        (string title, string message) = Describe(state);
        _notifications.Notify(title, message, NotificationKind.Warning, () => _presenter.ShowSettings("account"));
    }

    private bool ShouldPrompt()
    {
        lock (_promptGate)
        {
            DateTimeOffset now = _clock.UtcNow;
            if (now - _lastPrompt < PromptThrottle)
            {
                return false;
            }

            _lastPrompt = now;
            return true;
        }
    }

    private static (string Title, string Message) Describe(LicenseState state) => state.Mode switch
    {
        LicenseMode.Revoked => (
            "Octadock license revoked",
            "This license is no longer active. Enter a valid license key in Settings → Account to keep using Octadock. Your existing captures stay available."),
        LicenseMode.TrialFrozen => (
            "Octadock trial paused",
            "The trial countdown is paused because this PC's clock looks wrong. Fix the clock, or enter a license key in Settings → Account."),
        _ => (
            "Octadock trial ended",
            "Your free trial has ended. Buy a license or enter your key in Settings → Account to keep capturing. Your existing captures stay available."),
    };
}
