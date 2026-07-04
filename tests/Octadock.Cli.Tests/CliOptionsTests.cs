using FluentAssertions;
using Xunit;

namespace Octadock.Cli.Tests;

public sealed class CliOptionsTests
{
    [Fact]
    public void TryParse_consumes_global_options_before_command_verb()
    {
        bool ok = CliOptions.TryParse(
            ["--json", "--timeout", "3.5", "--no-launch", "capture-area"],
            out CliOptions options,
            out string? error);

        ok.Should().BeTrue(error);
        options.Json.Should().BeTrue();
        options.NoLaunch.Should().BeTrue();
        options.Timeout.Should().Be(TimeSpan.FromSeconds(3.5));
        options.CommandArguments.Should().Equal("capture-area");
    }

    [Fact]
    public void TryParse_leaves_global_looking_options_after_command_verb_for_command_parser()
    {
        bool ok = CliOptions.TryParse(
            ["capture-area", "--timeout", "5", "--json"],
            out CliOptions options,
            out string? error);

        ok.Should().BeTrue(error);
        options.Json.Should().BeFalse();
        options.Timeout.Should().Be(CliOptions.DefaultTimeout);
        options.CommandArguments.Should().Equal("capture-area", "--timeout", "5", "--json");
    }

    [Fact]
    public void TryParse_consumes_help_after_command_verb()
    {
        bool ok = CliOptions.TryParse(
            ["capture-area", "--help"],
            out CliOptions options,
            out string? error);

        ok.Should().BeTrue(error);
        options.Help.Should().BeTrue();
        options.CommandArguments.Should().Equal("capture-area");
    }

    [Fact]
    public void TryParse_stops_global_parsing_at_double_dash()
    {
        bool ok = CliOptions.TryParse(
            ["--json", "--", "capture-area", "--timeout", "5"],
            out CliOptions options,
            out string? error);

        ok.Should().BeTrue(error);
        options.Json.Should().BeTrue();
        options.Timeout.Should().Be(CliOptions.DefaultTimeout);
        options.CommandArguments.Should().Equal("capture-area", "--timeout", "5");
    }

    [Fact]
    public void TryParse_preserves_run_command_delimiter_after_verb()
    {
        bool ok = CliOptions.TryParse(
            ["run", "--", "dotnet", "test", "--filter", "Name With Space"],
            out CliOptions options,
            out string? error);

        ok.Should().BeTrue(error);
        options.CommandArguments.Should().Equal("run", "--", "dotnet", "test", "--filter", "Name With Space");
    }
}
