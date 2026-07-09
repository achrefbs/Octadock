using FluentAssertions;
using Octadock.Core.Commands;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class CommandFormatterTests
{
    private readonly CommandFormatter _formatter = new();
    private readonly CommandParser _parser = new();

    [Fact]
    public void ToUri_verb_without_parameters()
    {
        string uri = _formatter.ToUri(OctadockCommand.Create(CommandType.OpenHistory));
        uri.Should().Be("octadock://open-history");
    }

    [Fact]
    public void ToUri_emits_keys_in_stable_ordinal_order()
    {
        var cmd = OctadockCommand.Create(CommandType.CaptureArea, new Dictionary<string, string>
        {
            ["width"] = "800",
            ["action"] = "annotate",
            ["x"] = "100",
        });

        string uri = _formatter.ToUri(cmd);

        // action < width < x ordinally.
        uri.Should().Be("octadock://capture-area?action=annotate&width=800&x=100");
    }

    [Fact]
    public void ToUri_percent_encodes_values()
    {
        var cmd = OctadockCommand.Create(CommandType.Pin, new Dictionary<string, string>
        {
            ["filepath"] = @"C:\Users\me\ref image.png",
        });

        string uri = _formatter.ToUri(cmd);

        uri.Should().StartWith("octadock://pin?filepath=");
        uri.Should().Contain("%5C"); // backslash encoded
        uri.Should().Contain("%20"); // space encoded
    }

    [Fact]
    public void ToUri_throws_for_unknown_command()
    {
        Action act = () => _formatter.ToUri(OctadockCommand.Create(CommandType.Unknown));
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(CommandType.CaptureArea)]
    [InlineData(CommandType.CaptureFullscreen)]
    [InlineData(CommandType.OpenSettings)]
    [InlineData(CommandType.RecordScreen)]
    [InlineData(CommandType.Pin)]
    [InlineData(CommandType.OpenHistory)]
    [InlineData(CommandType.OpenContext)]
    public void Formatter_and_parser_round_trip(CommandType type)
    {
        var original = OctadockCommand.Create(type, new Dictionary<string, string>
        {
            ["action"] = "copy",
            ["monitor"] = "2",
            ["filepath"] = @"C:\path with spaces\image.png",
            ["language"] = "en-US",
            ["silent"] = "true",
        });

        string uri = _formatter.ToUri(original);
        CommandParseResult parsed = _parser.ParseUri(uri);

        parsed.Success.Should().BeTrue(parsed.Error);
        parsed.Command!.Type.Should().Be(type);
        parsed.Command!.Parameters.Should().BeEquivalentTo(original.Parameters);
    }

    [Fact]
    public void RoundTrip_preserves_region_parameters()
    {
        var original = OctadockCommand.Create(CommandType.CaptureArea, new Dictionary<string, string>
        {
            ["x"] = "10",
            ["y"] = "20",
            ["width"] = "300",
            ["height"] = "400",
        });

        string uri = _formatter.ToUri(original);
        CommandParseResult parsed = _parser.ParseUri(uri);

        parsed.Command!.Region.Should().Be(original.Region);
    }
}
