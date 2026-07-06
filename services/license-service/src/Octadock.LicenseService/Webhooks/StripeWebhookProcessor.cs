using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Data;
using Octadock.LicenseService.Stripe;

namespace Octadock.LicenseService.Webhooks;

/// <summary>What the webhook did with an event.</summary>
public enum WebhookOutcome
{
    Rejected,        // signature invalid — 4xx, NO side effect
    BadRequest,      // malformed body — 4xx, no side effect
    Duplicate,       // already-seen event id — 200, no repeat side effect
    LicenseIssued,   // one license created
    LicenseRevoked,  // refund / dispute revocation applied
    Ignored,         // verified but not an event we act on — 200
}

/// <summary>Outcome of processing a webhook, including the HTTP status to return.</summary>
public sealed record WebhookProcessingResult(
    WebhookOutcome Outcome, int StatusCode, string Message, string? LicenseKey = null);

/// <summary>
/// The heart of the money path (WS3). For every inbound webhook it:
///   1. verifies the Stripe-Signature BEFORE any parsing or side effect (R12);
///   2. dedupes by event id so replays/duplicates act at most once (R3);
///   3. issues exactly one license for a paid Checkout session (R2), or revokes
///      on refund/dispute (R21).
/// A bad signature or malformed body returns 4xx and changes nothing.
/// </summary>
public sealed class StripeWebhookProcessor
{
    // HTTP status codes kept as plain ints so this pure-logic processor carries no
    // ASP.NET dependency and stays testable in a plain library test project.
    private const int Http200Ok = 200;
    private const int Http400BadRequest = 400;

    private readonly StripeSignatureVerifier _verifier;
    private readonly LicenseRepository _repository;
    private readonly LicenseServiceOptions _options;
    private readonly TimeProvider _time;

    public StripeWebhookProcessor(
        StripeSignatureVerifier verifier,
        LicenseRepository repository,
        IOptions<LicenseServiceOptions> options,
        TimeProvider time)
    {
        _verifier = verifier;
        _repository = repository;
        _options = options.Value;
        _time = time;
    }

    public WebhookProcessingResult Process(string body, string? signatureHeader)
    {
        // 1. Signature FIRST — nothing else runs on an unverified request.
        SignatureVerificationResult verification =
            _verifier.Verify(body, signatureHeader, _options.StripeWebhookSecrets);
        if (!verification.IsValid)
        {
            return new WebhookProcessingResult(
                WebhookOutcome.Rejected, Http400BadRequest,
                $"Signature verification failed: {verification.Failure}.");
        }

        StripeEvent? stripeEvent = StripeEvent.TryParse(body);
        if (stripeEvent is null)
        {
            return new WebhookProcessingResult(
                WebhookOutcome.BadRequest, Http400BadRequest, "Malformed event body.");
        }

        DateTimeOffset now = _time.GetUtcNow();

        // 2. Idempotency. Only a fully processed event is skipped; an event that
        // was received but not completed remains retryable for Stripe delivery.
        WebhookEventBeginStatus beginStatus =
            _repository.BeginWebhookEvent(stripeEvent.Id, stripeEvent.Type, Sha256Hex(body), now);
        if (beginStatus == WebhookEventBeginStatus.AlreadyProcessed)
        {
            return new WebhookProcessingResult(
                WebhookOutcome.Duplicate, Http200Ok,
                $"Duplicate event {stripeEvent.Id} ignored.");
        }

        if (beginStatus == WebhookEventBeginStatus.PayloadMismatch)
        {
            return new WebhookProcessingResult(
                WebhookOutcome.BadRequest, Http400BadRequest,
                $"Event {stripeEvent.Id} was already received with a different payload.");
        }

        // 3. Dispatch.
        WebhookProcessingResult result = stripeEvent.Type switch
        {
            "checkout.session.completed" => HandleCheckoutCompleted(stripeEvent, now),
            "charge.refunded" => HandleRevocation(stripeEvent, "refunded", "charge.refunded", now),
            "charge.dispute.created" => HandleRevocation(stripeEvent, "revoked", "charge.dispute.created", now),
            _ => new WebhookProcessingResult(
                WebhookOutcome.Ignored, Http200Ok,
                $"Event {stripeEvent.Type} acknowledged; no action."),
        };

        _repository.MarkWebhookProcessed(stripeEvent.Id, now);
        return result;
    }

