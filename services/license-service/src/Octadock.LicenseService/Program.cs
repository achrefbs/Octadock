using System.Text;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Data;
using Octadock.LicenseService.Licensing;
using Octadock.LicenseService.Reconciliation;
using Octadock.LicenseService.Stripe;
using Octadock.LicenseService.Webhooks;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<LicenseServiceOptions>(
    builder.Configuration.GetSection(LicenseServiceOptions.SectionName));
builder.Services.Configure<EntitlementSigningOptions>(
    builder.Configuration.GetSection(EntitlementSigningOptions.SectionName));
builder.Services.AddSingleton<IEntitlementIssuer, EntitlementIssuer>();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(sp =>
    new LicenseDatabase(sp.GetRequiredService<IOptions<LicenseServiceOptions>>().Value.ConnectionString));
builder.Services.AddSingleton<LicenseRepository>();
builder.Services.AddSingleton(sp => new StripeSignatureVerifier(sp.GetRequiredService<TimeProvider>()));
builder.Services.AddSingleton<StripeWebhookProcessor>();
builder.Services.AddSingleton<IPaidSessionSource, NullPaidSessionSource>();
builder.Services.AddSingleton<ReconciliationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReconciliationService>());

WebApplication app = builder.Build();

// Apply migrations at startup — fail fast rather than serve against a bad schema.
app.Services.GetRequiredService<LicenseDatabase>().Migrate();

app.MapGet("/health", (LicenseRepository repository, ReconciliationService reconciliation) =>
{
    LaunchHealthSnapshot health = repository.GetLaunchHealth();
    return Results.Ok(new
    {
        status = "ok",
        licenses = new
        {
            total = health.LicensesTotal,
            active = health.LicensesActive,
            revoked = health.LicensesRevoked,
        },
        webhookEvents = health.WebhookEventsTotal,
        reconciliation = new { unreconciled = reconciliation.LastUnreconciledSessions.Count },
    });
});

// The webhook MUST read the exact raw body — the signature is over those bytes.
app.MapPost("/webhooks/stripe", async (HttpRequest request, StripeWebhookProcessor processor) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();
    string? signature = request.Headers["Stripe-Signature"];

    WebhookProcessingResult result = processor.Process(body, signature);
    return Results.Json(
        new { outcome = result.Outcome.ToString(), message = result.Message, licenseKey = result.LicenseKey },
        statusCode: result.StatusCode);
});

// Activation (WS4/WS5): a valid key + this device's machine hash yields a signed,
// device-bound entitlement, enforcing the device limit. The client verifies the
// entitlement offline against its trust ring.
app.MapPost("/activate", (
    ActivateRequest request, LicenseRepository repository, IEntitlementIssuer issuer, TimeProvider time) =>
{
    if (string.IsNullOrWhiteSpace(request.LicenseKey) || string.IsNullOrWhiteSpace(request.MachineHash))
    {
        return Results.BadRequest(new { error = "licenseKey and machineHash are required." });
    }

    string key = request.LicenseKey.Trim();
    string machine = request.MachineHash.Trim();
    int deviceHashVersion = request.DeviceHashV ?? 1;
    DateTimeOffset now = time.GetUtcNow();

    ActivationResult result = repository.Activate(key, machine, deviceHashVersion, now);
    return result.Outcome switch
    {
        ActivationOutcome.Activated or ActivationOutcome.AlreadyActive => Results.Ok(new
        {
            outcome = result.Outcome.ToString(),
            activeDevices = result.ActiveDeviceCount,
            deviceLimit = result.License!.DeviceLimit,
            entitlement = issuer.Issue(result.License!, machine, deviceHashVersion, now).ToJson(),
        }),
        ActivationOutcome.DeviceLimitReached => Results.Json(
            new { outcome = "DeviceLimitReached", activeDevices = result.ActiveDeviceCount, deviceLimit = result.License!.DeviceLimit },
            statusCode: 409),
        ActivationOutcome.LicenseNotActive => Results.Json(new { outcome = "LicenseNotActive" }, statusCode: 403),
        _ => Results.NotFound(new { outcome = "LicenseNotFound" }),
    };
});

// The public trust anchor clients embed in their verification trust ring.
app.MapGet("/trust-anchor", (IEntitlementIssuer issuer) =>
{
    (string keyId, string publicKey) = issuer.TrustAnchor;
    return Results.Ok(new { keyId, publicKeyBase64Url = publicKey });
});

app.Run();

/// <summary>Activation request body.</summary>
internal sealed record ActivateRequest(string LicenseKey, string MachineHash, int? DeviceHashV);

/// <summary>Exposed so integration tests can host the app via WebApplicationFactory.</summary>
public partial class Program;
