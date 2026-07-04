using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Settings;

namespace Octadock.Core.Services;

/// <summary>
/// The pure, side-effect-free history retention rules. Given a set of capture
/// records, the current settings and the current time, it decides which live
/// captures have aged past the retention window (and should be removed) and which
/// already-soft-deleted captures have sat in the trash long enough to be purged.
/// It performs no I/O so it is fully unit-testable.
/// </summary>
public static class RetentionPolicy
{
    /// <summary>
    /// The grace period a soft-deleted capture stays recoverable before it is
    /// permanently purged, while history is enabled.
    /// </summary>
    public static readonly TimeSpan SoftDeleteGrace = TimeSpan.FromDays(1);

    /// <summary>
    /// Evaluates the retention plan for the supplied captures.
    /// </summary>
    /// <param name="captures">Candidate captures (live and/or soft-deleted).</param>
    /// <param name="settings">Current settings (history + retention window).</param>
    /// <param name="now">The reference time (usually <see cref="Common.IClock.UtcNow"/>).</param>
    /// <returns>The set of captures to hard-delete for expiry and to purge from trash.</returns>
    public static RetentionPlan Evaluate(
        IReadOnlyList<CaptureRecord> captures,
        OctadockSettings settings,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(captures);
        ArgumentNullException.ThrowIfNull(settings);

        if (captures.Count == 0)
        {
            return RetentionPlan.Empty;
        }

        bool historyEnabled = settings.History.Enabled;
        int? retentionDays = settings.History.Retention.RetentionDays();

        var expired = new List<Guid>();
        var purge = new List<Guid>();

        // When history is disabled entirely, everything is fair game: live
        // captures are expired immediately and soft-deleted ones purged with no
        // grace period.
        bool retentionActive = historyEnabled && retentionDays is int;
        DateTimeOffset expiryCutoff = retentionDays is int days
            ? now - TimeSpan.FromDays(days)
            : DateTimeOffset.MinValue;

        TimeSpan grace = historyEnabled ? SoftDeleteGrace : TimeSpan.Zero;
        DateTimeOffset purgeCutoff = now - grace;

        foreach (CaptureRecord capture in captures)
        {
            if (capture.DeletedAt is DateTimeOffset deletedAt)
            {
                // Soft-deleted: purge once it has been in the trash beyond the grace.
                if (deletedAt <= purgeCutoff)
                {
                    purge.Add(capture.Id);
                }

                continue;
            }

            // Live capture: expire when history is off, or when it has aged past
            // the retention window. Forever/never-expire retention leaves it alone.
            if (!historyEnabled)
            {
                expired.Add(capture.Id);
            }
            else if (retentionActive && capture.CreatedAt < expiryCutoff)
            {
                expired.Add(capture.Id);
            }
        }

        return expired.Count == 0 && purge.Count == 0
            ? RetentionPlan.Empty
            : new RetentionPlan(expired, purge);
    }

    /// <summary>
    /// Computes the live-capture expiry cutoff for the current settings, or
    /// <c>null</c> when there is no age cutoff. Disabled history means live
    /// captures expire immediately, so callers must handle that separately.
    /// </summary>
    public static DateTimeOffset? ExpiryCutoff(OctadockSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.History.Enabled)
        {
            return null;
        }

        return settings.History.Retention.RetentionDays() is int days
            ? now - TimeSpan.FromDays(days)
            : null;
    }

    /// <summary>
    /// Computes the soft-delete purge cutoff: soft-deleted captures whose
    /// <see cref="CaptureRecord.DeletedAt"/> is at or before this instant are
    /// purged.
    /// </summary>
    public static DateTimeOffset PurgeCutoff(OctadockSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        TimeSpan grace = settings.History.Enabled ? SoftDeleteGrace : TimeSpan.Zero;
        return now - grace;
    }
}
