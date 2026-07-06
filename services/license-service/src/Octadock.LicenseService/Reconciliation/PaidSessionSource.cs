using Microsoft.Extensions.Logging;

namespace Octadock.LicenseService.Reconciliation;

/// <summary>
/// Supplies the set of PAID Stripe Checkout session ids for a window, so
/// reconciliation can find any that never produced a license. The live
/// implementation calls the Stripe API and is founder-gated (needs live keys);
/// see <see cref="NullPaidSessionSource"/> for the unconfigured default.
/// </summary>
public interface IPaidSessionSource
{
    /// <summary>True when a real Stripe source is configured.</summary>
    bool IsConfigured { get; }

    /// <summary>Paid Checkout session ids created at/after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<string>> GetPaidSessionIdsAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

/// <summary>
/// Default no-op source used until live Stripe API credentials are provisioned.
/// Reconciliation still runs on schedule but reports "not configured" instead of
/// silently claiming a clean diff.
/// </summary>
public sealed class NullPaidSessionSource : IPaidSessionSource
{
    private readonly ILogger<NullPaidSessionSource> _logger;

    public NullPaidSessionSource(ILogger<NullPaidSessionSource> logger) => _logger = logger;

    public bool IsConfigured => false;

    public Task<IReadOnlyList<string>> GetPaidSessionIdsAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "Reconciliation paid-session source is not configured (no live Stripe key); " +
            "reconciliation cannot compare Stripe against the license DB until it is set.");
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
