using FluentAssertions;
using Octadock.Core.Speech;
using Xunit;

namespace Octadock.Core.Tests.Speech;

public sealed class TranscriptDictionaryTests
{
    [Fact]
    public void Apply_replaces_longest_spoken_form_first()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements =
        [
            new("arrow", "->"),
            new("arrow function", "=>"),
        ];

        TranscriptDictionary.Apply("an arrow function here", replacements)
            .Should().Be("an => here");
    }

    [Fact]
    public void Apply_is_case_insensitive()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements = [new("Open Brace", "{")];

        TranscriptDictionary.Apply("please open brace now", replacements)
            .Should().Be("please { now");
    }

    [Fact]
    public void Apply_returns_input_untouched_without_replacements()
    {
        TranscriptDictionary.Apply("unchanged", []).Should().Be("unchanged");
        TranscriptDictionary.Apply(string.Empty, [new("a", "b")]).Should().BeEmpty();
    }

    [Fact]
    public void Apply_never_rewrites_inside_a_longer_word()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements = [new("cat", "dog")];

        TranscriptDictionary.Apply("concatenate the catalog, cat", replacements)
            .Should().Be("concatenate the catalog, dog",
                "a dictionary entry must match whole spoken words, not substrings");
    }

    [Fact]
    public void Apply_matches_across_punctuation_boundaries()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements = [new("arrow function", "=>")];

        TranscriptDictionary.Apply("write an arrow function, then stop", replacements)
            .Should().Be("write an =>, then stop");
    }

    [Fact]
    public void Apply_keeps_replacement_text_literal()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements = [new("price", "$100")];

        TranscriptDictionary.Apply("the price is fair", replacements)
            .Should().Be("the $100 is fair", "'$' in a replacement must not act as a regex group");
    }

    [Fact]
    public void Apply_handles_spoken_forms_with_non_word_edges()
    {
        IReadOnlyList<KeyValuePair<string, string>> replacements = [new("c++", "CSharp")];

        TranscriptDictionary.Apply("port the c++ codebase", replacements)
            .Should().Be("port the CSharp codebase");
    }

    [Fact]
    public void Parse_returns_starter_dictionary_for_empty_input()
    {
        TranscriptDictionary.Parse(null).Should().BeSameAs(TranscriptDictionary.DefaultCodeDictionary);
        TranscriptDictionary.Parse("   ").Should().BeSameAs(TranscriptDictionary.DefaultCodeDictionary);
    }

    [Fact]
    public void Parse_supports_both_separators_and_skips_comments()
    {
        IReadOnlyList<KeyValuePair<string, string>> parsed = TranscriptDictionary.Parse(
            """
            # comment
            fat arrow => =>
            log line = Console.WriteLine
            broken-line-without-separator
            """);

        parsed.Should().Contain(new KeyValuePair<string, string>("fat arrow", "=>"));
        parsed.Should().Contain(new KeyValuePair<string, string>("log line", "Console.WriteLine"));
        parsed.Should().Contain(TranscriptDictionary.DefaultCodeDictionary[0]);
        parsed.Should().NotContain(p => p.Key.Contains("broken"));
    }
}
