using FluentAssertions;
using Octadock.Core.Updates;
using Xunit;

namespace Octadock.Core.Tests.Updates;

/// <summary>Version comparison for the update check (WS1) — must never offer a downgrade.</summary>
public class ReleaseVersionTests
{
    [Theory]
    [InlineData("0.2.0", 0, 2, 0, null)]
    [InlineData("1.4.7-beta.2", 1, 4, 7, "beta.2")]
    [InlineData("0.2.0-alpha.0", 0, 2, 0, "alpha.0")]
    [InlineData("2.0", 2, 0, 0, null)]
    [InlineData("0.2.0+build.5", 0, 2, 0, null)]
    public void Parses_core_and_prerelease(string text, int major, int minor, int patch, string? pre)
    {
        ReleaseVersion.TryParse(text, out ReleaseVersion v).Should().BeTrue();
        v.Should().Be(new ReleaseVersion(major, minor, patch, pre));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1.x.0")]
    public void Rejects_garbage(string? text) => ReleaseVersion.TryParse(text, out _).Should().BeFalse();

    [Fact]
    public void Release_outranks_prerelease_of_the_same_core()
    {
        Parse("0.2.0").CompareTo(Parse("0.2.0-alpha.0")).Should().BePositive();
        Parse("0.2.0-alpha.0").CompareTo(Parse("0.2.0")).Should().BeNegative();
    }

    [Fact]
    public void Orders_by_major_minor_patch()
    {
        Parse("0.3.0").CompareTo(Parse("0.2.9")).Should().BePositive();
        Parse("1.0.0").CompareTo(Parse("0.9.9")).Should().BePositive();
        Parse("0.2.0").CompareTo(Parse("0.2.0")).Should().Be(0);
    }

    [Fact]
    public void Comparison_operators_follow_version_precedence()
    {
        ReleaseVersion earlier = Parse("0.2.9");
        ReleaseVersion later = Parse("0.3.0");
        ReleaseVersion sameAsLater = Parse("0.3.0");
        ReleaseVersion prerelease = Parse("0.3.0-alpha.0");

        (earlier < later).Should().BeTrue();
        (earlier <= later).Should().BeTrue();
        (later > earlier).Should().BeTrue();
        (later >= earlier).Should().BeTrue();
        (prerelease < later).Should().BeTrue();
        (later <= sameAsLater).Should().BeTrue();
        (later >= sameAsLater).Should().BeTrue();
    }

    private static ReleaseVersion Parse(string text)
    {
        ReleaseVersion.TryParse(text, out ReleaseVersion v).Should().BeTrue();
        return v;
    }
}
