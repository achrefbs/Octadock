using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class CommandLicenseRoutingTests
{
    [Fact]
    public async Task Denied_command_returns_failure_instead_of_false_success()
    {
        var gate = new DenyLicenseGate();
        var dispatcher = new CommandDispatcher(
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            gate,
            null!,
            NullLogger<CommandDispatcher>.Instance);

        CommandResult result = await dispatcher.DispatchAsync(OctadockCommand.Create(CommandType.CaptureArea));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("active trial or license");
        gate.Requests.Should().Equal(GatedFeature.Capture);
    }

    [Theory]
    [InlineData(CommandType.AllInOne)]
    [InlineData(CommandType.CaptureArea)]
    [InlineData(CommandType.CapturePreviousArea)]
    [InlineData(CommandType.CaptureFullscreen)]
    [InlineData(CommandType.CaptureWindow)]
    [InlineData(CommandType.SelfTimer)]
    [InlineData(CommandType.ScrollingCapture)]
    public void Capture_entry_points_are_refused_truthfully_at_the_command_boundary(CommandType command)
    {
        CommandDispatcher.RequiredLicenseFeature(command).Should().Be(GatedFeature.Capture);
    }

    [Theory]
    [InlineData(CommandType.Pin, GatedFeature.Pin)]
    [InlineData(CommandType.CaptureText, GatedFeature.Ocr)]
    [InlineData(CommandType.OpenAnnotate, GatedFeature.Pin)]
    [InlineData(CommandType.OpenFromClipboard, GatedFeature.Pin)]
    [InlineData(CommandType.AddShelfItem, GatedFeature.AddShelfItem)]
    [InlineData(CommandType.OpenTextTools, GatedFeature.TextTools)]
    public void Non_toggle_create_commands_map_to_their_service_gate(
        CommandType command,
        GatedFeature feature)
    {
        CommandDispatcher.RequiredLicenseFeature(command).Should().Be(feature);
    }

    [Theory]
    [InlineData(CommandType.RecordScreen)]
    [InlineData(CommandType.Dictation)]
    [InlineData(CommandType.ReadAloud)]
    public void Toggle_commands_are_not_preflight_blocked_so_stop_remains_available(CommandType command)
    {
        CommandDispatcher.RequiredLicenseFeature(command).Should().BeNull();
    }

    [Theory]
    [InlineData(CommandType.Open)]
    [InlineData(CommandType.OpenHistory)]
    [InlineData(CommandType.OpenClipboardHistory)]
    [InlineData(CommandType.OpenContext)]
    [InlineData(CommandType.RestoreRecentlyClosed)]
    [InlineData(CommandType.ClearHistory)]
    [InlineData(CommandType.OpenSettings)]
    [InlineData(CommandType.Quit)]
    public void Result_aware_or_existing_data_commands_do_not_need_command_preflight(CommandType command)
    {
        CommandDispatcher.RequiredLicenseFeature(command).Should().BeNull();
    }

    private sealed class DenyLicenseGate : ILicenseGate
    {
        public List<GatedFeature> Requests { get; } = [];

        public LicenseState State { get; } = new(LicenseMode.TrialExpired, null, null, false, false, "expired");

        public bool AllowsFullUse => false;

        public event EventHandler<LicenseState>? Refused { add { } remove { } }

        public bool Allow(GatedFeature feature)
        {
            Requests.Add(feature);
            return false;
        }
    }
}
