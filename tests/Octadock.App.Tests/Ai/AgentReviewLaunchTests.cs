using FluentAssertions;
using Octadock.App.Ai;
using Octadock.Core.Ai;
using Octadock.Core.Commands;
using Xunit;

namespace Octadock.App.Tests.Ai;

public sealed class AgentReviewLaunchTests
{
    [Fact]
    public void Every_product_entry_source_uses_the_same_contextual_review_contract()
    {
        Guid captureId = Guid.NewGuid();
        Guid contextId = Guid.NewGuid();
        Guid contextItemId = Guid.NewGuid();
        var commands = new Dictionary<string, OctadockCommand>
        {
            ["shelf"] = AgentReviewLaunch.FromShelf(captureId, "capture.png", "Terminal", "build"),
            ["context"] = AgentReviewLaunch.FromContext(contextId, [contextItemId], "Release Context"),
            ["history"] = AgentReviewLaunch.FromHistory(captureId, "history.png", "Browser"),
            ["clipboard"] = AgentReviewLaunch.FromClipboardText("copied text", "Visual Studio Code"),
            ["dock"] = AgentReviewLaunch.FromDock(),
            ["tray"] = AgentReviewLaunch.FromTray(),
        };

        commands.Should().OnlyContain(pair => pair.Value.Type == CommandType.AiActions);
        commands.Should().OnlyContain(pair =>
            pair.Value.Get(AgentReviewLaunch.ReviewSourceParameter) == pair.Key);
        commands.Values.Should().OnlyContain(command =>
            !string.IsNullOrWhiteSpace(command.Get(AgentReviewLaunch.ReviewLabelParameter)));

        commands["shelf"].Get("captureid").Should().Be(captureId.ToString("D"));
        commands["shelf"].Get("workflow").Should().Be("build");
        commands["context"].Get("contextitems").Should().Be(contextItemId.ToString("D"));
        commands["history"].Get("workflow").Should().Be("choose");
        commands["clipboard"].Get("text").Should().Be("copied text");
    }

    [Fact]
    public void Clipboard_image_and_text_keep_the_same_source_identity()
    {
        OctadockCommand text = AgentReviewLaunch.FromClipboardText("body", "Editor");
        OctadockCommand image = AgentReviewLaunch.FromClipboardImage(@"C:\local\clip.png", "Editor");

        image.Get(AgentReviewLaunch.ReviewSourceParameter).Should().Be(
            text.Get(AgentReviewLaunch.ReviewSourceParameter));
        image.Get(AgentReviewLaunch.ReviewLabelParameter).Should().Be(
            text.Get(AgentReviewLaunch.ReviewLabelParameter));
        image.Get("workflow").Should().Be("choose");
    }

    [Fact]
    public void Automation_adapter_preserves_evidence_and_adds_review_identity_only()
    {
        OctadockCommand input = OctadockCommand.Create(
            CommandType.AiActions,
            new Dictionary<string, string>
            {
                ["text"] = "private local evidence",
                ["action"] = "explain",
            });

        OctadockCommand review = AgentReviewLaunch.FromAutomation(input);

        review.Get("text").Should().Be("private local evidence");
        review.Get("action").Should().Be("explain");
        review.Get(AgentReviewLaunch.ReviewSourceParameter).Should().Be("automation");
        review.Get(AgentReviewLaunch.ReviewLabelParameter).Should().Be("CLI or protocol");
    }

    [Fact]
    public void Display_label_is_single_line_and_bounded()
    {
        string unsafeLabel = "  Build\r\nheader\t" + new string('x', 180);

        string normalized = AgentReviewLaunch.NormalizeLabel(unsafeLabel);

        normalized.Should().NotContainAny("\r", "\n", "\t");
        normalized.Should().StartWith("Build header ");
        normalized.Should().HaveLength(120).And.EndWith("…");
    }

    [Fact]
    public void Generated_titles_are_single_line_and_bounded_for_every_variable_name_source()
    {
        string longName = "evidence\r\n" + new string('x', 260) + ".png";
        Guid id = Guid.NewGuid();
        OctadockCommand[] commands =
        [
            AgentReviewLaunch.FromShelf(id, longName, null, "build"),
            AgentReviewLaunch.FromContext(id, [Guid.NewGuid()], longName),
            AgentReviewLaunch.FromHistory(id, longName, null),
            AgentReviewLaunch.FromAutomation(OctadockCommand.Create(
                CommandType.AiActions,
                new Dictionary<string, string> { ["title"] = longName })),
        ];

        commands.Select(command => command.Title).Should().OnlyContain(title =>
            title != null &&
            title.Length <= AgentPacketLimits.MaxTitleCharacters &&
            !title.Contains('\r') &&
            !title.Contains('\n'));
    }
}
