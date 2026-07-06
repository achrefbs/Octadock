namespace Octadock.Core.Licensing;

/// <summary>
/// Formats the resolved <see cref="LicenseState"/> into short, honest strings for the
/// ambient status surfaces (tray tooltip, dock badge) and the Account &amp; Billing chip
/// (WS5, R31). Chip labels are TEXT, never colour alone (R40).
/// </summary>
public static class LicenseStatusFormatter
{
    /// <summary>Whole days remaining in the trial (ceiling; never negative).</summary>
    public static int DaysLeft(DateTimeOffset? endsUtc, DateTimeOffset nowUtc)
    {
        if (endsUtc is not { } ends)
        {
            return 0;
        }

        double days = (ends - nowUtc).TotalDays;
        return days <= 0 ? 0 : (int)Math.Ceiling(days);
    }

    /// <summary>The short text label for the state chip: Trial / Licensed / Trial ended / Paused / Revoked.</summary>
    public static string ChipLabel(LicenseMode mode) => mode switch
    {
        LicenseMode.Licensed => "Licensed",
        LicenseMode.Trial => "Trial",
        LicenseMode.TrialExpired => "Trial ended",
        LicenseMode.TrialFrozen => "Paused",
        LicenseMode.Revoked => "Revoked",
        _ => "Trial",
    };

    /// <summary>A one-line status, e.g. "Trial — 9 days left", "Licensed", "Trial ended".</summary>
    public static string ShortStatus(LicenseState state, DateTimeOffset nowUtc) => state.Mode switch
    {
        LicenseMode.Licensed => state.UpdatesExpired ? "Licensed (updates ended)" : "Licensed",
        LicenseMode.Trial => TrialLine(state.TrialEndsUtc, nowUtc),
        LicenseMode.TrialExpired => "Trial ended — enter a license key",
        LicenseMode.TrialFrozen => "Trial paused — check your PC clock",
        LicenseMode.Revoked => "License revoked",
        _ => "Trial",
    };

    /// <summary>The tray-icon tooltip (clamped to the legacy NotifyIcon 63-char limit).</summary>
    public static string TrayTooltip(LicenseState state, DateTimeOffset nowUtc)
    {
        string text = $"Octadock — {ShortStatus(state, nowUtc)}";
        return text.Length <= 63 ? text : text[..63];
    }

    private static string TrialLine(DateTimeOffset? endsUtc, DateTimeOffset nowUtc)
    {
        int days = DaysLeft(endsUtc, nowUtc);
        return days == 1 ? "Trial — 1 day left" : $"Trial — {days} days left";
    }
}
