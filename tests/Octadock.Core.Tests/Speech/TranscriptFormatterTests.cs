using FluentAssertions;
using Octadock.Core.Speech;
using Xunit;

namespace Octadock.Core.Tests.Speech;

public sealed class TranscriptFormatterTests
{
    [Fact]
    public void Join_trims_parts_drops_empties_and_uses_single_spaces()
    {
        TranscriptFormatter.Join(["  hello there  ", "", "  ", "world "])
            .Should().Be("hello there world");
    }

    [Fact]
    public void Join_stitches_punctuation_split_onto_its_own_segment()
    {
        // A VAD boundary can land between a word and its punctuation: the next
        // segment decodes to a lone mark. The join must not leak "world .".
        TranscriptFormatter.Join(["Hello world", "."])
            .Should().Be("Hello world.");

        TranscriptFormatter.Join(["first sentence.", "second one", "!"])
            .Should().Be("first sentence. second one!");
    }

    [Fact]
    public void Join_handles_commas_colons_and_question_marks_at_boundaries()
    {
        TranscriptFormatter.Join(["items : alpha", ",", "beta", "?"])
            .Should().Be("items: alpha, beta?");
    }

    [Fact]
    public void Normalize_collapses_whitespace_runs()
    {
        TranscriptFormatter.Normalize("  too \t many\n\n spaces   here ")
            .Should().Be("too many spaces here");
    }

    [Fact]
    public void Normalize_does_not_reattach_opening_punctuation_or_apostrophes()
    {
        // Only closing punctuation is pulled back; quotes and apostrophes keep
        // their spacing so quoted text is not corrupted.
        TranscriptFormatter.Normalize("he said ' hi '")
            .Should().Be("he said ' hi '");
    }

    [Fact]
    public void Normalize_keeps_the_space_before_words_that_start_with_a_dot()
    {
        // ".NET"/".env" legitimately start with a period: pulling it back would
        // corrupt the preceding word ("then.NET").
        TranscriptFormatter.Normalize("then .NET test the project")
            .Should().Be("then .NET test the project");
        TranscriptFormatter.Join(["compose up, then", ".NET test passes"])
            .Should().Be("compose up, then .NET test passes");
    }

    [Fact]
    public void Normalize_leaves_well_formed_text_untouched()
    {
        const string text = "Version 8.0 ships today, and it works!";
        TranscriptFormatter.Normalize(text).Should().Be(text);
    }

    [Fact]
    public void Normalize_returns_empty_for_blank_input()
    {
        TranscriptFormatter.Normalize("").Should().BeEmpty();
        TranscriptFormatter.Normalize("   ").Should().BeEmpty();
    }
}
