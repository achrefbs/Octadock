using System.Globalization;
using System.Text;
using Octadock.Core.Abstractions;
using Octadock.Core.Naming;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="IFilenameGenerator"/>. Expands the filename template tokens
/// (<c>{yyyy}</c>, <c>{MM}</c>, <c>{dd}</c>, <c>{HH}</c>, <c>{mm}</c>, <c>{ss}</c>,
/// <c>{process}</c>, <c>{window}</c>, <c>{type}</c>, <c>{counter}</c>) into a
/// filesystem-safe base name (no extension) and sanitizes illegal Windows
/// filename characters and reserved device names.
/// </summary>
public sealed class FilenameGenerator : IFilenameGenerator
{
    /// <summary>Maximum length of a generated base name (leaves headroom for extension and path).</summary>
    public const int MaxBaseNameLength = 150;

    private const char Replacement = '_';

    // Reserved Windows device names (case-insensitive, with or without extension).
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    // Union of Path.GetInvalidFileNameChars() and the explicit Windows set so the
    // result is safe even when generated on a non-Windows host.
    private static readonly char[] IllegalChars = BuildIllegalChars();

    /// <inheritdoc />
    public string Generate(string template, FilenameContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(template))
        {
            template = "Screenshot {yyyy}-{MM}-{dd} at {HH}.{mm}.{ss}";
        }

        DateTimeOffset t = context.Timestamp;
        var sb = new StringBuilder(template.Length + 16);
        int i = 0;
        while (i < template.Length)
        {
            char c = template[i];
            if (c == '{')
            {
                int end = template.IndexOf('}', i + 1);
                if (end > i)
                {
                    string token = template.Substring(i + 1, end - i - 1);
                    sb.Append(ExpandToken(token, t, context));
                    i = end + 1;
                    continue;
                }
            }

            sb.Append(c);
            i++;
        }

        return Sanitize(sb.ToString());
    }

    /// <inheritdoc />
    public string Sanitize(string candidate)
    {
        if (string.IsNullOrEmpty(candidate))
        {
            return "capture";
        }

        var sb = new StringBuilder(candidate.Length);
        char previous = '\0';
        foreach (char c in candidate)
        {
            char safe = Array.IndexOf(IllegalChars, c) >= 0 || char.IsControl(c)
                ? Replacement
                : c;

            // Collapse runs of the replacement character.
            if (safe == Replacement && previous == Replacement)
            {
                continue;
            }

            sb.Append(safe);
            previous = safe;
        }

        // Trim whitespace, dots and replacement chars from the ends (Windows also
        // forbids trailing dots/spaces).
        string result = sb.ToString().Trim().Trim('.', ' ', Replacement).Trim();
        if (result.Length == 0)
        {
            return "capture";
        }

        // Truncate first, because trimming a truncated name can re-expose a
        // reserved device stem (e.g. "CON___…" -> "CON"); guard reserved names
        // only after the final length is fixed.
        if (result.Length > MaxBaseNameLength)
        {
            result = TruncateToRuneBoundary(result).TrimEnd('.', ' ', Replacement);
            if (result.Length == 0)
            {
                result = "capture";
            }
        }

        // Guard reserved device names by checking the stem before the first dot.
        int dot = result.IndexOf('.');
        string stem = dot < 0 ? result : result[..dot];
        if (ReservedNames.Contains(stem))
        {
            result = "_" + result;
        }

        return result;
    }

    private static string ExpandToken(string token, DateTimeOffset t, FilenameContext context) => token switch
    {
        "yyyy" => t.ToString("yyyy", CultureInfo.InvariantCulture),
        "MM" => t.ToString("MM", CultureInfo.InvariantCulture),
        "dd" => t.ToString("dd", CultureInfo.InvariantCulture),
        "HH" => t.ToString("HH", CultureInfo.InvariantCulture),
        "mm" => t.ToString("mm", CultureInfo.InvariantCulture),
        "ss" => t.ToString("ss", CultureInfo.InvariantCulture),
        "process" => CleanSegment(StripProcessExtension(context.ProcessName)),
        "window" => CleanSegment(context.WindowTitle),
        "type" => context.Type.ToString().ToLowerInvariant(),
        "counter" => context.Counter.ToString(CultureInfo.InvariantCulture),

        // Unknown token: leave verbatim (including the braces) so the author sees it.
        _ => "{" + token + "}",
    };

    private static string StripProcessExtension(string? process)
    {
        if (string.IsNullOrWhiteSpace(process))
        {
            return string.Empty;
        }

        return process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? process[..^4]
            : process;
    }

    private static string CleanSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Replace illegal chars inside individual token values immediately; the
        // final Sanitize pass collapses/trims runs across the whole name.
        var sb = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            sb.Append(Array.IndexOf(IllegalChars, c) >= 0 || char.IsControl(c) ? Replacement : c);
        }

        return sb.ToString();
    }

    private static string TruncateToRuneBoundary(string value)
    {
        if (value.Length <= MaxBaseNameLength)
        {
            return value;
        }

        var sb = new StringBuilder(MaxBaseNameLength);
        foreach (Rune rune in value.EnumerateRunes())
        {
            if (sb.Length + rune.Utf16SequenceLength > MaxBaseNameLength)
            {
                break;
            }

            sb.Append(rune.ToString());
        }

        return sb.ToString();
    }

    private static char[] BuildIllegalChars()
    {
        var set = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (char c in "<>:\"/\\|?*")
        {
            set.Add(c);
        }

        return set.ToArray();
    }
}
