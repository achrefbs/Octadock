using FluentAssertions;
using Xunit;

namespace Octadock.Cli.Tests;

public sealed class HelpTextTests
{
    [Fact]
    public void Root_includes_open_command_and_current_aliases()
    {
        string help = HelpText.Root();

        help.Should().Contain("open");
        help.Should().Contain("Preview a local file in Octadock.");
        help.Should().Contain("dictation");
        help.Should().Contain("Toggle speech-to-text dictation");
        help.Should().Contain("read");
        help.Should().Contain("Read text, a file, clipboard text, or an OCR region aloud");
        help.Should().Contain("ai");
        help.Should().Contain("AI image editing now lives directly inside a pinned image");
        help.Should().Contain("allinone");
        help.Should().Contain("capture-ocr");
        help.Should().Contain("read-aloud");
        help.Should().Contain("explain");
        help.Should().Contain("dictate");
        help.Should().Contain("recording");
    }

    [Fact]
    public void ForCommand_open_describes_required_filepath()
    {
        string? help = HelpText.ForCommand("open");

        help.Should().NotBeNull();
        help.Should().Contain("octadock open --filepath <path>");
        help.Should().Contain("Preview a local file in Octadock.");
    }

    [Fact]
    public void ForCommand_capture_text_describes_language_option()
    {
        string? help = HelpText.ForCommand("capture-text");

        help.Should().NotBeNull();
        help.Should().Contain("--language <tag>");
    }

    [Fact]
    public void ForCommand_open_settings_lists_current_tabs()
    {
        string? help = HelpText.ForCommand("open-settings");

        help.Should().NotBeNull();
        help.Should().Contain("speech");
        help.Should().Contain("clipboard");
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
    public void ForCommand_ai_describes_native_pin_workflow_and_cli_boundary()
    {
        string? help = HelpText.ForCommand("ask-ai");

        help.Should().NotBeNull();
        help.Should().Contain("octadock ai");
        help.Should().Contain("no longer opens a separate AI screen");
        help.Should().Contain("press the sparkle");
        help.Should().Contain("full visible image");
        help.Should().Contain("signed-in Codex CLI");
        help.Should().Contain("cross-fades");
        help.Should().Contain("original capture stays untouched");
        help.Should().Contain("Undo restores");
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
    public void ForCommand_reflects_current_scrolling_recording_and_annotate_behavior()
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

        HelpText.ForCommand("open-annotate").Should()
            .Contain("octadock open-annotate --filepath <path>")
            .And.NotContain("capture-id");
    }
}