    private WebhookProcessingResult HandleCheckoutCompleted(StripeEvent stripeEvent, DateTimeOffset now)
    {
        string? paymentStatus = stripeEvent.GetString("payment_status");
        if (!string.Equals(paymentStatus, "paid", StringComparison.OrdinalIgnoreCase))
        {
            return new WebhookProcessingResult(
                WebhookOutcome.Ignored, Http200Ok,
                "Checkout session not paid; no license issued.");
        }

        if (!MatchesExpectedLaunchPrice(stripeEvent))
        {
            return new WebhookProcessingResult(
                WebhookOutcome.Ignored, Http200Ok,
                "Checkout session does not match the configured Octadock launch price; no license issued.");
        }

        string? sessionId = stripeEvent.GetString("id");
        if (string.IsNullOrEmpty(sessionId))
        {
            return new WebhookProcessingResult(
                WebhookOutcome.BadRequest, Http400BadRequest,
                "Checkout session has no id; cannot issue a license.");
        }

        var issuance = new LicenseIssuance(
            Product: _options.ProductId,
            PurchaseEmail: stripeEvent.GetString("customer_email")
                ?? stripeEvent.GetNestedString("customer_details", "email"),
            StripeCustomerId: stripeEvent.GetString("customer"),
            StripeCheckoutSessionId: sessionId,
            StripePaymentIntentId: stripeEvent.GetString("payment_intent"),
            AmountCents: stripeEvent.GetInt64("amount_total"),
            Currency: stripeEvent.GetString("currency"),
            DeviceLimit: _options.DeviceLimit,
            UpdatesUntil: now.AddMonths(_options.UpdatesMonths));

        LicenseIssuanceResult issued = _repository.IssueLicense(issuance, now);
        return new WebhookProcessingResult(
            issued.WasNewlyIssued ? WebhookOutcome.LicenseIssued : WebhookOutcome.Duplicate,
            Http200Ok,
            issued.WasNewlyIssued
                ? "License issued."
                : "License already issued for this session.",
            issued.LicenseKey);
    }

    private bool MatchesExpectedLaunchPrice(StripeEvent stripeEvent)
    {
        long? amount = stripeEvent.GetInt64("amount_total");
        if (amount != _options.ExpectedAmountCents)
        {
            return false;
        }

        string? currency = stripeEvent.GetString("currency");
        if (!string.Equals(currency, _options.ExpectedCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string? metadataPriceId = stripeEvent.GetMetadataString("stripe_price_id")
            ?? stripeEvent.GetMetadataString("price_id");
        if (string.Equals(metadataPriceId, _options.ExpectedStripePriceId, StringComparison.Ordinal))
        {
            return true;
        }

        string? metadataLookupKey = stripeEvent.GetMetadataString("price_key")
            ?? stripeEvent.GetMetadataString("lookup_key");
        if (string.Equals(metadataLookupKey, _options.ExpectedStripePriceLookupKey, StringComparison.Ordinal))
        {
            return true;
        }

        return stripeEvent.HasLineItemPrice(
            _options.ExpectedStripePriceId,
            _options.ExpectedStripePriceLookupKey);
    }

    private WebhookProcessingResult HandleRevocation(
        StripeEvent stripeEvent, string status, string reason, DateTimeOffset now)
    {
        string? paymentIntent = stripeEvent.GetString("payment_intent");
        if (string.IsNullOrEmpty(paymentIntent))
        {
            return new WebhookProcessingResult(
                WebhookOutcome.Ignored, Http200Ok,
                $"{reason} without payment_intent; nothing to revoke.");
        }

        int affected = _repository.RevokeByPaymentIntent(paymentIntent, status, reason, "webhook", now);
        return new WebhookProcessingResult(
            WebhookOutcome.LicenseRevoked, Http200Ok,
            $"{reason}: {affected} license(s) set to {status}.");
    }

    private static string Sha256Hex(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body)))
            .ToLower(CultureInfo.InvariantCulture);
}
