using FluentAssertions;
using Octadock.Core.Reading;
using Xunit;

namespace Octadock.Core.Tests.Reading;

public sealed class SentenceChunkerTests
{
    [Fact]
    public void Short_text_stays_one_chunk()
    {
        SentenceChunker.Split("Hello world. This is short.")
            .Should().ContainSingle().Which.Should().Be("Hello world. This is short.");
    }

    [Fact]
    public void Empty_input_yields_no_chunks()
    {
        SentenceChunker.Split(null).Should().BeEmpty();
        SentenceChunker.Split("   ").Should().BeEmpty();
    }

    [Fact]
    public void Long_text_splits_on_sentence_boundaries()
    {
        string first = "This is the first sentence, and it keeps going for a while to add length.";
        string second = "Here is the second sentence, also padded out to be reasonably long.";
        string third = "Finally a third one closes the paragraph.";
        string text = $"{first} {second} {third}";

        IReadOnlyList<string> chunks = SentenceChunker.Split(text, maxChars: 100);

        chunks.Should().HaveCount(3);
        chunks[0].Should().Be(first);
        chunks[1].Should().Be(second);
        chunks[2].Should().Be(third);
    }

    [Fact]
    public void Reassembled_chunks_preserve_every_word()
    {
        string text = string.Join(
            ' ',
            Enumerable.Range(1, 200).Select(i => $"Sentence number {i} carries some words."));

        IReadOnlyList<string> chunks = SentenceChunker.Split(text);

        chunks.Should().HaveCountGreaterThan(1);
        string.Join(' ', chunks).Should().Be(text);
        chunks.Should().OnlyContain(c => c.Length <= SentenceChunker.DefaultMaxChars);
    }

    [Fact]
    public void Decimal_numbers_do_not_split_sentences()
    {
        string text = "The value of pi is 3.14159 which we all know. " +
                      new string('x', 90);

        IReadOnlyList<string> chunks = SentenceChunker.Split(text, maxChars: 100);

        chunks[0].Should().Be("The value of pi is 3.14159 which we all know.");
    }

    [Fact]
    public void A_single_giant_token_hard_splits_instead_of_hanging()
    {
        string text = new('a', 500);

        IReadOnlyList<string> chunks = SentenceChunker.Split(text, maxChars: 100);

        chunks.Should().HaveCount(5);
        string.Concat(chunks).Should().Be(text);
    }

    [Fact]
    public void Line_breaks_are_chunk_boundaries()
    {
        string text = $"First paragraph line.\n{new string('b', 80)} more words here";

        IReadOnlyList<string> chunks = SentenceChunker.Split(text, maxChars: 90);

        chunks[0].Should().Be("First paragraph line.");
    }
}
