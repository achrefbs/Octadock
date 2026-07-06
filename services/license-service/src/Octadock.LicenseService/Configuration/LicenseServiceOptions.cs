namespace Octadock.LicenseService.Configuration;

/// <summary>
/// Configuration for the license service, bound from the <c>LicenseService</c>
/// configuration section / environment. Secrets (the Stripe webhook signing
/// secrets) come from environment or a secret store, never source.
/// </summary>
public sealed class LicenseServiceOptions
{
    public const string SectionName = "LicenseService";

    /// <summary>
    /// Stripe webhook signing secret(s). More than one supports zero-downtime
    /// secret rotation: keep the old and new secret here during the overlap.
    /// </summary>
    public List<string> StripeWebhookSecrets { get; set; } = new();

    /// <summary>
    /// Restricted Stripe secret API key (rk_...) used by the LIVE reconciliation
    /// source to list PAID Checkout sessions. EMPTY by default and left empty in
    /// appsettings.json — the real (founder-gated) key is injected from environment
    /// or a secret store. When empty, reconciliation falls back to the null source.
    /// A read-only "Checkout Sessions: read" restricted key is sufficient.
    /// </summary>
    public string StripeApiKey { get; set; } = string.Empty;

    /// <summary>Base address for Stripe's REST API (overridable for tests).</summary>
    public string StripeApiBaseUrl { get; set; } = "https://api.stripe.com";

    /// <summary>Product id stamped on issued licenses.</summary>
    public string ProductId { get; set; } = "octadock-local-beta";

    /// <summary>The only Stripe Price ID allowed to issue a launch license.</summary>
    public string ExpectedStripePriceId { get; set; } = "price_1TqH3lKNDvjYLyJhld4yYPIZ";

    /// <summary>The only Stripe price lookup key allowed to issue a launch license.</summary>
    public string ExpectedStripePriceLookupKey { get; set; } = "octadock_local_beta_usd_49";

    /// <summary>Expected paid Checkout amount in cents.</summary>
    public long ExpectedAmountCents { get; set; } = 4900;

    /// <summary>Expected paid Checkout currency.</summary>
    public string ExpectedCurrency { get; set; } = "usd";

    /// <summary>Devices a single license may activate (the "3 devices" policy).</summary>
    public int DeviceLimit { get; set; } = 3;

    /// <summary>Months of updates included from purchase.</summary>
    public int UpdatesMonths { get; set; } = 12;

    /// <summary>SQLite connection string for the license database.</summary>
    public string ConnectionString { get; set; } = "Data Source=octadock-license.db";

    /// <summary>
    /// Optional shared token gating <c>GET /admin/health</c> at the app layer. This is
    /// a thin secondary check, NOT the primary control: production must sit behind a
    /// network gate (Cloudflare Access + WebAuthn, founder-gated). When set, the admin
    /// page requires this token via <c>?token=</c> or the <c>X-Admin-Token</c> header.
    /// When empty, the page still serves but shows an "UNAUTHENTICATED" banner.
    /// </summary>
    public string AdminToken { get; set; } = string.Empty;
}
