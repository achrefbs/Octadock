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
}
