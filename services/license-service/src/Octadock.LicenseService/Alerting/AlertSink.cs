using Microsoft.Extensions.Logging;

namespace Octadock.LicenseService.Alerting;

/// <summary>Severity of a founder alert. Maps to the sink's delivery channel/urgency.</summary>
public enum AlertSeverity
{
    /// <summary>A degradation worth watching (logged at Warning).</summary>
    Warning,

    /// <summary>A four-alarm, page-the-founder condition (logged at Error).</summary>
    Critical,
}

/// <summary>The four launch-health alert kinds (WS6). Each maps to one seam in <see cref="AlertEvaluator"/>.</summary>
public enum AlertKind
{
    /// <summary>(a) The most recent received webhook event is older than the staleness window.</summary>
    WebhookStaleness,

    /// <summary>(b) Reconciliation found paid Checkout sessions with no license (paid-but-no-key).</summary>
    ReconciliationDiff,

    /// <summary>(c) Activation success rate dropped below the floor over a meaningful sample.</summary>
    LowActivationSuccessRate,

    /// <summary>(d) Email bounce/spam spike — founder-gated: only fires when an email-status source is wired.</summary>
    EmailBounceSpike,
}

/// <summary>A single fired alert: what kind, how bad, and a human-readable message.</summary>
public sealed record Alert(AlertKind Kind, AlertSeverity Severity, string Message);

/// <summary>
/// The delivery seam for founder alerts (WS6). REAL email + phone/SMS paging is
/// founder-gated (needs an email/SMS provider + on-call routing that is not built);
/// this abstraction is the wire that a production sink plugs into. The default
/// <see cref="LoggingAlertSink"/> logs, so alerts are at least visible in logs/console
/// until a paging sink is provisioned.
/// </summary>
public interface IAlertSink
{
    /// <summary>Delivers a fired alert. MUST NOT throw — alerting is best-effort and non-fatal.</summary>
    void Send(Alert alert);
}

/// <summary>
/// Default alert sink: writes the alert to the logger at Error (Critical) or Warning.
/// This is the honest floor — real paging (email/phone) is founder-gated and plugs in
/// by replacing this registration.
/// </summary>
public sealed class LoggingAlertSink : IAlertSink
{
    private readonly ILogger<LoggingAlertSink> _logger;

    public LoggingAlertSink(ILogger<LoggingAlertSink> logger) => _logger = logger;

    public void Send(Alert alert)
    {
        // Never throw out of an alert sink — the caller runs in a background loop.
        try
        {
            if (alert.Severity == AlertSeverity.Critical)
            {
                _logger.LogError("ALERT [{Kind}] {Message}", alert.Kind, alert.Message);
            }
            else
            {
                _logger.LogWarning("ALERT [{Kind}] {Message}", alert.Kind, alert.Message);
            }
        }
#pragma warning disable CA1031 // Alerting must never destabilise the background loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // Last-resort: swallow. If even logging fails there is nowhere left to go.
            System.Diagnostics.Debug.WriteLine($"LoggingAlertSink failed: {ex}");
        }
    }
}
