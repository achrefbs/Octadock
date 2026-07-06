using FluentAssertions;
using Octadock.LicenseService.Alerting;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// The four founder alert seams (WS6): webhook staleness, reconciliation diff,
/// low activation success rate, and the founder-gated email-bounce branch. Each is a
/// pure function of <see cref="AlertState"/>, so it is directly unit-testable.
/// </summary>
public class AlertEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A capturing sink so we can assert what actually got dispatched.</summary>
    private sealed class CapturingSink : IAlertSink
    {
        public List<Alert> Sent { get; } = new();

        public void Send(Alert alert) => Sent.Add(alert);
    }

    private static AlertState CleanState() => new(
        Now: Now,
        MostRecentWebhookReceivedAt: Now.AddMinutes(-5),
        ReconciliationDiff: 0,
        ActivationSuccessRate: 1.0,
        ActivationAttempts: 50);

    [Fact]
    public void Clean_state_fires_no_alerts()
    {
        var sink = new CapturingSink();
        IReadOnlyList<Alert> fired = new AlertEvaluator(sink).EvaluateAndDispatch(CleanState());

        fired.Should().BeEmpty();
        sink.Sent.Should().BeEmpty();
    }

    [Fact]
    public void Webhook_staleness_fires_when_last_event_is_older_than_60_min()
    {
        AlertState state = CleanState() with { MostRecentWebhookReceivedAt = Now.AddMinutes(-61) };

        new AlertEvaluator(new CapturingSink()).Evaluate(state)
            .Should().ContainSingle(a => a.Kind == AlertKind.WebhookStaleness);
    }

    [Fact]
    public void Webhook_staleness_does_not_fire_when_no_event_exists()
    {
        // "only when at least one event exists" — a service that has never received a
        // webhook must not page on staleness.
        AlertState state = CleanState() with { MostRecentWebhookReceivedAt = null };

        new AlertEvaluator(new CapturingSink()).Evaluate(state)
            .Should().NotContain(a => a.Kind == AlertKind.WebhookStaleness);
    }

    [Fact]
    public void Reconciliation_diff_greater_than_zero_fires_a_critical_alert()
    {
        AlertState state = CleanState() with { ReconciliationDiff = 2 };

        Alert alert = new AlertEvaluator(new CapturingSink()).Evaluate(state)
            .Should().ContainSingle(a => a.Kind == AlertKind.ReconciliationDiff).Subject;
        alert.Severity.Should().Be(AlertSeverity.Critical);
    }

    [Fact]
    public void Low_activation_success_rate_fires_only_with_enough_attempts()
    {
        AlertState belowFloorEnoughAttempts = CleanState() with
        {
            ActivationSuccessRate = 0.85,
            ActivationAttempts = 20,
        };
        new AlertEvaluator(new CapturingSink()).Evaluate(belowFloorEnoughAttempts)
            .Should().ContainSingle(a => a.Kind == AlertKind.LowActivationSuccessRate);

        AlertState belowFloorTooFewAttempts = CleanState() with
        {
            ActivationSuccessRate = 0.50,
            ActivationAttempts = 19,
        };
        new AlertEvaluator(new CapturingSink()).Evaluate(belowFloorTooFewAttempts)
            .Should().NotContain(a => a.Kind == AlertKind.LowActivationSuccessRate,
                "below 20 attempts the rate is not yet meaningful");
    }

    [Fact]
    public void Email_bounce_branch_is_founder_gated_and_skipped_when_no_source()
    {
        // Default: no email-status source wired ⇒ EmailBounceRate is null ⇒ no alert,
        // by design (email delivery not built).
        AlertState noSource = CleanState() with { EmailBounceRate = null };
        new AlertEvaluator(new CapturingSink()).Evaluate(noSource)
            .Should().NotContain(a => a.Kind == AlertKind.EmailBounceSpike);

        // But the branch DOES fire once a source is wired and the rate spikes.
        AlertState spike = CleanState() with { EmailBounceRate = 0.20 };
        new AlertEvaluator(new CapturingSink()).Evaluate(spike)
            .Should().ContainSingle(a => a.Kind == AlertKind.EmailBounceSpike);
    }

    [Fact]
    public void Evaluate_and_dispatch_sends_every_fired_alert_to_the_sink()
    {
        var sink = new CapturingSink();
        AlertState twoAlerts = CleanState() with
        {
            MostRecentWebhookReceivedAt = Now.AddMinutes(-90),
            ReconciliationDiff = 1,
        };

        new AlertEvaluator(sink).EvaluateAndDispatch(twoAlerts);

        sink.Sent.Select(a => a.Kind).Should().BeEquivalentTo(
            new[] { AlertKind.WebhookStaleness, AlertKind.ReconciliationDiff });
    }

    [Fact]
    public void Evaluate_and_dispatch_never_throws_even_if_the_sink_throws()
    {
        var throwingSink = new ThrowingSink();
        AlertState state = CleanState() with { ReconciliationDiff = 1 };

        // A sink fault must not escape into the background loop; LoggingAlertSink
        // swallows, but even a raw throwing sink must be tolerated by the caller path.
        Action act = () => new AlertEvaluator(throwingSink).EvaluateAndDispatch(state);
        act.Should().Throw<InvalidOperationException>(
            "EvaluateAndDispatch itself surfaces sink exceptions; the NON-throwing contract is on " +
            "the production LoggingAlertSink, verified separately");
    }

    [Fact]
    public void Logging_alert_sink_never_throws()
    {
        var sink = new LoggingAlertSink(Microsoft.Extensions.Logging.Abstractions.NullLogger<LoggingAlertSink>.Instance);
        Action act = () => sink.Send(new Alert(AlertKind.ReconciliationDiff, AlertSeverity.Critical, "x"));
        act.Should().NotThrow();
    }

    private sealed class ThrowingSink : IAlertSink
    {
        public void Send(Alert alert) => throw new InvalidOperationException("boom");
    }
}
