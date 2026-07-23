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
        help.Should().Contain("allinone");
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
        help.Should().Contain("--action copy|save|annotate|shelf|upload|discard");
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
    public void ForCommand_read_describes_verbatim_tts_internal_summary_status_and_elevenlabs()
    {
        string? help = HelpText.ForCommand("read");

        help.Should().NotBeNull();
        help.Should().Contain("octadock read");
        help.Should().Contain("Windows voices work");
        help.Should().Contain("trusted-summary engine");
        help.Should().Contain("internal prototype");
        help.Should().Contain("ElevenLabs");
        help.Should().Contain("OCTADOCK_ELEVENLABS_API_KEY");
        help.Should().Contain("octadock:// URLs are blocked");
    }

    [Fact]
    public void ForCommand_ai_describes_reviewed_handoff_and_cli_boundary()
    {
        string? help = HelpText.ForCommand("ask-ai");

        help.Should().NotBeNull();
        help.Should().Contain("octadock ai");
        help.Should().Contain("same reviewed handoff");
        help.Should().Contain("Shelf, Context, History, and Clipboard");
        help.Should().Contain("exact redacted packet");
        help.Should().Contain("Nothing is sent on open");
        help.Should().Contain("read-only Codex or Claude CLI");
        help.Should().Contain("Results are ephemeral unless you copy them");
        help.Should().Contain("prompt history");
        help.Should().Contain("no API key");
    }

    [Theory]
    [InlineData("capture-ocr", "octadock capture-text")]
    [InlineData("recording", "octadock record-screen")]
    [InlineData("allinone", "octadock all-in-one")]
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
