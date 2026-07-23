using FluentAssertions;
using Octadock.Core.Commands;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

/// <summary>
/// The removed-command contract: previously documented verbs whose features were
/// removed in this version keep a parse-level tombstone (they never become
/// "unknown command"), while help/usage listings stop advertising them.
/// </summary>
public class CommandTombstoneTests
{
    private readonly CommandParser _parser = new();

    [Theory]
    [InlineData("pin", CommandType.Pin)]
    [InlineData("open-annotate", CommandType.OpenAnnotate)]
    [InlineData("open-from-clipboard", CommandType.OpenFromClipboard)]
    [InlineData("add-shelf-item", CommandType.AddShelfItem)]
    public void Removed_verbs_still_parse_as_tombstones_on_the_cli(string verb, CommandType expected)
    {
        CommandParseResult result = _parser.ParseArguments([verb]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(expected);
        CommandTokens.IsRemoved(result.Command.Type).Should().BeTrue();
    }

    [Theory]
    [InlineData("octadock://pin", CommandType.Pin)]
    [InlineData("octadock://open-annotate", CommandType.OpenAnnotate)]
    [InlineData("octadock://open-from-clipboard", CommandType.OpenFromClipboard)]
    [InlineData("octadock://add-shelf-item", CommandType.AddShelfItem)]
    [InlineData("octadock://open?filepath=C%3A%5Cdata.csv", CommandType.Open)]
    public void Removed_verbs_still_parse_as_tombstones_on_the_protocol(string uri, CommandType expected)
    {
        CommandParseResult result = _parser.ParseUri(uri);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(expected);
        CommandTokens.IsRemoved(result.Command.Type).Should().BeTrue();
    }

    [Fact]
    public void Open_still_requires_its_filepath_parameter_before_the_tombstone_fires()
    {
        CommandParseResult result = _parser.ParseArguments(["open"]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("filepath");
    }

    [Fact]
    public void Active_tokens_exclude_removed_verbs_but_wire_tokens_stay_stable()
    {
        CommandTokens.ActiveTokens.Should().NotContain(["pin", "open-annotate", "open-from-clipboard", "add-shelf-item", "open"]);
        CommandTokens.AllTokens.Should().Contain(
            ["pin", "open-annotate", "open-from-clipboard", "add-shelf-item", "open"],
            "the parser must keep recognizing the historical wire tokens");
        CommandTokens.ToToken(CommandType.Pin).Should().Be("pin");
        CommandTokens.ToToken(CommandType.Open).Should().Be("open");
    }

    [Fact]
    public void Usage_error_listings_do_not_advertise_removed_verbs()
    {
        CommandParseResult result = _parser.ParseArguments(["definitely-not-a-command"]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("capture-area");
        result.Error.Should().NotContain("pin");
        result.Error.Should().NotContain("open-annotate");
        result.Error.Should().NotContain("add-shelf-item");
    }

    [Theory]
    [InlineData(CommandType.Pin)]
    [InlineData(CommandType.OpenAnnotate)]
    [InlineData(CommandType.OpenFromClipboard)]
    [InlineData(CommandType.AddShelfItem)]
    [InlineData(CommandType.Open)]
    public void Removed_message_is_truthful_and_names_the_token(CommandType type)
    {
        string message = CommandTokens.RemovedMessage(type);

        message.Should().Contain(CommandTokens.ToToken(type));
        message.Should().Contain("removed in this version");
    }

    [Theory]
    [InlineData(CommandType.CaptureArea)]
    [InlineData(CommandType.OpenHistory)]
    [InlineData(CommandType.Quit)]
    public void Remaining_commands_are_not_marked_removed(CommandType type)
    {
        CommandTokens.IsRemoved(type).Should().BeFalse();
    }
}
