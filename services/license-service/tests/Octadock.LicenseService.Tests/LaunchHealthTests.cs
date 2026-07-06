using FluentAssertions;
using Octadock.LicenseService.Data;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// Expanded launch-health surface (WS6): the 24h issuance count, webhook age,
/// issued-not-activated, and a LIVE activation success rate driven by audit rows —
/// including the failure rows now recorded on the Activate failure branches.
/// </summary>
public class LaunchHealthTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static LicenseIssuance Issuance(string sessionId, string paymentIntent = "pi_h")
        => new(
            Product: "octadock-local-beta",
            PurchaseEmail: "buyer@example.com",
            StripeCustomerId: "cus_1",
            StripeCheckoutSessionId: sessionId,
            StripePaymentIntentId: paymentIntent,
            AmountCents: 4900,
            Currency: "usd",
            DeviceLimit: 3,
            UpdatesUntil: Now.AddMonths(12));

    [Fact]
    public void Issued_last_24h_counts_only_recent_licenses()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.IssueLicense(Issuance("cs_recent"), Now.AddHours(-2));
        db.Repository.IssueLicense(Issuance("cs_old", "pi_old"), Now.AddHours(-30));

        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now);

        health.LicensesTotal.Should().Be(2);
        health.LicensesIssuedLast24h.Should().Be(1, "only the license created within 24h counts");
    }

    [Fact]
    public void Most_recent_webhook_received_at_reflects_the_latest_event()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.BeginWebhookEvent("evt_a", "checkout.session.completed", "h1", Now.AddMinutes(-90));
        db.Repository.BeginWebhookEvent("evt_b", "checkout.session.completed", "h2", Now.AddMinutes(-10));

        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now);

        health.WebhookEventsTotal.Should().Be(2);
        health.MostRecentWebhookReceivedAt.Should().Be(Now.AddMinutes(-10));
    }

    [Fact]
    public void No_webhooks_yields_null_most_recent()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.GetLaunchHealth(Now).MostRecentWebhookReceivedAt.Should().BeNull();
    }

    [Fact]
    public void Activation_failures_are_audited_and_drive_a_live_success_rate()
    {
        using var db = new TempLicenseDatabase();
        LicenseIssuanceResult issued = db.Repository.IssueLicense(Issuance("cs_rate"), Now);
        string key = issued.LicenseKey;

        // 3 successes (device limit is 3), then 1 failure (limit reached).
        db.Repository.Activate(key, "d1", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        db.Repository.Activate(key, "d2", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        db.Repository.Activate(key, "d3", 1, Now).Outcome.Should().Be(ActivationOutcome.Activated);
        db.Repository.Activate(key, "d4", 1, Now).Outcome.Should().Be(ActivationOutcome.DeviceLimitReached);
        // 1 more failure: unknown key.
        db.Repository.Activate("OCTA-NOPE", "d5", 1, Now).Outcome.Should().Be(ActivationOutcome.LicenseNotFound);

        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now);

        health.ActivationsSucceeded.Should().Be(3);
        health.ActivationsFailed.Should().Be(2);
        health.ActivationAttempts.Should().Be(5);
        health.ActivationSuccessRate.Should().BeApproximately(3.0 / 5.0, 1e-9);
    }

    [Fact]
    public void Revoked_license_activation_failure_is_audited()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.IssueLicense(Issuance("cs_rev", "pi_rev"), Now);
        db.Repository.RevokeByPaymentIntent("pi_rev", "refunded", "charge.refunded", "webhook", Now);

        db.Repository.Activate(FindKey(db, "cs_rev"), "d1", 1, Now)
            .Outcome.Should().Be(ActivationOutcome.LicenseNotActive);

        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now);
        health.ActivationsSucceeded.Should().Be(0);
        health.ActivationsFailed.Should().Be(1);
    }

    [Fact]
    public void No_attempts_yields_null_success_rate()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.GetLaunchHealth(Now).ActivationSuccessRate.Should().BeNull();
    }

    [Fact]
    public void Issued_not_activated_counts_active_licenses_with_no_live_activation()
    {
        using var db = new TempLicenseDatabase();
        LicenseIssuanceResult activated = db.Repository.IssueLicense(Issuance("cs_act", "pi_a"), Now);
        db.Repository.IssueLicense(Issuance("cs_idle", "pi_b"), Now); // never activated
        db.Repository.Activate(activated.LicenseKey, "d1", 1, Now);

        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now);

        health.LicensesActive.Should().Be(2);
        health.IssuedNotActivated.Should().Be(1, "one active license has no device activation");
        health.IssuedNotActivatedRate.Should().BeApproximately(0.5, 1e-9);
    }

    [Fact]
    public void Reconciliation_and_email_fields_are_folded_in_and_gated()
    {
        using var db = new TempLicenseDatabase();
        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth(Now)
            .WithReconciliation(diff: 3, sourceConfigured: true);

        health.ReconciliationDiff.Should().Be(3);
        health.PaidSessionSourceConfigured.Should().BeTrue();

        // Email/resend stay null — founder-gated, never fabricated.
        health.EmailsDelivered.Should().BeNull();
        health.EmailsBounced.Should().BeNull();
        health.ResendCount.Should().BeNull();
    }

    private static string FindKey(TempLicenseDatabase db, string sessionId)
    {
        // Re-issue is idempotent on the session id, so it returns the existing key.
        return db.Repository.IssueLicense(
            new LicenseIssuance("octadock-local-beta", null, null, sessionId, null, null, null, 3, null),
            Now).LicenseKey;
    }
}
