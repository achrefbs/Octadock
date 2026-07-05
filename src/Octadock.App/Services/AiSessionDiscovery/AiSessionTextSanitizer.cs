using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// Normalization, truncation, and secret redaction for the local metadata the
/// discovery pipeline persists (command lines, paths, titles). Command lines
/// and cwd paths are sensitive local metadata: everything stored on a session
/// row must pass through here first.
/// </summary>
public static partial class AiSessionTextSanitizer
{
    public const int MaxCommandLength = 512;
    public const int MaxTitleLength = 96;
    public const int MaxMetadataValueLength = 256;
    public const int MaxMetadataEntries = 32;

    [GeneratedRegex(
        @"(?<prefix>(--?|/)[\w-]*(key|token|secret|password|passwd|credential|auth)[\w-]*(\s+|[=:]))(?<value>""[^""]*""|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretOptionRegex();

    [GeneratedRegex(
        @"\b(sk-[A-Za-z0-9_-]{8,}|ghp_[A-Za-z0-9]{8,}|github_pat_[A-Za-z0-9_]{8,}|xox[a-z]-[A-Za-z0-9-]{8,}|AKIA[0-9A-Z]{16}|Bearer\s+[A-Za-z0-9._~+/=-]{8,})",
        RegexOptions.CultureInvariant)]
    private static partial Regex SecretTokenRegex();

    /// <summary>Trims and null-collapses whitespace-only text.</summary>
    public static string? Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>Removes the \\?\ prefix and trailing separators from a Windows path.</summary>
    public static string? NormalizePath(string? path)
    {
        string? clean = Clean(path);
        if (clean is null)
        {
            return null;
        }

        if (clean.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            clean = clean[4..];
        }

        clean = clean.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return clean.Length == 0 ? null : clean;
    }

    /// <summary>Normalized path or empty string, for use as a dictionary key.</summary>
    public static string WorkspaceKey(string? path)
        => NormalizePath(path) ?? string.Empty;

    /// <summary>Last folder name of a path for display, or empty.</summary>
    public static string WorkspaceLabel(string? path)
    {
        string? normalized = NormalizePath(path);
        if (normalized is null)
        {
            return string.Empty;
        }

        try
        {
            return Path.GetFileName(normalized);
        }
        catch (ArgumentException)
        {
            return normalized;
        }
    }

    /// <summary>
    /// Collapses a path to the loose token Claude Code uses for its per-project
    /// state folders (non-alphanumerics become '-'). Deterministic in the
    /// path→token direction, so two paths can be compared through it even when
    /// the token itself cannot be decoded unambiguously.
    /// </summary>
    public static string ClaudeProjectToken(string? path)
    {
        string? normalized = NormalizePath(path) ?? Clean(path);
        if (normalized is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            builder.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : '-');
        }

        return builder.ToString();
    }

    /// <summary>
    /// Best-effort decode of a Claude project folder name ("C--Users-me-repo")
    /// back to a display path. Ambiguous when real folders contain '-', so use
    /// only for display, never for identity.
    /// </summary>
    public static string? TryDecodeClaudeProjectDirectory(string? folderName)
    {
        string? clean = Clean(folderName);
        if (clean is null || clean.Length < 4)
        {
            return null;
        }

        // Drive-letter form: "C--Users-me-repo" => "C:\Users\me\repo".
        if (char.IsLetter(clean[0]) && clean[1] == '-' && clean[2] == '-')
        {
            return $"{char.ToUpperInvariant(clean[0])}:\\{clean[3..].Replace('-', '\\')}";
        }

        return null;
    }

    /// <summary>Masks secret-looking option values and bare tokens in free text.</summary>
    public static string? RedactSecrets(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string redacted = SecretOptionRegex().Replace(text, "${prefix}***");
        return SecretTokenRegex().Replace(redacted, "***");
    }

    /// <summary>Redacts then truncates a command line for storage.</summary>
    public static string? SanitizeCommand(string? commandLine)
        => Truncate(RedactSecrets(commandLine), MaxCommandLength);

    /// <summary>Truncates a title to the UI-safe length.</summary>
    public static string SanitizeTitle(string? title, string fallback)
    {
        string resolved = Clean(title) ?? fallback;
        return Truncate(resolved, MaxTitleLength)!;
    }

    /// <summary>Truncates text, appending an ellipsis when clipped.</summary>
    public static string? Truncate(string? text, int maxLength)
    {
        if (text is null)
        {
            return null;
        }

        return text.Length <= maxLength
            ? text
            : text[..Math.Max(0, maxLength - 3)] + "...";
    }

    /// <summary>
    /// Reads the value following a named option in a raw command line. The
    /// option must sit on token boundaries: "--working-dir" never matches
    /// inside "--working-directory" or inside another token.
    /// </summary>
    public static string? ReadCommandOption(string? commandLine, string option)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        var searchFrom = 0;
        while (searchFrom < commandLine.Length)
        {
            int index = commandLine.IndexOf(option, searchFrom, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return null;
            }

            int after = index + option.Length;
            bool boundaryBefore = index == 0 || char.IsWhiteSpace(commandLine[index - 1]);
            bool boundaryAfter = after >= commandLine.Length ||
                char.IsWhiteSpace(commandLine[after]) ||
                commandLine[after] is '=' or ':';
            if (!boundaryBefore || !boundaryAfter)
            {
                searchFrom = index + 1;
                continue;
            }

            return ReadOptionValue(commandLine, after);
        }

        return null;
    }

    private static string? ReadOptionValue(string commandLine, int index)
    {
        if (index < commandLine.Length && (commandLine[index] == '=' || commandLine[index] == ':'))
        {
            index++;
        }

        while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index]))
        {
            index++;
        }

        if (index >= commandLine.Length)
        {
            return null;
        }

        if (commandLine[index] == '"')
        {
            int endQuote = commandLine.IndexOf('"', index + 1);
            return endQuote > index ? Clean(commandLine[(index + 1)..endQuote]) : null;
        }

        int end = index;
        while (end < commandLine.Length && !char.IsWhiteSpace(commandLine[end]))
        {
            end++;
        }

        return Clean(commandLine[index..end]);
    }

    /// <summary>Process name without a trailing ".exe".</summary>
    public static string NormalizeProcessName(string? processName)
    {
        string clean = Clean(processName) ?? string.Empty;
        return clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? clean[..^4]
            : clean;
    }
}
