using FluentAssertions;
using Xunit;

namespace Octadock.Cli.Tests;

public sealed class HelpTextTests
{
    [Fact]
    public void Root_lists_current_verbs_and_aliases()
    {
        string help = HelpText.Root();

        help.Should().Contain("open-history");
        help.Should().Contain("dictation");
        help.Should().Contain("Toggle speech-to-text dictation");
        help.Should().Contain("read");
        help.Should().Contain("Read text, a file, clipboard text, or an OCR region aloud");
        help.Should().Contain("ai");
        help.Should().Contain("source-bound handoff");
        help.Should().Contain("capture-ocr");
        help.Should().Contain("read-aloud");
        help.Should().Contain("explain");
        help.Should().Contain("dictate");
        help.Should().Contain("recording");
    }

    [Fact]
    public void Root_never_advertises_removed_verbs_or_their_aliases()
    {
        string help = HelpText.Root();

        help.Should().NotContain("Float an image");
        help.Should().NotContain("Preview a local file");
        help.Should().NotContain("add-shelf-item");
        help.Should().NotContain("all-in-one");
        help.Should().NotContain("allinone");
        help.Should().NotContain("open-annotate");
        help.Should().NotContain("open-from-clipboard");
        help.Should().NotContain("|pin|");
    }

    [Fact]
    public void ForCommand_returns_null_for_removed_verbs_and_their_aliases()
    {
        HelpText.ForCommand("open").Should().BeNull();
        HelpText.ForCommand("pin").Should().BeNull();
        HelpText.ForCommand("open-annotate").Should().BeNull();
        HelpText.ForCommand("open-from-clipboard").Should().BeNull();
        HelpText.ForCommand("add-shelf-item").Should().BeNull();
        HelpText.ForCommand("all-in-one").Should().BeNull();
        HelpText.ForCommand("all").Should().BeNull();
        HelpText.ForCommand("allinone").Should().BeNull();
        HelpText.ForCommand("annotate").Should().BeNull();
        HelpText.ForCommand("shelf").Should().BeNull();
    }

    [Fact]
    public void ForCommand_capture_text_describes_language_option()
    {
        string? help = HelpText.ForCommand("capture-text");

        help.Should().NotBeNull();
        help.Should().Contain("--language <tag>");
    }

    [Fact]
    public void ForCommand_capture_actions_no_longer_offer_pin()
    {
        string? help = HelpText.ForCommand("capture-area");

        help.Should().NotBeNull();
        help.Should().Contain("--action copy|save|annotate|shelf|discard");
    }

    [Fact]
    public void ForCommand_open_settings_lists_current_tabs()
    {
        string? help = HelpText.ForCommand("open-settings");

        help.Should().NotBeNull();
        help.Should().Contain("speech");
        help.Should().Contain("clipboard");
        help.Should().NotContain("pins");
    }

    [Fact]
    public void ForCommand_dictation_describes_local_only_toggle()
    {
        string? help = HelpText.ForCommand("dictate");

        help.Should().NotBeNull();
        help.Should().Contain("octadock dictation");
        help.Should().Contain("configured provider");
        help.Should().Contain("octadock:// URLs are blocked");
    }

    [Fact]
    public void Read_and_export_help_describe_only_local_processing()
    {
        HelpText.ForCommand("read").Should().Contain("installed Windows voices").And.NotContain("API_KEY");
        HelpText.ForCommand("ai").Should().Contain("save a bundle on this PC").And.NotContain("Codex");
        HelpText.ForCommand("activate").Should().BeNull();
    }

    [Theory]
    [InlineData("capture-ocr", "octadock capture-text")]
    [InlineData("recording", "octadock record-screen")]
    [InlineData("speech", "octadock dictation")]
    [InlineData("summarize", "octadock ai")]
    [InlineData("ai-actions", "octadock ai")]
    [InlineData("agent", "octadock ai")]
    [InlineData("handoff", "octadock ai")]
    public void ForCommand_resolves_parser_aliases(string alias, string canonicalUsage)
    {
        string? help = HelpText.ForCommand(alias);

        help.Should().NotBeNull();
        help.Should().Contain(canonicalUsage);
    }

    [Fact]
    public void ForCommand_reflects_current_scrolling_and_recording_behavior()
    {
        HelpText.ForCommand("scrolling-capture").Should()
            .Contain("manually scrolled vertical")
            .And.Contain("[--direction vertical]")
            .And.NotContain("horizontal")
            .And.NotContain("autoscroll");

        HelpText.ForCommand("record-screen").Should()
            .Contain("Toggle MP4 video recording for a monitor or selected area.")
            .And.Contain("--select-area")
            .And.Contain("--area x,y,width,height")
            .And.NotContain("microphone")
            .And.NotContain("system-audio");
    }
}
