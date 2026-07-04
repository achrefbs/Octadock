using FluentAssertions;
using Octadock.Core.Primitives;
using Xunit;

namespace Octadock.Core.Tests.Primitives;

public class RgbaColorTests
{
    [Fact]
    public void Parse_six_digit_hex_is_opaque()
    {
        RgbaColor c = RgbaColor.Parse("#0078D4");
        c.R.Should().Be(0x00);
        c.G.Should().Be(0x78);
        c.B.Should().Be(0xD4);
        c.A.Should().Be(255);
    }

    [Fact]
    public void Parse_eight_digit_hex_reads_alpha()
    {
        RgbaColor c = RgbaColor.Parse("#0078D480");
        c.A.Should().Be(0x80);
    }

    [Fact]
    public void Parse_three_digit_shorthand_doubles_nibbles()
    {
        RgbaColor c = RgbaColor.Parse("#0AF");
        c.R.Should().Be(0x00);
        c.G.Should().Be(0xAA);
        c.B.Should().Be(0xFF);
        c.A.Should().Be(255);
    }

    [Fact]
    public void Parse_four_digit_shorthand_doubles_nibbles_with_alpha()
    {
        RgbaColor c = RgbaColor.Parse("#0AF8");
        c.R.Should().Be(0x00);
        c.G.Should().Be(0xAA);
        c.B.Should().Be(0xFF);
        c.A.Should().Be(0x88);
    }

    [Fact]
    public void Parse_without_leading_hash_is_supported()
    {
        RgbaColor.Parse("FF0000").Should().Be(new RgbaColor(255, 0, 0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("#GG0000")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("nothex")]
    [InlineData(null)]
    public void TryParse_returns_false_for_invalid(string? input)
    {
        RgbaColor.TryParse(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_format_exception_on_bad_input()
    {
        Action act = () => RgbaColor.Parse("#zzz");
        act.Should().Throw<FormatException>();
    }

    [Fact]
    public void ToHex_omits_alpha_when_opaque()
    {
        new RgbaColor(0x12, 0x34, 0x56).ToHex().Should().Be("#123456");
    }

    [Fact]
    public void ToHex_includes_alpha_when_translucent()
    {
        new RgbaColor(0x12, 0x34, 0x56, 0x78).ToHex().Should().Be("#12345678");
    }

    [Theory]
    [InlineData("#123456")]
    [InlineData("#12345678")]
    [InlineData("#000000")]
    [InlineData("#FFFFFFFF")]
    public void ToHex_round_trips_through_parse(string hex)
    {
        RgbaColor c = RgbaColor.Parse(hex);
        RgbaColor.Parse(c.ToHex()).Should().Be(c);
    }

    [Fact]
    public void WithOpacity_sets_alpha_and_clamps()
    {
        RgbaColor c = RgbaColor.Black.WithOpacity(0.5);
        c.A.Should().Be(128);

        RgbaColor.Black.WithOpacity(2.0).A.Should().Be(255);
        RgbaColor.Black.WithOpacity(-1.0).A.Should().Be(0);
    }

    [Fact]
    public void Opacity_is_alpha_over_255()
    {
        new RgbaColor(0, 0, 0, 255).Opacity.Should().BeApproximately(1.0, 1e-9);
        new RgbaColor(0, 0, 0, 0).Opacity.Should().Be(0.0);
    }
}

public class PointDTests
{
    [Fact]
    public void DistanceTo_computes_euclidean_distance()
    {
        new PointD(0, 0).DistanceTo(new PointD(3, 4)).Should().BeApproximately(5.0, 1e-9);
    }

    [Fact]
    public void Equality_is_value_based()
    {
        (new PointD(1.5, 2.5) == new PointD(1.5, 2.5)).Should().BeTrue();
    }
}
