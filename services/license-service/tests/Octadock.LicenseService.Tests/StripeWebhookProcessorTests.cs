using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Data;
using Octadock.LicenseService.Stripe;
using Octadock.LicenseService.Webhooks;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// End-to-end money-path acceptance (WS3, item 8): a verified paid Checkout event
/// yields EXACTLY one license; a bad signature or replay changes nothing; refunds
/// revoke.
/// </summary>
public class StripeWebhookProcessorTests
{
    private const string Secret = "whsec_test";
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions WebJson =
        new(JsonSerializerDefaults.Web);

    private static StripeWebhookProcessor Build(TempLicenseDatabase db)
    {
        var time = new FixedTimeProvider(Now);
        var verifier = new StripeSignatureVerifier(time);
        IOptions<LicenseServiceOptions> options = Options.Create(new LicenseServiceOptions
        {
            StripeWebhookSecrets = new List<string> { Secret },
            ProductId = "octadock-local-beta",
            ExpectedStripePriceId = Events.ExpectedPriceId,
            ExpectedStripePriceLookupKey = Events.ExpectedLookupKey,
            ExpectedAmountCents = 4900,
            ExpectedCurrency = "usd",
            DeviceLimit = 3,
            UpdatesMonths = 12,
        });
        return new StripeWebhookProcessor(verifier, db.Repository, options, time);
    }

    private static string Sign(string body) => StripeSignatureFactory.Make(body, Secret, Now.ToUnixTimeSeconds());

    private static string ReadOnlyIssuedLicenseKey(TempLicenseDatabase db)
    {
        using var connection = new SqliteConnection(db.ConnectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT license_key FROM licenses;";
        return (string?)command.ExecuteScalar()
            ?? throw new InvalidOperationException("Expected one issued license.");
    }

    [Fact]
    public void Unsigned_request_is_rejected_with_400_and_no_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_1", "cs_1", "pi_1");

        WebhookProcessingResult result = processor.Process(body, signatureHeader: null);

        result.StatusCode.Should().Be(400);
        result.Outcome.Should().Be(WebhookOutcome.Rejected);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(0, "an unverified event must not issue a license");
    }

    [Fact]
    public void Forged_signature_is_rejected_with_400_and_no_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_2", "cs_2", "pi_2");
        string forged = StripeSignatureFactory.Make(body, "whsec_attacker", Now.ToUnixTimeSeconds());

        WebhookProcessingResult result = processor.Process(body, forged);

        result.StatusCode.Should().Be(400);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(0);
    }

    [Fact]
    public void Signed_checkout_completed_issues_exactly_one_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_3", "cs_3", "pi_3");

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.Outcome.Should().Be(WebhookOutcome.LicenseIssued);
        result.StatusCode.Should().Be(200);
        ReadOnlyIssuedLicenseKey(db).Should().StartWith("OCTA-");
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1);
    }

    [Fact]
    public void Webhook_http_response_does_not_serialize_issued_license_material()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_response", "cs_response", "pi_response");

        WebhookProcessingResult result = processor.Process(body, Sign(body));
        StripeWebhookResponse response = StripeWebhookResponseFactory.Create(result);
        string issuedKey = ReadOnlyIssuedLicenseKey(db);
        string json = JsonSerializer.Serialize(
            response, WebJson);

        issuedKey.Should().StartWith("OCTA-");
        typeof(WebhookProcessingResult).GetProperty("LicenseKey").Should().BeNull();
        result.ToString().Should().NotContain(issuedKey);
        json.Should().NotContain(issuedKey);
        json.Should().NotContain("\"licenseKey\"");
    }

    [Fact]
    public void Signed_checkout_completed_with_expanded_line_item_price_can_issue_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted(
            "evt_3b",
            "cs_3b",
            "pi_3b",
            includeMetadata: false,
            includeExpandedLineItems: true);

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.Outcome.Should().Be(WebhookOutcome.LicenseIssued);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1);
    }

    [Fact]
    public void Duplicate_event_id_does_not_issue_a_second_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_4", "cs_4", "pi_4");
        string sig = Sign(body);

        WebhookProcessingResult first = processor.Process(body, sig);
        WebhookProcessingResult second = processor.Process(body, sig);

        first.Outcome.Should().Be(WebhookOutcome.LicenseIssued);
        second.Outcome.Should().Be(WebhookOutcome.Duplicate);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1, "replay must be idempotent");
    }

    [Fact]
    public void Retry_of_unprocessed_event_id_can_still_issue_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_retry", "cs_retry", "pi_retry");
        db.Repository.BeginWebhookEvent(
                "evt_retry",
                "checkout.session.completed",
                Events.PayloadHash(body),
                Now)
            .Should().Be(WebhookEventBeginStatus.Started);

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.Outcome.Should().Be(WebhookOutcome.LicenseIssued);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1);
    }

    [Fact]
    public void Duplicate_event_id_with_different_payload_is_rejected()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string original = Events.CheckoutCompleted("evt_mismatch", "cs_original", "pi_original");
        string changed = Events.CheckoutCompleted("evt_mismatch", "cs_changed", "pi_changed");
        processor.Process(original, Sign(original));

        WebhookProcessingResult result = processor.Process(changed, Sign(changed));

        result.Outcome.Should().Be(WebhookOutcome.BadRequest);
        result.StatusCode.Should().Be(400);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(1);
    }

    [Fact]
    public void Refund_event_revokes_the_issued_license()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string issue = Events.CheckoutCompleted("evt_5", "cs_5", "pi_5");
        processor.Process(issue, Sign(issue));

        string refund = Events.ChargeRefunded("evt_6", "pi_5");
        WebhookProcessingResult result = processor.Process(refund, Sign(refund));

        result.Outcome.Should().Be(WebhookOutcome.LicenseRevoked);
        LaunchHealthSnapshot health = db.Repository.GetLaunchHealth();
        health.LicensesActive.Should().Be(0);
        health.LicensesRevoked.Should().Be(1);
    }

    [Fact]
    public void Unpaid_checkout_session_issues_nothing()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted("evt_7", "cs_7", "pi_7", paymentStatus: "unpaid");

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.Outcome.Should().Be(WebhookOutcome.Ignored);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(0);
    }

    [Fact]
    public void Paid_checkout_for_wrong_price_issues_nothing()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted(
            "evt_wrong_price",
            "cs_wrong_price",
            "pi_wrong_price",
            priceId: "price_other",
            priceLookupKey: "other_product");

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.StatusCode.Should().Be(200);
        result.Outcome.Should().Be(WebhookOutcome.Ignored);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(0);
    }

    [Fact]
    public void Paid_checkout_for_wrong_amount_issues_nothing()
    {
        using var db = new TempLicenseDatabase();
        StripeWebhookProcessor processor = Build(db);
        string body = Events.CheckoutCompleted(
            "evt_wrong_amount",
            "cs_wrong_amount",
            "pi_wrong_amount",
            amountTotal: 9900);

        WebhookProcessingResult result = processor.Process(body, Sign(body));

        result.StatusCode.Should().Be(200);
        result.Outcome.Should().Be(WebhookOutcome.Ignored);
        db.Repository.GetLaunchHealth().LicensesTotal.Should().Be(0);
    }
}
