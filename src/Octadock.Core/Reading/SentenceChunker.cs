namespace Octadock.Core.Reading;

/// <summary>
/// Splits long text into speech-sized chunks on sentence boundaries so
/// read-aloud can synthesize the first chunk immediately (audio starts fast)
/// and prefetch the next while one plays. Chunks break after sentence enders
/// (. ! ? … and line breaks); a sentence longer than the limit falls back to
/// the last word boundary.
/// </summary>
public static class SentenceChunker
{
    /// <summary>Default chunk budget — a few sentences, ~15-25 s of speech.</summary>
    public const int DefaultMaxChars = 600;

    /// <summary>Splits <paramref name="text"/> into ordered, non-empty chunks.</summary>
    public static IReadOnlyList<string> Split(string? text, int maxChars = DefaultMaxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        maxChars = Math.Max(80, maxChars);
        var chunks = new List<string>();
        ReadOnlySpan<char> remaining = text.AsSpan().Trim();

        while (!remaining.IsEmpty)
        {
            if (remaining.Length <= maxChars)
            {
                AddChunk(chunks, remaining);
                break;
            }

            ReadOnlySpan<char> window = remaining[..maxChars];
            int cut = LastSentenceBreak(window);
            if (cut < 0)
            {
                cut = window.LastIndexOfAny(' ', '\t');
            }

            if (cut <= 0)
            {
                cut = maxChars - 1; // One giant token: hard split.
            }

            AddChunk(chunks, remaining[..(cut + 1)]);
            remaining = remaining[(cut + 1)..].TrimStart();
        }

        return chunks;
    }

    private static int LastSentenceBreak(ReadOnlySpan<char> window)
    {
        for (int i = window.Length - 1; i > 0; i--)
        {
            char c = window[i];
            if (c is '\n' or '\r')
            {
                return i;
            }

            // Break after the ender, but not inside "3.14" or "e.g."-style dots.
            if (c is '.' or '!' or '?' or '…' &&
                (i + 1 >= window.Length || char.IsWhiteSpace(window[i + 1])))
            {
                bool decimalDot = c == '.'
                    && i > 0
                    && char.IsDigit(window[i - 1])
                    && i + 1 < window.Length
                    && char.IsDigit(window[i + 1]);
                if (!decimalDot)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static void AddChunk(List<string> chunks, ReadOnlySpan<char> chunk)
    {
        string value = chunk.Trim().ToString();
        if (value.Length > 0)
        {
            chunks.Add(value);
        }
    }
}
