using FluentAssertions;
using Octadock.Core.Commands;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AutomationLaunchSafetyTests
{
    [Theory]
    [InlineData(CommandType.Run)]
    [InlineData(CommandType.Watch)]
    [InlineData(CommandType.AiSessionEvent)]
    [InlineData(CommandType.ReadAloud)]
    [InlineData(CommandType.Dictation)]
    public void BlocksProtocolCommand_blocks_local_only_commands(CommandType type)
    {
        OctadockCommand command = OctadockCommand.Create(type, new Dictionary<string, string>
        {
            ["command"] = "dotnet test",
            ["pid"] = "1234",
            ["session-id"] = Guid.NewGuid().ToString("D"),
            ["event"] = "heartbeat",
        });

        AutomationLaunchSafety
            .BlocksProtocolCommand(["octadock://run?command=dotnet%20test"], command)
            .Should().BeTrue();
    }

    [Fact]
    public void BlocksProtocolCommand_allows_local_cli_run_command()
    {
        OctadockCommand command = OctadockCommand.Create(CommandType.Run, new Dictionary<string, string>
        {
            ["command"] = "dotnet test",
        });

        AutomationLaunchSafety
            .BlocksProtocolCommand(["run", "--", "dotnet", "test"], command)
            .Should().BeFalse();
    }

    [Fact]
    public void BlocksProtocolCommand_allows_non_process_protocol_commands()
    {
        OctadockCommand command = OctadockCommand.Create(CommandType.CaptureArea);

        AutomationLaunchSafety
            .BlocksProtocolCommand(["octadock://capture-area"], command)
            .Should().BeFalse();
    }
}
