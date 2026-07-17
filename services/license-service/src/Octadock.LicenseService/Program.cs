using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Octadock.LicenseService.Admin;
using Octadock.LicenseService.Alerting;
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

// Founder alert seam (WS6). The default sink LOGS; real email/phone paging is
// founder-gated and plugs in by replacing this registration.
builder.Services.AddSingleton<IAlertSink, LoggingAlertSink>();
builder.Services.AddSingleton<AlertEvaluator>();

// Reconciliation paid-session source: use the LIVE Stripe source only when a
// restricted API key is configured (founder-gated), else the honest null source.
// IsConfigured reflects reality so the admin tile never claims a clean diff on a
// source that never ran.
builder.Services.AddSingleton<IPaidSessionSource>(sp =>
{
    LicenseServiceOptions options = sp.GetRequiredService<IOptions<LicenseServiceOptions>>().Value;
    if (string.IsNullOrWhiteSpace(options.StripeApiKey))
    {
        return new NullPaidSessionSource(sp.GetRequiredService<ILogger<NullPaidSessionSource>>());
    }

    return new StripePaidSessionSource(
        options,
        new HttpClientHandler(),
        sp.GetRequiredService<ILogger<StripePaidSessionSource>>());
});
builder.Services.AddSingleton<ReconciliationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ReconciliationService>());

WebApplication app = builder.Build();

// Apply migrations at startup — fail fast rather than serve against a bad schema.
app.Services.GetRequiredService<LicenseDatabase>().Migrate();

// Builds the full launch-health snapshot, folding in the out-of-DB reconciliation
// numbers from the background service. Email/resend numbers stay null (founder-gated).
static LaunchHealthSnapshot BuildHealth(
    LicenseRepository repository, ReconciliationService reconciliation,
    IPaidSessionSource paidSessions, TimeProvider time)
    => repository
        .GetLaunchHealth(time.GetUtcNow())
        .WithReconciliation(reconciliation.LastUnreconciledSessions.Count, paidSessions.IsConfigured);

// Public liveness probe: deliberately excludes database and commercial metrics.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

// Founder launch-health page (WS6). Self-contained inline HTML — no external assets.
// PRODUCTION MUST sit behind a network gate (Cloudflare Access + WebAuthn, founder-
// gated). The shared admin token is a required secondary app-layer check. It is
// accepted only via X-Admin-Token and fails closed when not configured.
app.MapGet("/admin/health", (
    HttpRequest request, LicenseRepository repository, ReconciliationService reconciliation,
    IPaidSessionSource paidSessions, TimeProvider time, IOptions<LicenseServiceOptions> options) =>
{
    // Metrics are sensitive even after authentication; forbid browser/proxy storage
    // for success and every failure response from this endpoint.
    request.HttpContext.Response.Headers["Cache-Control"] = "no-store";
    string configuredToken = options.Value.AdminToken;
    string? presentedToken = request.Headers["X-Admin-Token"].FirstOrDefault();
    AdminHealthAuthorizationResult authorization =
        AdminHealthAuthorization.Evaluate(configuredToken, presentedToken);
    if (authorization == AdminHealthAuthorizationResult.Unavailable)
    {
        return Results.Text("Admin health is unavailable.", "text/plain", Encoding.UTF8, statusCode: 503);
    }

    if (authorization == AdminHealthAuthorizationResult.Forbidden)
    {
        return Results.Text("Forbidden.", "text/plain", Encoding.UTF8, statusCode: 403);
    }

    LaunchHealthSnapshot health = BuildHealth(repository, reconciliation, paidSessions, time);
    string html = AdminHealthPage.Render(health, time.GetUtcNow());
    return Results.Text(html, "text/html", Encoding.UTF8);
});

// The webhook MUST read the exact raw body — the signature is over those bytes.
app.MapPost("/webhooks/stripe", async (HttpRequest request, StripeWebhookProcessor processor) =>
{
    using var reader = new StreamReader(request.Body, Encoding.UTF8);
    string body = await reader.ReadToEndAsync();
    string? signature = request.Headers["Stripe-Signature"];

    WebhookProcessingResult result = processor.Process(body, signature);
    return Results.Json(
        StripeWebhookResponseFactory.Create(result),
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
