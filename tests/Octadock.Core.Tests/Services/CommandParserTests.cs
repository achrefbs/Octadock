using FluentAssertions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class CommandParserTests
{
    private readonly CommandParser _parser = new();

    [Theory]
    [InlineData("octadock://all-in-one", CommandType.AllInOne)]
    [InlineData("octadock://capture-area", CommandType.CaptureArea)]
    [InlineData("octadock://capture-previous-area", CommandType.CapturePreviousArea)]
    [InlineData("octadock://capture-fullscreen", CommandType.CaptureFullscreen)]
    [InlineData("octadock://capture-window", CommandType.CaptureWindow)]
    [InlineData("octadock://self-timer", CommandType.SelfTimer)]
    [InlineData("octadock://scrolling-capture", CommandType.ScrollingCapture)]
    [InlineData("octadock://pin", CommandType.Pin)]
    [InlineData("octadock://record-screen", CommandType.RecordScreen)]
    [InlineData("octadock://capture-text", CommandType.CaptureText)]
    [InlineData("octadock://read", CommandType.ReadAloud)]
    [InlineData("octadock://ai", CommandType.AiActions)]
    [InlineData("octadock://dictation", CommandType.Dictation)]
    [InlineData("octadock://open-annotate", CommandType.OpenAnnotate)]
    [InlineData("octadock://open-from-clipboard", CommandType.OpenFromClipboard)]
    [InlineData("octadock://add-shelf-item", CommandType.AddShelfItem)]
    [InlineData("octadock://open-history", CommandType.OpenHistory)]
    [InlineData("octadock://open-context", CommandType.OpenContext)]
    [InlineData("octadock://restore-recently-closed", CommandType.RestoreRecentlyClosed)]
    [InlineData("octadock://clear-history", CommandType.ClearHistory)]
    [InlineData("octadock://open-settings", CommandType.OpenSettings)]
    public void ParseUri_recognizes_every_verb(string uri, CommandType expected)
    {
        CommandParseResult result = _parser.ParseUri(uri);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(expected);
        result.ExitCode.Should().Be(0);
    }

    [Theory]
    [InlineData("capture-area", CommandType.CaptureArea)]
    [InlineData("capture-fullscreen", CommandType.CaptureFullscreen)]
    [InlineData("pin", CommandType.Pin)]
    [InlineData("open-settings", CommandType.OpenSettings)]
    [InlineData("open-context", CommandType.OpenContext)]
    [InlineData("record-screen", CommandType.RecordScreen)]
    [InlineData("read", CommandType.ReadAloud)]
    [InlineData("ai", CommandType.AiActions)]
    [InlineData("dictation", CommandType.Dictation)]
    public void ParseArguments_recognizes_canonical_tokens(string verb, CommandType expected)
    {
        CommandParseResult result = _parser.ParseArguments([verb]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(expected);
    }

    [Theory]
    [InlineData("ocr", CommandType.CaptureText)]
    [InlineData("capture-ocr", CommandType.CaptureText)]
    [InlineData("read-aloud", CommandType.ReadAloud)]
    [InlineData("ask-ai", CommandType.AiActions)]
    [InlineData("ai-actions", CommandType.AiActions)]
    [InlineData("agent", CommandType.AiActions)]
    [InlineData("agent-workspace", CommandType.AiActions)]
    [InlineData("handoff", CommandType.AiActions)]
    [InlineData("explain", CommandType.AiActions)]
    [InlineData("summarize", CommandType.AiActions)]
    [InlineData("dictate", CommandType.Dictation)]
    [InlineData("speech", CommandType.Dictation)]
    [InlineData("settings", CommandType.OpenSettings)]
    [InlineData("record", CommandType.RecordScreen)]
    [InlineData("recording", CommandType.RecordScreen)]
    [InlineData("history", CommandType.OpenHistory)]
    [InlineData("context", CommandType.OpenContext)]
    [InlineData("context-stack", CommandType.OpenContext)]
    [InlineData("annotate", CommandType.OpenAnnotate)]
    [InlineData("allinone", CommandType.AllInOne)]
    public void ParseArguments_recognizes_friendly_aliases(string verb, CommandType expected)
    {
        CommandParseResult result = _parser.ParseArguments([verb]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(expected);
    }

    [Fact]
    public void ParseUri_reads_action_monitor_and_region()
    {
        CommandParseResult result = _parser.ParseUri(
            "octadock://capture-area?x=100&y=120&width=800&height=600&monitor=1&action=annotate");

        result.Success.Should().BeTrue(result.Error);
        OctadockCommand cmd = result.Command!;
        cmd.Type.Should().Be(CommandType.CaptureArea);
        cmd.Region.Should().Be(new PixelRect(100, 120, 800, 600));
        cmd.Monitor.Should().Be("1");
        cmd.Action.Should().Be(PostCaptureAction.Annotate);
    }

    [Fact]
    public void ParseUri_defaults_action_to_shelf()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area");
        result.Command!.Action.Should().Be(PostCaptureAction.Shelf);
    }

    [Theory]
    [InlineData("copy", PostCaptureAction.Copy)]
    [InlineData("save", PostCaptureAction.Save)]
    [InlineData("annotate", PostCaptureAction.Annotate)]
    [InlineData("upload", PostCaptureAction.Upload)]
    [InlineData("pin", PostCaptureAction.Pin)]
    [InlineData("discard", PostCaptureAction.Discard)]
    [InlineData("shelf", PostCaptureAction.Shelf)]
    public void ParseUri_maps_every_action(string action, PostCaptureAction expected)
    {
        CommandParseResult result = _parser.ParseUri($"octadock://capture-area?action={action}");
        result.Command!.Action.Should().Be(expected);
    }

    [Fact]
    public void ParseUri_reads_units_dip()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area?x=0&y=0&width=10&height=10&units=dip");
        result.Command!.Units.Should().Be(CoordinateUnits.Dip);
    }

    [Fact]
    public void ParseUri_defaults_units_to_pixels()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area");
        result.Command!.Units.Should().Be(CoordinateUnits.Pixels);
    }

    [Fact]
    public void ParseUri_reads_mode()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area?mode=ocr");
        result.Command!.Mode.Should().Be(CaptureMode.Ocr);
    }

    [Fact]
    public void ParseArguments_reads_size_without_a_full_region()
    {
        CommandParseResult result = _parser.ParseArguments(
            ["capture-area", "--width", "1200", "--height", "800"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.CaptureArea);
        result.Command.GetInt("width").Should().Be(1200);
        result.Command.GetInt("height").Should().Be(800);
    }

    [Fact]
    public void ParseUri_bare_flag_becomes_true()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area?silent");
        result.Command!.Silent.Should().BeTrue();
    }

    [Fact]
    public void ParseUri_silent_true_is_read()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-area?silent=true");
        result.Command!.Silent.Should().BeTrue();
    }

    [Fact]
    public void ParseUri_decodes_encoded_filepath()
    {
        CommandParseResult result = _parser.ParseUri(
            "octadock://pin?filepath=C%3A%5CUsers%5Cme%5CDesktop%5Cref%20image.png");

        result.Success.Should().BeTrue(result.Error);
        result.Command!.FilePath.Should().Be(@"C:\Users\me\Desktop\ref image.png");
    }

    [Fact]
    public void ParseArguments_supports_space_separated_options()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-fullscreen", "--monitor", "1", "--action", "save"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Monitor.Should().Be("1");
        result.Command!.Action.Should().Be(PostCaptureAction.Save);
    }

    [Fact]
    public void ParseArguments_supports_equals_separated_options()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-area", "--action=copy"]);
        result.Command!.Action.Should().Be(PostCaptureAction.Copy);
    }

    [Fact]
    public void ParseArguments_boolean_flag_without_value()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-area", "--silent", "--action", "copy"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Silent.Should().BeTrue();
        result.Command!.Action.Should().Be(PostCaptureAction.Copy);
    }

    [Theory]
    [InlineData("open", "--filepath")]
    [InlineData("capture-area", "--action")]
    [InlineData("capture-fullscreen", "--monitor")]
    [InlineData("settings", "--tab")]
    public void ParseArguments_known_value_option_without_value_fails(string verb, string option)
    {
        CommandParseResult result = _parser.ParseArguments([verb, option]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("requires a value");
    }

    [Fact]
    public void ParseArguments_known_value_option_followed_by_another_long_option_fails()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-area", "--action", "--monitor", "1"]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("requires a value");
    }

    [Fact]
    public void ParseArguments_area_shorthand_expands_to_region()
    {
        // From api-and-data-spec.md: octadock.exe ocr --area 100,120,800,600 --mode lines
        CommandParseResult result = _parser.ParseArguments(["ocr", "--area", "100,120,800,600", "--mode", "lines"]);

        result.Success.Should().BeTrue(result.Error);
        OctadockCommand cmd = result.Command!;
        cmd.Type.Should().Be(CommandType.CaptureText);
        cmd.Region.Should().Be(new PixelRect(100, 120, 800, 600));
        cmd.Get("mode").Should().Be("lines");
    }

    [Fact]
    public void ParseArguments_capture_text_consumes_language_value()
    {
        CommandParseResult result = _parser.ParseArguments(
            ["capture-text", "--filepath", @"C:\shots\invoice.png", "--mode", "layout", "--language", "en-US"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.CaptureText);
        result.Command.FilePath.Should().Be(@"C:\shots\invoice.png");
        result.Command.Get("language").Should().Be("en-US");
    }

    [Fact]
    public void ParseArguments_read_consumes_text_and_voice_options()
    {
        CommandParseResult result = _parser.ParseArguments(
            [
                "read",
                "--text",
                "A long paragraph",
                "--style",
                "brief",
                "--length",
                "short",
                "--provider",
                "claude",
                "--voice-id",
                "voice123",
                "--model-id",
                "eleven_flash_v2_5",
            ]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.ReadAloud);
        result.Command.Get("text").Should().Be("A long paragraph");
        result.Command.Get("style").Should().Be("brief");
        result.Command.Get("length").Should().Be("short");
        result.Command.Get("provider").Should().Be("claude");
        result.Command.Get("voice-id").Should().Be("voice123");
        result.Command.Get("model-id").Should().Be("eleven_flash_v2_5");
    }

    [Fact]
    public void ParseArguments_ai_consumes_review_inputs_and_provider()
    {
        CommandParseResult result = _parser.ParseArguments(
            [
                "ai",
                "--action",
                "action-items",
                "--filepath",
                @"C:\notes\brief.md",
                "--provider",
                "claude",
            ]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.AiActions);
        result.Command.Get("action").Should().Be("action-items");
        result.Command.FilePath.Should().Be(@"C:\notes\brief.md");
        result.Command.Get("provider").Should().Be("claude");
    }

    [Fact]
    public void ParseArguments_agent_workspace_consumes_goal_sources_and_completion_contract()
    {
        CommandParseResult result = _parser.ParseArguments(
        [
            "agent",
            "--workflow", "investigate",
            "--goal", "Fix the captured regression",
            "--captureid", "34dc8a55-77c9-4fc0-91b2-fb10853d3fe4",
            "--contextid", "4164f42d-364b-4c79-95b8-bd45a20be7aa",
            "--criteria", "Layout is compact|Tests pass",
            "--project", "Octadock",
            "--target", "Settings",
            "--environment", "Windows 11",
            "--provider", "codex",
        ]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.AiActions);
        result.Command.Get("workflow").Should().Be("investigate");
        result.Command.Get("goal").Should().Be("Fix the captured regression");
        result.Command.Get("captureid").Should().NotBeNullOrWhiteSpace();
        result.Command.Get("contextid").Should().NotBeNullOrWhiteSpace();
        result.Command.Get("criteria").Should().Contain("Tests pass");
        result.Command.Get("project").Should().Be("Octadock");
        result.Command.Get("target").Should().Be("Settings");
        result.Command.Get("environment").Should().Be("Windows 11");
    }

    [Theory]
    [InlineData("explain", "explain")]
    [InlineData("summarize", "summarize")]
    public void Ai_aliases_seed_the_reviewed_action(string alias, string expectedAction)
    {
        CommandParseResult result = _parser.ParseArguments([alias, "--clipboard"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.AiActions);
        result.Command.Get("action").Should().Be(expectedAction);
        result.Command.GetBool("clipboard").Should().BeTrue();
    }

    [Theory]
    [InlineData("--provider", "other", "provider")]
    [InlineData("--action", "do-everything", "action")]
    [InlineData("--workflow", "make-magic", "workflow")]
    public void Ai_rejects_unknown_provider_or_action(string option, string value, string expectedError)
    {
        CommandParseResult result = _parser.ParseArguments(["ai", option, value]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain(expectedError);
    }

    [Theory]
    [InlineData("captureid")]
    [InlineData("contextid")]
    public void Agent_workspace_rejects_invalid_source_ids(string option)
    {
        CommandParseResult result = _parser.ParseArguments(["agent", $"--{option}", "not-a-guid"]);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain(option).And.Contain("GUID");
    }

    [Fact]
    public void ParseUri_area_shorthand_in_query()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-text?area=10,20,30,40");
        result.Command!.Region.Should().Be(new PixelRect(10, 20, 30, 40));
    }

    [Fact]
    public void ParseUri_capture_text_reads_coordinate_region()
    {
        CommandParseResult result = _parser.ParseUri("octadock://capture-text?x=10&y=20&width=30&height=40");

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.CaptureText);
        result.Command.Region.Should().Be(new PixelRect(10, 20, 30, 40));
    }

    [Fact]
    public void ParseArguments_settings_tab()
    {
        // From api-and-data-spec.md: octadock.exe settings --tab shortcuts
        CommandParseResult result = _parser.ParseArguments(["settings", "--tab", "shortcuts"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.OpenSettings);
        result.Command!.Get("tab").Should().Be("shortcuts");
    }

    [Fact]
    public void ParseArguments_pin_with_quoted_path()
    {
        CommandParseResult result = _parser.ParseArguments(["pin", "--filepath", @"C:\Users\me\Desktop\reference.png"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.FilePath.Should().Be(@"C:\Users\me\Desktop\reference.png");
    }

    [Fact]
    public void ParseArguments_pin_consumes_unix_style_absolute_path_as_value()
    {
        CommandParseResult result = _parser.ParseArguments(["pin", "--filepath", "/tmp/reference.png"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.FilePath.Should().Be("/tmp/reference.png");
    }

    [Fact]
    public void ParseArguments_pin_consumes_single_segment_unix_style_absolute_path_as_value()
    {
        CommandParseResult result = _parser.ParseArguments(["pin", "--filepath", "/tmp"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.FilePath.Should().Be("/tmp");
    }

    [Fact]
    public void ParseArguments_slash_style_boolean_flag_does_not_consume_next_slash_option()
    {
        CommandParseResult result = _parser.ParseArguments(["pin", "/clipboard", "/filepath", @"C:\ref.png"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.GetBool("clipboard").Should().BeTrue();
        result.Command!.FilePath.Should().Be(@"C:\ref.png");
    }

    [Fact]
    public void ParseArguments_slash_style_filepath_consumes_unix_style_absolute_path()
    {
        CommandParseResult result = _parser.ParseArguments(["pin", "/filepath", "/tmp"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.FilePath.Should().Be("/tmp");
    }

    [Fact]
    public void ParseArguments_keys_are_lowercased()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-area", "--Action", "COPY"]);
        result.Command!.Has("action").Should().BeTrue();
        result.Command!.Action.Should().Be(PostCaptureAction.Copy);
    }

    [Fact]
    public void ParseArguments_open_requires_filepath()
    {
        CommandParseResult result = _parser.ParseArguments(["open", "--filepath", @"C:\data\report.csv"]);

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.Open);
        result.Command!.FilePath.Should().Be(@"C:\data\report.csv");
    }

    [Fact]
    public void ParseArguments_open_without_filepath_fails()
    {
        CommandParseResult result = _parser.ParseArguments(["open"]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("filepath");
    }


    [Fact]
    public void ParseUri_open_reads_encoded_filepath()
    {
        CommandParseResult result = _parser.ParseUri(
            "octadock://open?filepath=C%3A%5Cdata%5Creport.csv");

        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.Open);
        result.Command!.FilePath.Should().Be(@"C:\data\report.csv");
    }

    // ---- Failure cases -------------------------------------------------

    [Fact]
    public void ParseUri_unknown_verb_fails_with_nonzero_exit()
    {
        CommandParseResult result = _parser.ParseUri("octadock://frobnicate");

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.Error.Should().Contain("frobnicate");
    }

    [Fact]
    public void ParseArguments_unknown_verb_fails()
    {
        CommandParseResult result = _parser.ParseArguments(["not-a-command"]);

        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.Command.Should().BeNull();
    }

    [Fact]
    public void ParseUri_wrong_scheme_fails()
    {
        CommandParseResult result = _parser.ParseUri("http://capture-area");
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("scheme");
    }

    [Theory]
    [InlineData("octadock://capture-area?x=abc&y=0&width=10&height=10")]
    [InlineData("octadock://capture-area?x=10&y=0&width=ten&height=10")]
    public void ParseUri_malformed_coordinate_fails(string uri)
    {
        CommandParseResult result = _parser.ParseUri(uri);
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void ParseArguments_malformed_area_fails()
    {
        _parser.ParseArguments(["ocr", "--area", "100,120,800"]).Success.Should().BeFalse();
        _parser.ParseArguments(["ocr", "--area", "a,b,c,d"]).Success.Should().BeFalse();
        _parser.ParseArguments(["ocr", "--area", ""]).Success.Should().BeFalse();
    }

    [Fact]
    public void ParseArguments_empty_fails()
    {
        _parser.ParseArguments([]).Success.Should().BeFalse();
    }

    [Fact]
    public void ParseUri_empty_fails()
    {
        _parser.ParseUri("").Success.Should().BeFalse();
        _parser.ParseUri("   ").Success.Should().BeFalse();
    }

    [Fact]
    public void ParseArguments_unexpected_positional_fails()
    {
        CommandParseResult result = _parser.ParseArguments(["capture-area", "oops"]);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void ParseArguments_unknown_boolean_flag_does_not_swallow_positional()
    {
        // Regression for D-10: "--silent C:\img.png" must not set silent to the
        // path; the stray positional should raise a loud error instead.
        CommandParseResult result = _parser.ParseArguments(["add-shelf-item", "--silent", @"C:\img.png"]);
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("img.png");
    }

    [Theory]
    [InlineData("octadock://capture-area?x=0&y=0&width=0&height=10")]
    [InlineData("octadock://capture-area?x=0&y=0&width=10&height=-5")]
    public void ParseUri_non_positive_region_extent_fails(string uri)
    {
        // Regression for D-5.
        CommandParseResult result = _parser.ParseUri(uri);
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
    }

    [Fact]
    public void ParseUri_allows_negative_region_origin()
    {
        // x/y may be negative on a secondary monitor; only width/height are bounded.
        CommandParseResult result = _parser.ParseUri(
            "octadock://capture-area?x=-1920&y=-100&width=800&height=600");
        result.Success.Should().BeTrue(result.Error);
        result.Command!.Region.Should().Be(new PixelRect(-1920, -100, 800, 600));
    }

    [Fact]
    public void GetEnum_rejects_undefined_numeric_value()
    {
        // Regression for D-4: action=999 must fall back to the documented default
        // rather than surfacing an undefined enum value.
        CommandParseResult result = _parser.ParseUri("octadock://capture-area?action=999");
        result.Success.Should().BeTrue(result.Error);
        result.Command!.Action.Should().Be(PostCaptureAction.Shelf);
    }

    // ---- URI / CLI parity ----------------------------------------------

    [Fact]
    public void Uri_and_cli_produce_equivalent_commands()
    {
        CommandParseResult fromUri = _parser.ParseUri(
            "octadock://capture-fullscreen?monitor=1&action=save");
        CommandParseResult fromCli = _parser.ParseArguments(
            ["capture-fullscreen", "--monitor", "1", "--action", "save"]);

        fromUri.Success.Should().BeTrue();
        fromCli.Success.Should().BeTrue();

        fromUri.Command!.Type.Should().Be(fromCli.Command!.Type);
        fromUri.Command!.Monitor.Should().Be(fromCli.Command!.Monitor);
        fromUri.Command!.Action.Should().Be(fromCli.Command!.Action);
    }

    [Fact]
    public void ParseArguments_accepts_full_uri_as_first_argument()
    {
        CommandParseResult result = _parser.ParseArguments(["octadock://capture-area?action=copy"]);
        result.Success.Should().BeTrue(result.Error);
        result.Command!.Type.Should().Be(CommandType.CaptureArea);
        result.Command!.Action.Should().Be(PostCaptureAction.Copy);
    }
}
