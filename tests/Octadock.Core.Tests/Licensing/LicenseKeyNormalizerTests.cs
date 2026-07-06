using FluentAssertions;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>The key-entry box must forgive how a buyer copied their key (WS5).</summary>
public class LicenseKeyNormalizerTests
{
    private const string Canonical = "OCTA-ABCDE-FGHJK-MNPQR-STUVW";

    [Theory]
    [InlineData("OCTA-ABCDE-FGHJK-MNPQR-STUVW")]                 // already canonical
    [InlineData("  OCTA-ABCDE-FGHJK-MNPQR-STUVW  ")]             // surrounding whitespace
    [InlineData("octa-abcde-fghjk-mnpqr-stuvw")]                 // lower case
    [InlineData("OCTA ABCDE FGHJK MNPQR STUVW")]                 // spaces instead of dashes
    [InlineData("OCTAABCDEFGHJKMNPQRSTUVW")]                     // no separators at all
    [InlineData("OCTA-ABCDE-FGHJK-MNPQR-STUVW\r\n")]             // trailing newline
    public void Well_formed_keys_normalize_to_the_canonical_form(string input)
    {
        LicenseKeyNormalizer.Normalize(input).Should().Be(Canonical);
        LicenseKeyNormalizer.IsWellFormed(input).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-key")]
    [InlineData("OCTA-ABCDE-FGHJK-MNPQR")] // too short (3 groups)
    public void Malformed_input_is_reported_as_not_well_formed(string? input)
    {
        LicenseKeyNormalizer.IsWellFormed(input).Should().BeFalse();
    }
}
