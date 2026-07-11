using FluentAssertions;
using Octadock.Core.Commands;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AutomationLaunchSafetyTests
{
    [Theory]
    [InlineData(CommandType.ReadAloud)]
    [InlineData(CommandType.Dictation)]
    [InlineData(CommandType.Quit)]
    public void BlocksProtocolCommand_blocks_local_only_commands(CommandType type)
    {
        OctadockCommand command = OctadockCommand.Create(type);

        AutomationLaunchSafety
            .BlocksProtocolCommand(["octadock://dictation"], command)
            .Should().BeTrue();
    }

    [Fact]
    public void BlocksProtocolCommand_allows_local_cli_commands()
    {
        OctadockCommand command = OctadockCommand.Create(CommandType.Dictation);

        AutomationLaunchSafety
            .BlocksProtocolCommand(["dictation"], command)
            .Should().BeFalse();
    }

    [Fact]
    public void BlocksProtocolCommand_allows_non_local_only_protocol_commands()
    {
        OctadockCommand command = OctadockCommand.Create(CommandType.CaptureArea);

        AutomationLaunchSafety
            .BlocksProtocolCommand(["octadock://capture-area"], command)
            .Should().BeFalse();
    }

    [Fact]
    public void BlocksProtocolCommand_allows_review_only_ai_actions_without_auto_send()
    {
        OctadockCommand command = OctadockCommand.Create(CommandType.AiActions);

        AutomationLaunchSafety
            .BlocksProtocolCommand(["octadock://ai?action=summarize"], command)
            .Should().BeFalse("protocol activation only opens the explicit review window");
    }

    [Theory]
    [InlineData("octadock://activate?key=OCTA-ABCDE-FGHJK-MNPQR-STUVW")]
    [InlineData("octadock://activate")]
    [InlineData("activate")]
    [InlineData("--activate")]
    public void IsActivationLaunch_recognizes_activation_launches(string firstArg)
    {
        AutomationLaunchSafety.IsActivationLaunch([firstArg]).Should().BeTrue();
    }

    [Theory]
    [InlineData("octadock://capture-area")]
    [InlineData("capture-area")]
    [InlineData("open-settings")]
    public void IsActivationLaunch_ignores_other_launches(string firstArg)
    {
        AutomationLaunchSafety.IsActivationLaunch([firstArg]).Should().BeFalse();
    }
}
