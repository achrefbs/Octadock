using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace Octadock.Cli.Tests;

public sealed class CliConsoleTests
{
    [Fact]
    public void Json_success_includes_capture_id_for_machine_correlation()
    {
        Guid captureId = Guid.NewGuid();
        var output = new StringWriter();
        var error = new StringWriter();
        var console = new CliConsole(json: true, output, error);

        console.Success("Capture completed.", ExitCodes.Ok, captureId);

        using JsonDocument result = JsonDocument.Parse(output.ToString());
        result.RootElement.GetProperty("success").GetBoolean().Should().BeTrue();
        result.RootElement.GetProperty("captureId").GetGuid().Should().Be(captureId);
        error.ToString().Should().BeEmpty();
    }

    [Fact]
    public void Text_success_keeps_the_human_readable_contract()
    {
        var output = new StringWriter();
        var console = new CliConsole(json: false, output, new StringWriter());

        console.Success("Capture completed.", ExitCodes.Ok, Guid.NewGuid());

        output.ToString().Trim().Should().Be("Capture completed.");
    }
}
