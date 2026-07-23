using System.Text.RegularExpressions;

namespace Octadock.Core.Speech;

/// <summary>
/// The dictation dictionary: spoken-form → written-form replacements applied to
/// a finished transcript ("arrow function" → "=&gt;"). One implementation shared
/// by every speech provider so batch and streaming finalization behave
/// identically.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Naming",
    "CA1711:Identifiers should not have incorrect suffix",
    Justification = "'Dictionary' is the established user-facing speech feature name; preserving this public type avoids an API break.")]
public static class TranscriptDictionary
{
    /// <summary>Starter dictionary used when the user has not defined their own.</summary>
    public static readonly IReadOnlyList<KeyValuePair<string, string>> DefaultCodeDictionary =
    [
        new("arrow function", "=>"),
        new("triple equals", "==="),
        new("not equals", "!="),
        new("open brace", "{"),
        new("close brace", "}"),
        new("pipe operator", "|>"),
        new("async await", "async/await"),
    ];

    /// <summary>
    /// Applies replacements longest-spoken-form-first (so more specific phrases
    /// win), case-insensitively. A spoken form that starts/ends with a letter or
    /// digit only matches on a word boundary, so a "cat" entry can never corrupt
    /// "concatenate" — replacements rewrite spoken words, not arbitrary substrings.
    /// </summary>
    public static string Apply(string transcript, IReadOnlyList<KeyValuePair<string, string>> replacements)
    {
        if (string.IsNullOrEmpty(transcript) || replacements.Count == 0)
        {
            return transcript;
        }

        string result = transcript;
        foreach ((string spoken, string written) in replacements.OrderByDescending(r => r.Key.Length))
        {
            if (spoken.Length == 0)
            {
                continue;
            }

            // The match evaluator returns the written form literally: '$' or
            // other regex group syntax in a replacement must stay verbatim text.
            result = Regex.Replace(
                result,
                BuildSpokenFormPattern(spoken),
                _ => written,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    /// <summary>
    /// Escapes the spoken form and anchors its letter/digit edges on word
    /// boundaries (Unicode-aware, so "élève" entries behave like ASCII ones).
    /// </summary>
    private static string BuildSpokenFormPattern(string spoken)
    {
        string escaped = Regex.Escape(spoken);
        string prefix = char.IsLetterOrDigit(spoken[0]) ? @"(?<![\p{L}\p{Nd}])" : string.Empty;
        string suffix = char.IsLetterOrDigit(spoken[^1]) ? @"(?![\p{L}\p{Nd}])" : string.Empty;
        return prefix + escaped + suffix;
    }

    /// <summary>
    /// Parses the user's multiline dictionary ("term =&gt; replacement" or
    /// "term = replacement", '#' comments) on top of the starter dictionary.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> Parse(string? customDictionary)
    {
        if (string.IsNullOrWhiteSpace(customDictionary))
        {
            return DefaultCodeDictionary;
        }

        var replacements = new List<KeyValuePair<string, string>>(DefaultCodeDictionary);
        foreach (string rawLine in customDictionary.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            int separator = line.IndexOf("=>", StringComparison.Ordinal);
            int separatorLength = 2;
            if (separator < 0)
            {
                separator = line.IndexOf('=', StringComparison.Ordinal);
                separatorLength = 1;
            }

            if (separator <= 0 || separator + separatorLength >= line.Length)
            {
                continue;
            }

            string spoken = line[..separator].Trim();
            string replacement = line[(separator + separatorLength)..].Trim();
            if (spoken.Length > 0 && replacement.Length > 0)
            {
                replacements.Add(new KeyValuePair<string, string>(spoken, replacement));
            }
        }

        return replacements;
    }
}
