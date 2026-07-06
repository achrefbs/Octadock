using FluentAssertions;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>The ambient status strings (tray tooltip, dock badge, chip) — WS5, R31/R40.</summary>
public class LicenseStatusFormatterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    private static LicenseState Trial(DateTimeOffset endsUtc)
        => new(LicenseMode.Trial, endsUtc, null, false, false, "trial");

    [Fact]
    public void Trial_status_counts_whole_days_left()
    {
        LicenseStatusFormatter.ShortStatus(Trial(Now.AddDays(9)), Now).Should().Be("Trial — 9 days left");
        LicenseStatusFormatter.ShortStatus(Trial(Now.AddHours(20)), Now).Should().Be("Trial — 1 day left");
    }

    [Fact]
    public void Non_trial_modes_have_stable_labels()
    {
        LicenseStatusFormatter.ShortStatus(new LicenseState(LicenseMode.Licensed, null, null, false, false, ""), Now)
            .Should().Be("Licensed");
        LicenseStatusFormatter.ShortStatus(new LicenseState(LicenseMode.Licensed, null, null, false, true, ""), Now)
            .Should().Be("Licensed (updates ended)");
        LicenseStatusFormatter.ShortStatus(new LicenseState(LicenseMode.TrialExpired, null, null, false, false, ""), Now)
            .Should().StartWith("Trial ended");
        LicenseStatusFormatter.ShortStatus(new LicenseState(LicenseMode.Revoked, null, null, false, false, ""), Now)
            .Should().Be("License revoked");
    }

    [Theory]
    [InlineData(LicenseMode.Trial, "Trial")]
    [InlineData(LicenseMode.Licensed, "Licensed")]
    [InlineData(LicenseMode.TrialExpired, "Trial ended")]
    [InlineData(LicenseMode.TrialFrozen, "Paused")]
    [InlineData(LicenseMode.Revoked, "Revoked")]
    public void Chip_labels_are_text_not_colour(LicenseMode mode, string expected)
    {
        LicenseStatusFormatter.ChipLabel(mode).Should().Be(expected);
    }

    [Fact]
    public void Tray_tooltip_is_prefixed_and_within_the_legacy_length_limit()
    {
        string tip = LicenseStatusFormatter.TrayTooltip(Trial(Now.AddDays(9)), Now);
        tip.Should().Be("Octadock — Trial — 9 days left");
        tip.Length.Should().BeLessThanOrEqualTo(63);
    }
}
