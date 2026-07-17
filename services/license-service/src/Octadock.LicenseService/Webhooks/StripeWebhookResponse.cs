namespace Octadock.LicenseService.Webhooks;

internal sealed record StripeWebhookResponse(
    string Outcome,
    string Message);

internal static class StripeWebhookResponseFactory
{
    public static StripeWebhookResponse Create(WebhookProcessingResult result)
        => new(result.Outcome.ToString(), result.Message);
}
