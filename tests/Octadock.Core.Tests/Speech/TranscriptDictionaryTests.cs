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
