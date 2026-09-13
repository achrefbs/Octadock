using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class LocalCommandRoutingTests
{
    private static CommandDispatcher Dispatcher(IWindowPresenter? presenter = null) => new(
        null!, null!, null!, presenter!, null!, null!, null!, null!, null!, NullLogger<CommandDispatcher>.Instance);

    [Fact]
    public async Task Legacy_ai_command_opens_local_export()
    {
        var presenter = new Presenter();
        var result = await Dispatcher(presenter).DispatchAsync(OctadockCommand.Create(CommandType.AiActions,
            new Dictionary<string, string> { ["text"] = "Local evidence" }));
        result.Success.Should().BeTrue();
        result.Message.Should().Contain("local export");
        presenter.Command!.Get("text").Should().Be("Local evidence");
    }

    [Theory]
    [InlineData(CommandType.Activate)]
    [InlineData(CommandType.AllInOne)]
    [InlineData(CommandType.Pin)]
    [InlineData(CommandType.OpenAnnotate)]
    [InlineData(CommandType.OpenFromClipboard)]
    [InlineData(CommandType.AddShelfItem)]
    [InlineData(CommandType.Open)]
    public async Task Removed_commands_fail_without_resolving_any_service(CommandType command)
    {
        var result = await Dispatcher().DispatchAsync(OctadockCommand.Create(command));
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("removed");
    }

    [Fact]
    public void Capture_success_requires_a_durable_artifact()
    {
        var id = Guid.NewGuid();
        CommandDispatcher.CaptureCommandResult(id).CaptureId.Should().Be(id);
        CommandDispatcher.CaptureCommandResult(null).Success.Should().BeFalse();
    }

    private sealed class Presenter : IWindowPresenter
    {
        public OctadockCommand? Command { get; private set; }
        public void ShowHistory() { }
        public void ShowClipboardHistory() { }
        public void ShowTextTools() { }
        public void ShowContext() { }
        public void ShowAiActions(OctadockCommand? launchCommand = null) => Command = launchCommand;
        public void ShowSettings(string? tab = null) { }
        public Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
