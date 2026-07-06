using FluentAssertions;
using Octadock.LicenseService.Data;
using Xunit;

namespace Octadock.LicenseService.Tests;

public class LicenseRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static LicenseIssuance Issuance(string sessionId, string paymentIntent = "pi_1")
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
    public void Webhook_event_id_is_recorded_once_then_deduped()
    {
        using var db = new TempLicenseDatabase();

        db.Repository.TryBeginWebhookEvent("evt_1", "checkout.session.completed", "hash", Now)
            .Should().BeTrue("first sighting");
        db.Repository.TryBeginWebhookEvent("evt_1", "checkout.session.completed", "hash", Now)
            .Should().BeFalse("a replayed event id must be deduped");
    }

    [Fact]
    public void Unprocessed_webhook_event_id_is_retryable_until_marked_processed()
    {
        using var db = new TempLicenseDatabase();

        db.Repository.BeginWebhookEvent("evt_retry", "checkout.session.completed", "hash", Now)
            .Should().Be(WebhookEventBeginStatus.Started);
        db.Repository.BeginWebhookEvent("evt_retry", "checkout.session.completed", "hash", Now.AddSeconds(1))
            .Should().Be(WebhookEventBeginStatus.RetryUnprocessed);

        db.Repository.MarkWebhookProcessed("evt_retry", Now.AddSeconds(2));

        db.Repository.BeginWebhookEvent("evt_retry", "checkout.session.completed", "hash", Now.AddSeconds(3))
            .Should().Be(WebhookEventBeginStatus.AlreadyProcessed);
    }

    [Fact]
    public void Replayed_webhook_event_id_with_different_payload_is_rejected()
    {
        using var db = new TempLicenseDatabase();

        db.Repository.BeginWebhookEvent("evt_mismatch", "checkout.session.completed", "hash-a", Now)
            .Should().Be(WebhookEventBeginStatus.Started);

        db.Repository.BeginWebhookEvent("evt_mismatch", "checkout.session.completed", "hash-b", Now.AddSeconds(1))
            .Should().Be(WebhookEventBeginStatus.PayloadMismatch);
    }

    [Fact]
    public void Issuance_is_idempotent_on_the_checkout_session()
    {
        using var db = new TempLicenseDatabase();

        LicenseIssuanceResult first = db.Repository.IssueLicense(Issuance("cs_1"), Now);
        LicenseIssuanceResult second = db.Repository.IssueLicense(Issuance("cs_1"), Now);

        first.WasNewlyIssued.Should().BeTrue();
        second.WasNewlyIssued.Should().BeFalse("the same session must not mint a second license");
        second.LicenseKey.Should().Be(first.LicenseKey);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1);
    }

    [Fact]
    public void Refund_revokes_the_license_by_payment_intent()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.IssueLicense(Issuance("cs_2", "pi_2"), Now);

        int affected = db.Repository.RevokeByPaymentIntent("pi_2", "refunded", "charge.refunded", "webhook", Now);

        affected.Should().Be(1);
        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth();
        health.LicensesActive.Should().Be(0);
        health.LicensesRevoked.Should().Be(1);
    }

    [Fact]
    public void Reconciliation_finds_paid_sessions_without_a_license()
    {
        using var db = new TempLicenseDatabase();
        db.Repository.IssueLicense(Issuance("cs_has_key"), Now);

        IReadOnlyList<string> missing =
            db.Repository.FindPaidSessionsWithoutLicense(new[] { "cs_has_key", "cs_orphan" });

        missing.Should().ContainSingle().Which.Should().Be("cs_orphan");
    }
}
