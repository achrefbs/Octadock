using System.Text;

namespace Octadock.Core.Speech;

/// <summary>
/// Mechanical text cleanup for engine transcripts and for joining decoded
/// speech segments. Engines already handle language, casing, and most
/// punctuation; this only fixes artifacts of the join itself — collapsed
/// whitespace and spaces left before closing punctuation when a VAD boundary
/// lands between a word and its punctuation mark ("world ." → "world.").
/// Deliberately conservative: no re-casing, no rewording, no grammar edits, so
/// the transcript stays the engine's honest output.
/// </summary>
public static class TranscriptFormatter
{
    // Punctuation that attaches to the preceding word when a segment boundary
    // leaves a stray space in front of it.
    private static readonly char[] ClosingPunctuation = [',', '.', '!', '?', ';', ':', '%'];

    /// <summary>
    /// Joins decoded segment texts into one transcript: trims each part, drops
    /// empty parts, joins with single spaces, and applies <see cref="Normalize"/>.
    /// </summary>
    public static string Join(IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return Normalize(string.Join(' ', parts
            .Select(static part => part?.Trim() ?? string.Empty)
            .Where(static part => part.Length > 0)));
    }

    /// <summary>
    /// Collapses whitespace runs to single spaces and removes spaces before
    /// closing punctuation — but only when the mark is not itself followed by
    /// a letter or digit, so words that legitimately start with a dot (".NET",
    /// ".env") keep their space. Leading/trailing whitespace is trimmed.
    /// </summary>
    public static string Normalize(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(transcript.Length);
        for (int i = 0; i < transcript.Length; i++)
        {
            char c = transcript[i];
            if (char.IsWhiteSpace(c))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (builder.Length > 0 && builder[^1] == ' ' &&
                ClosingPunctuation.Contains(c) && !NextIsWordChar(transcript, i))
            {
                builder.Length--; // Pull the punctuation back onto the previous word.
            }

            builder.Append(c);
        }

        return builder.ToString().TrimEnd();
    }

    private static bool NextIsWordChar(string text, int index)
        => index + 1 < text.Length && char.IsLetterOrDigit(text[index + 1]);
}
