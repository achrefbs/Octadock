using System.Globalization;
using Octadock.LicenseService.Data;

namespace Octadock.LicenseService.Alerting;

/// <summary>
/// The inputs an <see cref="AlertEvaluator"/> needs to decide which founder alerts
/// fire. Deliberately a plain snapshot so the evaluator is a pure function of state
/// and is trivially unit-testable without a database or a running service.
/// </summary>
/// <param name="Now">Current time (for webhook-age math).</param>
/// <param name="MostRecentWebhookReceivedAt">Newest received webhook event, or null if none exists.</param>
/// <param name="ReconciliationDiff">Paid-but-no-key session count from the last pass.</param>
/// <param name="ActivationSuccessRate">Activation success rate over the sample, or null if no attempts.</param>
/// <param name="ActivationAttempts">Number of activation attempts in the sample.</param>
/// <param name="EmailBounceRate">
/// Email bounce/spam rate, or null when the email-status source is not wired
/// (founder-gated). When null, alert (d) is skipped by design rather than firing.
/// </param>
public sealed record AlertState(
    DateTimeOffset Now,
    DateTimeOffset? MostRecentWebhookReceivedAt,
    int ReconciliationDiff,
    double? ActivationSuccessRate,
    long ActivationAttempts,
    double? EmailBounceRate = null);

/// <summary>
/// Decides which of the four launch-health alerts fire from a <see cref="AlertState"/>
/// and dispatches them to an <see cref="IAlertSink"/> (WS6). Pure decision logic lives
/// in <see cref="Evaluate"/> so it is directly unit-testable; <see cref="EvaluateAndDispatch"/>
/// is the non-throwing entry point the reconciliation loop calls each cycle.
/// </summary>
public sealed class AlertEvaluator
{
    /// <summary>(a) Webhook staleness threshold — no received event newer than this pages the founder.</summary>
    public static readonly TimeSpan WebhookStalenessThreshold = TimeSpan.FromMinutes(60);

    /// <summary>(c) Activation success-rate floor.</summary>
    public const double ActivationSuccessFloor = 0.90;

    /// <summary>(c) Minimum attempts before the success-rate alert is meaningful.</summary>
    public const int ActivationMinAttempts = 20;

    /// <summary>(d) Email bounce/spam-rate ceiling (only used when an email source is wired).</summary>
    public const double EmailBounceCeiling = 0.05;

    private readonly IAlertSink _sink;

    public AlertEvaluator(IAlertSink sink) => _sink = sink;

    /// <summary>Returns the alerts that fire for <paramref name="state"/>, without side effects.</summary>
    public IReadOnlyList<Alert> Evaluate(AlertState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var alerts = new List<Alert>();

        // (a) Webhook staleness — only meaningful when at least one event exists.
        if (state.MostRecentWebhookReceivedAt is { } lastWebhook)
        {
            TimeSpan age = state.Now - lastWebhook;
            if (age > WebhookStalenessThreshold)
            {
                alerts.Add(new Alert(
                    AlertKind.WebhookStaleness,
                    AlertSeverity.Warning,
                    $"No Stripe webhook received for {age.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} min " +
                    $"(threshold {WebhookStalenessThreshold.TotalMinutes.ToString("F0", CultureInfo.InvariantCulture)} min). " +
                    "Money-path delivery may be down."));
            }
        }

        // (b) Reconciliation diff > 0 — paid-but-no-key is the four-alarm signal.
        if (state.ReconciliationDiff > 0)
        {
            alerts.Add(new Alert(
                AlertKind.ReconciliationDiff,
                AlertSeverity.Critical,
                $"Reconciliation diff: {state.ReconciliationDiff} paid Checkout session(s) have NO license " +
                "(paid-but-no-key). Investigate immediately."));
        }

        // (c) Activation success rate < floor over a meaningful sample.
        if (state.ActivationAttempts >= ActivationMinAttempts &&
            state.ActivationSuccessRate is { } rate &&
            rate < ActivationSuccessFloor)
        {
            alerts.Add(new Alert(
                AlertKind.LowActivationSuccessRate,
                AlertSeverity.Warning,
                $"Activation success rate {(rate * 100).ToString("F1", CultureInfo.InvariantCulture)}% " +
                $"is below {(ActivationSuccessFloor * 100).ToString("F0", CultureInfo.InvariantCulture)}% " +
                $"over {state.ActivationAttempts} attempts."));
        }

        // (d) Email bounce/spam spike — FOUNDER-GATED. Email delivery is not built, so
        // there is no bounce source; EmailBounceRate is null and this branch is skipped
        // by design. It only fires once an email-status source is wired in.
        if (state.EmailBounceRate is { } bounceRate && bounceRate > EmailBounceCeiling)
        {
            alerts.Add(new Alert(
                AlertKind.EmailBounceSpike,
                AlertSeverity.Warning,
                $"Email bounce/spam rate {(bounceRate * 100).ToString("F1", CultureInfo.InvariantCulture)}% " +
                $"exceeds {(EmailBounceCeiling * 100).ToString("F0", CultureInfo.InvariantCulture)}%."));
        }

        return alerts;
    }

    /// <summary>
    /// Evaluates <paramref name="state"/> and sends every fired alert to the sink.
    /// Non-throwing: any failure is swallowed so the caller's background loop is never
    /// destabilised. Returns the alerts that fired (for tests / observability).
    /// </summary>
    public IReadOnlyList<Alert> EvaluateAndDispatch(AlertState state)
    {
        IReadOnlyList<Alert> alerts;
        try
        {
            alerts = Evaluate(state);
        }
#pragma warning disable CA1031 // Alert evaluation must never take down the background loop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            System.Diagnostics.Debug.WriteLine($"AlertEvaluator.Evaluate failed: {ex}");
            return Array.Empty<Alert>();
        }

        foreach (Alert alert in alerts)
        {
            _sink.Send(alert);
        }

        return alerts;
    }

    /// <summary>Builds an <see cref="AlertState"/> from a launch-health snapshot and the current time.</summary>
    public static AlertState StateFrom(LaunchHealthSnapshot health, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(health);
        return new AlertState(
            Now: now,
            MostRecentWebhookReceivedAt: health.MostRecentWebhookReceivedAt,
            ReconciliationDiff: health.ReconciliationDiff,
            ActivationSuccessRate: health.ActivationSuccessRate,
            ActivationAttempts: health.ActivationAttempts,
            // Founder-gated: no email-status source, so no bounce rate to evaluate.
            EmailBounceRate: null);
    }
}
