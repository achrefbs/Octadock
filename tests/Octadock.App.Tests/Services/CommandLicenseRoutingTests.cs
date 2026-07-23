using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Ai;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class CommandLicenseRoutingTests
{
    [Fact]
    public async Task Ai_compatibility_command_opens_review_without_sending_or_rejecting_the_alias()
    {
        var presenter = new RecordingPresenter();
        var dispatcher = new CommandDispatcher(
            null!,
            null!,
            null!,
            presenter,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            null!,
            NullLogger<CommandDispatcher>.Instance);
        OctadockCommand command = OctadockCommand.Create(
            CommandType.AiActions,
            new Dictionary<string, string> { ["text"] = "An explicit local artifact" });

        CommandResult result = await dispatcher.DispatchAsync(command);

        result.Success.Should().BeTrue();
        result.Message.Should().Contain("Nothing is sent");
        presenter.ReviewCommand.Should().NotBeNull();
        presenter.ReviewCommand!.Get("text").Should().Be("An explicit local artifact");
        presenter.ReviewCommand.Get(AgentReviewLaunch.ReviewSourceParameter).Should().Be("automation");
    }

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

    [Fact]
    public void Capture_result_exposes_only_a_durable_identifier_and_rejects_no_artifact()
    {
        Guid captureId = Guid.NewGuid();

        CommandResult success = CommandDispatcher.CaptureCommandResult(captureId);
        CommandResult cancelled = CommandDispatcher.CaptureCommandResult(null);

        success.Success.Should().BeTrue();
        success.CaptureId.Should().Be(captureId);
        success.Message.Should().Be("Capture completed.");
        cancelled.Success.Should().BeFalse();
        cancelled.CaptureId.Should().BeNull();
        cancelled.Message.Should().Contain("cancelled");
    }

    [Theory]
    [InlineData(CommandType.Pin)]
    [InlineData(CommandType.OpenAnnotate)]
    [InlineData(CommandType.OpenFromClipboard)]
    [InlineData(CommandType.AddShelfItem)]
    [InlineData(CommandType.Open)]
    public async Task Removed_commands_fail_truthfully_without_touching_services_or_the_license_gate(
        CommandType command)
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
            gate,
            null!,
            NullLogger<CommandDispatcher>.Instance);

        CommandResult result = await dispatcher.DispatchAsync(OctadockCommand.Create(command));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("removed in this version");
        result.Message.Should().NotContain("active trial or license");
        gate.Requests.Should().BeEmpty("a removed feature must never reach the license gate");
    }

    [Theory]
    [InlineData(CommandType.CaptureText, GatedFeature.Ocr)]
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

    private sealed class RecordingPresenter : IWindowPresenter
    {
        public OctadockCommand? ReviewCommand { get; private set; }

        public void ShowHistory() { }
        public void ShowClipboardHistory() { }
        public void ShowTextTools() { }
        public void ShowContext() { }
        public void ShowAiActions(OctadockCommand? launchCommand = null) => ReviewCommand = launchCommand;
        public void ShowSettings(string? tab = null) { }
        public void ShowAllInOneHud(
            CaptureMode? mode = null,
            PixelRect? preloadedRegion = null,
            int? preloadedWidth = null,
            int? preloadedHeight = null) { }
        public Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }
}
