using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Abstractions;
using Octadock.Core.Ai;
using Octadock.Core.Commands;
using Octadock.Core.Imaging;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AiActionsViewModelTests
{
    private const string Secret = "sk-proj-abcdefghijklmnopqrstuvwxyz123456";

    [Fact]
    public async Task Send_requires_confirmation_and_uses_the_same_reviewed_redacted_payload()
    {
        var actions = new RecordingActions();
        var confirmation = new RecordingConfirmation { Result = false };
        using AiActionsViewModel viewModel = Build(actions, confirmation);
        viewModel.InputText = $"summarize this; key={Secret}";

        viewModel.RedactSecrets.Should().BeTrue();
        viewModel.DetectedSecretCount.Should().Be(1);
        viewModel.OutboundPreview.Should().NotContain(Secret).And.Contain("[REDACTED:OPENAI_API_KEY]");
        viewModel.SendButtonText.Should().Be("Send to Codex");

        await viewModel.SendCommand.ExecuteAsync(null);
        actions.Executed.Should().BeEmpty();
        confirmation.LastReview.Should().NotBeNull();

        confirmation.Result = true;
        await viewModel.SendCommand.ExecuteAsync(null);

        actions.Executed.Should().ContainSingle();
        actions.Executed[0].Should().BeSameAs(confirmation.LastReview);
        viewModel.ResultText.Should().Be("generated result");
    }

    [Fact]
    public async Task Closing_during_send_cancels_without_a_null_reference_race()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actions = new RecordingActions
        {
            Execute = async (_, cancellationToken) =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new AiTextActionResult("unreachable", AiCliProviderIds.Codex, AiTextActionKind.Explain);
            },
        };
        var confirmation = new RecordingConfirmation { Result = true };
        AiActionsViewModel viewModel = Build(actions, confirmation);
        viewModel.InputText = "text";

        Task send = viewModel.SendCommand.ExecuteAsync(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        viewModel.Dispose();
        await send;

        viewModel.IsBusy.Should().BeFalse();
        viewModel.HasError.Should().BeFalse();
        viewModel.StatusText.Should().Contain("cancelled");
    }

    [Fact]
    public async Task Result_can_be_read_aloud_without_another_ai_send()
    {
        var dispatcher = new RecordingDispatcher();
        using AiActionsViewModel viewModel = Build(
            new RecordingActions(),
            new RecordingConfirmation { Result = true },
            dispatcher: dispatcher);
        viewModel.InputText = "text";
        await viewModel.SendCommand.ExecuteAsync(null);

        await viewModel.ReadAloudResultCommand.ExecuteAsync(null);

        dispatcher.Commands.Should().ContainSingle();
        dispatcher.Commands[0].Type.Should().Be(CommandType.ReadAloud);
        dispatcher.Commands[0].Get("text").Should().Be("generated result");
    }

    [Fact]
    public async Task Launch_command_loads_a_local_file_and_preselects_action_and_provider()
    {
        var loader = new FakeLoader
        {
            Input = new AiTextFileInput("file body", "brief.md"),
        };
        using AiActionsViewModel viewModel = Build(
            new RecordingActions(),
            new RecordingConfirmation(),
            loader: loader);
        OctadockCommand command = OctadockCommand.Create(
            CommandType.AiActions,
            new Dictionary<string, string>
            {
                ["action"] = "clean-rewrite",
                ["provider"] = "claude",
                ["filepath"] = @"C:\local\brief.md",
            });

        await viewModel.ApplyLaunchCommandAsync(command);

        loader.LastPath.Should().Be(@"C:\local\brief.md");
        viewModel.InputText.Should().Be("file body");
        viewModel.SourceName.Should().Be("brief.md");
        viewModel.SelectedAction.Kind.Should().Be(AiTextActionKind.CleanRewrite);
        viewModel.SelectedProvider.Descriptor.Id.Should().Be(AiCliProviderIds.Claude);
        viewModel.OutboundPreview.Should().Contain("Action: Clean rewrite");
    }

    private static AiActionsViewModel Build(
        IAiTextActionService actions,
        IAiSendConfirmation confirmation,
        RecordingDispatcher? dispatcher = null,
        IAiTextFileLoader? loader = null)
        => new(
            actions,
            confirmation,
            new FakeClipboard(),
            new FakePicker(),
            loader ?? new FakeLoader(),
            dispatcher ?? new RecordingDispatcher());

    private sealed class RecordingActions : IAiTextActionService
    {
        private readonly TextSecretDetector _detector = new();

        public IReadOnlyList<AiCliProviderDescriptor> Providers { get; } =
        [
            new(AiCliProviderIds.Codex, "Codex", "the Codex destination", true),
            new(AiCliProviderIds.Claude, "Claude", "the Claude destination", true),
        ];

        public List<AiOutboundReview> Executed { get; } = [];

        public Func<AiOutboundReview, CancellationToken, Task<AiTextActionResult>>? Execute { get; init; }

        public AiOutboundReview Review(AiTextActionRequest request)
        {
            AiCliProviderDescriptor provider = Providers.Single(item => item.Id == request.ProviderId);
            string prompt = AiTextActionPromptBuilder.Build(request.Action, request.Text, request.SourceName);
            TextSecretScanResult scan = _detector.Scan(prompt);
            return new AiOutboundReview
            {
                Action = request.Action,
                ProviderId = provider.Id,
                ProviderDisplayName = provider.DisplayName,
                DestinationDisclosure = provider.DestinationDisclosure,
                OutboundText = request.RedactSecrets ? scan.RedactedText : prompt,
                SourceCharacterCount = request.Text.Length,
                DetectedSecretCount = scan.Findings.Count,
                SecretsRedacted = request.RedactSecrets,
            };
        }

        public Task<AiTextActionResult> ExecuteReviewedAsync(
            AiOutboundReview review,
            CancellationToken cancellationToken = default)
        {
            Executed.Add(review);
            return Execute?.Invoke(review, cancellationToken)
                ?? Task.FromResult(new AiTextActionResult("generated result", review.ProviderId, review.Action));
        }
    }

    private sealed class RecordingConfirmation : IAiSendConfirmation
    {
        public bool Result { get; set; }

        public AiOutboundReview? LastReview { get; private set; }

        public bool Confirm(AiOutboundReview review)
        {
            LastReview = review;
            return Result;
        }
    }

    private sealed class RecordingDispatcher : ICommandDispatcher
    {
        public List<OctadockCommand> Commands { get; } = [];

        public Task<CommandResult> DispatchAsync(
            OctadockCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(CommandResult.Ok);
        }
    }

    private sealed class FakeLoader : IAiTextFileLoader
    {
        public AiTextFileInput Input { get; set; } = new("loaded", "file.txt");

        public string? LastPath { get; private set; }

        public Task<AiTextFileInput> LoadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            LastPath = path;
            return Task.FromResult(Input);
        }
    }

    private sealed class FakePicker : IAiTextFilePicker
    {
        public string? PickFile() => null;
    }

    private sealed class FakeClipboard : IClipboardService
    {
        public bool ContainsImage() => false;

        public void SetImage(EncodedImage image) { }

        public void SetImageFromFile(string filePath) { }

        public void SetFileDropList(IEnumerable<string> filePaths) { }

        public void SetText(string text) { }

        public string? TryGetText() => "clipboard text";

        public EncodedImage? TryGetImage() => null;
    }
}
