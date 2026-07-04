using FluentAssertions;
using Octadock.Core.Models;
using Octadock.Core.Naming;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public class FilenameGeneratorTests
{
    private readonly FilenameGenerator _generator = new();

    private static FilenameContext Context(
        DateTimeOffset? timestamp = null,
        CaptureType type = CaptureType.Area,
        string? process = null,
        string? window = null,
        int counter = 0) => new()
        {
            Timestamp = timestamp ?? new DateTimeOffset(2026, 7, 1, 8, 5, 9, TimeSpan.Zero),
            Type = type,
            ProcessName = process,
            WindowTitle = window,
            Counter = counter,
        };

    [Fact]
    public void Generate_expands_default_template()
    {
        string name = _generator.Generate(
            "Screenshot {yyyy}-{MM}-{dd} at {HH}.{mm}.{ss}",
            Context());

        name.Should().Be("Screenshot 2026-07-01 at 08.05.09");
    }

    [Fact]
    public void Generate_expands_date_time_tokens_with_zero_padding()
    {
        var ctx = Context(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero));
        _generator.Generate("{yyyy}{MM}{dd}-{HH}{mm}{ss}", ctx).Should().Be("20260102-030405");
    }

    [Fact]
    public void Generate_expands_process_window_type_counter()
    {
        var ctx = Context(type: CaptureType.Window, process: "explorer.exe", window: "Documents", counter: 3);

        string name = _generator.Generate("{process}-{window}-{type}-{counter}", ctx);

        // process extension stripped, type lowercased.
        name.Should().Be("explorer-Documents-window-3");
    }

    [Fact]
    public void Generate_uses_default_template_when_blank()
    {
        string name = _generator.Generate("", Context());
        name.Should().StartWith("Screenshot 2026-07-01");
    }

    [Fact]
    public void Generate_leaves_unknown_tokens_verbatim()
    {
        _generator.Generate("A{bogus}B", Context()).Should().Be("A{bogus}B");
    }

    [Fact]
    public void Generate_sanitizes_illegal_characters_from_window_title()
    {
        var ctx = Context(window: "Report: Q3/Q4 <final>");
        string name = _generator.Generate("{window}", ctx);

        name.Should().NotContainAny(":", "/", "<", ">", "\"", "\\", "|", "?", "*");
        name.Should().Contain("Report");
        name.Should().Contain("final");
    }

    [Theory]
    [InlineData("a<b", "a_b")]
    [InlineData("a>b", "a_b")]
    [InlineData("a:b", "a_b")]
    [InlineData("a\"b", "a_b")]
    [InlineData("a/b", "a_b")]
    [InlineData("a\\b", "a_b")]
    [InlineData("a|b", "a_b")]
    [InlineData("a?b", "a_b")]
    [InlineData("a*b", "a_b")]
    public void Sanitize_replaces_each_illegal_char(string input, string expected)
    {
        _generator.Sanitize(input).Should().Be(expected);
    }

    [Fact]
    public void Sanitize_collapses_runs_of_replacement()
    {
        _generator.Sanitize("a///b").Should().Be("a_b");
        _generator.Sanitize("a??**b").Should().Be("a_b");
    }

    [Fact]
    public void Sanitize_trims_leading_trailing_dots_and_spaces()
    {
        _generator.Sanitize("  hello.  ").Should().Be("hello");
        _generator.Sanitize("...name...").Should().Be("name");
    }

    [Theory]
    [InlineData("CON")]
    [InlineData("con")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    public void Sanitize_guards_reserved_device_names(string reserved)
    {
        string result = _generator.Sanitize(reserved);
        result.Should().NotBe(reserved);
        result.Should().StartWith("_");
    }

    [Fact]
    public void Sanitize_guards_reserved_name_with_extension()
    {
        // CON.txt is still reserved on Windows.
        _generator.Sanitize("CON.txt").Should().StartWith("_");
    }

    [Fact]
    public void Sanitize_allows_non_reserved_similar_names()
    {
        _generator.Sanitize("CONSOLE").Should().Be("CONSOLE");
        _generator.Sanitize("COM10").Should().Be("COM10");
    }

    [Fact]
    public void Sanitize_truncates_to_safe_length()
    {
        string huge = new('a', 500);
        string result = _generator.Sanitize(huge);
        result.Length.Should().BeLessThanOrEqualTo(FilenameGenerator.MaxBaseNameLength);
    }

    [Fact]
    public void Sanitize_truncates_without_splitting_utf16_surrogate_pair()
    {
        string candidate = new string('a', FilenameGenerator.MaxBaseNameLength - 1) + char.ConvertFromUtf32(0x1F4F8);

        string result = _generator.Sanitize(candidate);

        result.Length.Should().Be(FilenameGenerator.MaxBaseNameLength - 1);
        result.Should().NotEndWith("\ud83d");
    }

    [Fact]
    public void Sanitize_empty_or_all_illegal_falls_back()
    {
        _generator.Sanitize("").Should().Be("capture");
        _generator.Sanitize("///").Should().Be("capture");
        _generator.Sanitize("   ").Should().Be("capture");
    }

    [Fact]
    public void Generate_result_is_a_valid_windows_filename()
    {
        var ctx = Context(process: "app:name.exe", window: "a/b\\c|d?e");
        string name = _generator.Generate("{process} {window} {counter}", ctx);

        char[] invalid = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
        name.IndexOfAny(invalid).Should().Be(-1);
    }
}
