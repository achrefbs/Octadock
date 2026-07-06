using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Octadock.LicenseService.Data;

namespace Octadock.LicenseService.Reconciliation;

/// <summary>
/// Hourly reconciliation (WS3, R3): compares the paid Checkout sessions Stripe
/// knows about against the licenses actually issued, and flags any paid session
/// with no license (paid-but-no-key). A nonzero diff is the four-alarm signal the
/// founder must be paged on; here it is logged at error and exposed for the admin
/// tile. Runs on schedule even when the Stripe source is unconfigured so the gap
/// is visible rather than silently green.
/// </summary>
public sealed class ReconciliationService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan LookBack = TimeSpan.FromHours(48);

    private readonly IPaidSessionSource _paidSessions;
    private readonly LicenseRepository _repository;
    private readonly TimeProvider _time;
    private readonly ILogger<ReconciliationService> _logger;

    /// <summary>Session ids from the most recent run that had no license (admin tile).</summary>
    public IReadOnlyList<string> LastUnreconciledSessions { get; private set; } = Array.Empty<string>();

    public ReconciliationService(
        IPaidSessionSource paidSessions,
        LicenseRepository repository,
        TimeProvider time,
        ILogger<ReconciliationService> logger)
    {
        _paidSessions = paidSessions;
        _repository = repository;
        _time = time;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reconciliation cycle failed; will retry next interval.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs a single reconciliation pass; returns the paid-but-no-key session ids.</summary>
    public async Task<IReadOnlyList<string>> RunOnceAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset since = _time.GetUtcNow() - LookBack;
        IReadOnlyList<string> paidSessions =
            await _paidSessions.GetPaidSessionIdsAsync(since, cancellationToken).ConfigureAwait(false);

        IReadOnlyList<string> missing = _repository.FindPaidSessionsWithoutLicense(paidSessions);
        LastUnreconciledSessions = missing;

        if (missing.Count > 0)
        {
            _logger.LogError(
                "RECONCILIATION DIFF: {Count} paid Checkout session(s) have NO license: {Sessions}. " +
                "Investigate immediately (paid-but-no-key).",
                missing.Count,
                string.Join(", ", missing));
        }
        else if (_paidSessions.IsConfigured)
        {
            _logger.LogInformation("Reconciliation clean: every paid session has a license.");
        }

        return missing;
    }
}
