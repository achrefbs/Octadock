using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Octadock.Core.TextTools;

/// <summary>
/// Pure text transforms for the developer toolbox: JSON formatting, encodings,
/// JWT inspection, identifier casing, hashes, timestamp conversion, and line
/// utilities. All transforms are local, deterministic, and never throw for bad
/// input; failures surface through <see cref="TextTransformResult.Error"/>.
/// </summary>
public static class TextTransforms
{
    private static readonly JsonSerializerOptions PrettyJson = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    /// <summary>Every transform with UI-facing metadata, in display order.</summary>
    public static IReadOnlyList<TextTransformDescriptor> Catalog { get; } =
    [
        new(TextTransformKind.JsonPretty, "JSON pretty-print", "Format", "Indent JSON for reading."),
        new(TextTransformKind.JsonMinify, "JSON minify", "Format", "Collapse JSON to one line."),
        new(TextTransformKind.JwtDecode, "JWT decode", "Decode", "Show a JWT's header and payload. The signature is not verified."),
        new(TextTransformKind.Base64Encode, "Base64 encode", "Encode", "Encode UTF-8 text as Base64."),
        new(TextTransformKind.Base64Decode, "Base64 decode", "Decode", "Decode standard or URL-safe Base64."),
        new(TextTransformKind.UrlEncode, "URL encode", "Encode", "Percent-encode for a URL component."),
        new(TextTransformKind.UrlDecode, "URL decode", "Decode", "Decode percent-encoding."),
        new(TextTransformKind.HtmlEncode, "HTML encode", "Encode", "Escape &, <, >, and quotes."),
        new(TextTransformKind.HtmlDecode, "HTML decode", "Decode", "Decode HTML entities."),
        new(TextTransformKind.UpperCase, "UPPERCASE", "Case", "Uppercase everything."),
        new(TextTransformKind.LowerCase, "lowercase", "Case", "Lowercase everything."),
        new(TextTransformKind.TitleCase, "Title Case", "Case", "Capitalize each word."),
        new(TextTransformKind.CamelCase, "camelCase", "Case", "Join words as a camelCase identifier."),
        new(TextTransformKind.PascalCase, "PascalCase", "Case", "Join words as a PascalCase identifier."),
        new(TextTransformKind.SnakeCase, "snake_case", "Case", "Join words with underscores."),
        new(TextTransformKind.KebabCase, "kebab-case", "Case", "Join words with hyphens."),
        new(TextTransformKind.ConstantCase, "CONSTANT_CASE", "Case", "Uppercase words joined with underscores."),
        new(TextTransformKind.HashMd5, "MD5", "Hash", "Hex digest of the UTF-8 bytes. Checksum use only."),
        new(TextTransformKind.HashSha1, "SHA-1", "Hash", "Hex digest of the UTF-8 bytes. Checksum use only."),
        new(TextTransformKind.HashSha256, "SHA-256", "Hash", "Hex digest of the UTF-8 bytes."),
        new(TextTransformKind.HashSha512, "SHA-512", "Hash", "Hex digest of the UTF-8 bytes."),
        new(TextTransformKind.UnixToDate, "Unix time → date", "Time", "Read a Unix timestamp in seconds or milliseconds."),
        new(TextTransformKind.DateToUnix, "Date → Unix time", "Time", "Convert an ISO-8601 date to Unix seconds/milliseconds."),
        new(TextTransformKind.TrimLines, "Trim lines", "Lines", "Strip trailing spaces and surrounding blank lines."),
        new(TextTransformKind.SortLines, "Sort lines", "Lines", "Sort lines alphabetically."),
        new(TextTransformKind.DedupeLines, "Dedupe lines", "Lines", "Remove duplicate lines, keeping the first."),
        new(TextTransformKind.ReverseLines, "Reverse lines", "Lines", "Reverse line order."),
        new(TextTransformKind.CountStats, "Count", "Lines", "Characters, words, and lines."),
    ];

    /// <summary>Applies <paramref name="kind"/> to <paramref name="input"/>.</summary>
    public static TextTransformResult Apply(TextTransformKind kind, string input)
    {
        input ??= string.Empty;
        return kind switch
        {
            TextTransformKind.JsonPretty => FormatJson(input, PrettyJson),
            TextTransformKind.JsonMinify => FormatJson(input, CompactJson),
            TextTransformKind.Base64Encode => TextTransformResult.Ok(Convert.ToBase64String(Encoding.UTF8.GetBytes(input))),
            TextTransformKind.Base64Decode => Base64Decode(input),
            TextTransformKind.UrlEncode => TextTransformResult.Ok(Uri.EscapeDataString(input)),
            TextTransformKind.UrlDecode => UrlDecode(input),
            TextTransformKind.HtmlEncode => TextTransformResult.Ok(WebUtility.HtmlEncode(input)),
            TextTransformKind.HtmlDecode => TextTransformResult.Ok(WebUtility.HtmlDecode(input)),
            TextTransformKind.JwtDecode => JwtDecode(input),
            TextTransformKind.UpperCase => TextTransformResult.Ok(input.ToUpperInvariant()),
            TextTransformKind.LowerCase => TextTransformResult.Ok(input.ToLowerInvariant()),
            TextTransformKind.TitleCase => TitleCase(input),
            TextTransformKind.CamelCase => Recase(input, CaseStyle.Camel),
            TextTransformKind.PascalCase => Recase(input, CaseStyle.Pascal),
            TextTransformKind.SnakeCase => Recase(input, CaseStyle.Snake),
            TextTransformKind.KebabCase => Recase(input, CaseStyle.Kebab),
            TextTransformKind.ConstantCase => Recase(input, CaseStyle.Constant),
            TextTransformKind.HashMd5 => Hash(input, HashKind.Md5),
            TextTransformKind.HashSha1 => Hash(input, HashKind.Sha1),
            TextTransformKind.HashSha256 => Hash(input, HashKind.Sha256),
            TextTransformKind.HashSha512 => Hash(input, HashKind.Sha512),
            TextTransformKind.UnixToDate => UnixToDate(input),
            TextTransformKind.DateToUnix => DateToUnix(input),
            TextTransformKind.TrimLines => TrimLines(input),
            TextTransformKind.SortLines => LineOp(input, static lines => lines.Order(StringComparer.OrdinalIgnoreCase).ToArray()),
            TextTransformKind.DedupeLines => LineOp(input, static lines => lines.Distinct(StringComparer.Ordinal).ToArray()),
            TextTransformKind.ReverseLines => LineOp(input, static lines => lines.Reverse().ToArray()),
            TextTransformKind.CountStats => CountStats(input),
            _ => TextTransformResult.Fail("Unknown transform."),
        };
    }

    // ---- JSON ------------------------------------------------------------

    private static TextTransformResult FormatJson(string input, JsonSerializerOptions options)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return TextTransformResult.Fail("Enter JSON to format.");
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(input, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            return TextTransformResult.Ok(JsonSerializer.Serialize(doc.RootElement, options));
        }
        catch (JsonException ex)
        {
            return TextTransformResult.Fail(DescribeJsonError(ex));
        }
    }

    private static string DescribeJsonError(JsonException ex)
    {
        if (ex.LineNumber is long line)
        {
            long column = (ex.BytePositionInLine ?? 0) + 1;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Invalid JSON at line {line + 1}, column {column}.");
        }

        return "Invalid JSON.";
    }

    // ---- Encodings ---------------------------------------------------------

    private static TextTransformResult Base64Decode(string input)
    {
        string compact = input.Trim();
        if (compact.Length == 0)
        {
            return TextTransformResult.Fail("Enter Base64 text to decode.");
        }

        byte[]? bytes = TryDecodeBase64(compact);
        if (bytes is null)
        {
            return TextTransformResult.Fail("That is not valid Base64.");
        }

        try
        {
            return TextTransformResult.Ok(new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes));
        }
        catch (DecoderFallbackException)
        {
            // Valid Base64 but not UTF-8 text: show a hex dump instead of mojibake.
            return TextTransformResult.Ok(FormatHexDump(bytes));
        }
    }

    private static byte[]? TryDecodeBase64(string value)
    {
        // Accept the URL-safe alphabet and missing padding.
        string normalized = value.Replace('-', '+').Replace('_', '/');
        int remainder = normalized.Length % 4;
        if (remainder is 2 or 3)
        {
            normalized += new string('=', 4 - remainder);
        }

        Span<byte> buffer = normalized.Length <= 4096 ? stackalloc byte[normalized.Length] : new byte[normalized.Length];
        return Convert.TryFromBase64String(normalized, buffer, out int written)
            ? buffer[..written].ToArray()
            : null;
    }

    private static string FormatHexDump(byte[] bytes)
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"{bytes.Length} bytes (not UTF-8 text):");
        for (int i = 0; i < bytes.Length; i += 16)
        {
            sb.AppendLine();
            int count = Math.Min(16, bytes.Length - i);
            sb.Append(LowerHex(bytes.AsSpan(i, count), spaced: true));
        }

        return sb.ToString();
    }

    private static TextTransformResult UrlDecode(string input)
    {
        try
        {
            return TextTransformResult.Ok(Uri.UnescapeDataString(input.Replace('+', ' ')));
        }
        catch (FormatException)
        {
            return TextTransformResult.Fail("That is not valid percent-encoding.");
        }
    }

    // ---- JWT ---------------------------------------------------------------

    private static TextTransformResult JwtDecode(string input)
    {
        string token = input.Trim();
        const string BearerPrefix = "Bearer ";
        if (token.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            token = token[BearerPrefix.Length..].Trim();
        }

        string[] parts = token.Split('.');
        if (parts.Length is not (2 or 3) || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return TextTransformResult.Fail("That does not look like a JWT (expected header.payload.signature).");
        }

        string? header = DecodeJwtSection(parts[0]);
        string? payload = DecodeJwtSection(parts[1]);
        if (header is null || payload is null)
        {
            return TextTransformResult.Fail("The JWT header or payload is not valid Base64URL JSON.");
        }

        var sb = new StringBuilder();
        sb.AppendLine("// Header");
        sb.AppendLine(header);
        sb.AppendLine();
        sb.AppendLine("// Payload");
        sb.AppendLine(payload);
        sb.AppendLine();
        sb.Append(parts.Length == 3 && parts[2].Length > 0
            ? "// Signature present — not verified."
            : "// No signature (unsecured JWT).");
        return TextTransformResult.Ok(sb.ToString());
    }

    private static string? DecodeJwtSection(string base64Url)
    {
        byte[]? bytes = TryDecodeBase64(base64Url);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            string json = Encoding.UTF8.GetString(bytes);
            using JsonDocument doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, PrettyJson);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
        }
    }

    // ---- Casing ------------------------------------------------------------

    private enum CaseStyle
    {
        Camel,
        Pascal,
        Snake,
        Kebab,
        Constant,
    }

    private static TextTransformResult TitleCase(string input)
    {
        TextInfo textInfo = CultureInfo.InvariantCulture.TextInfo;
        return TextTransformResult.Ok(textInfo.ToTitleCase(input.ToLowerInvariant()));
    }

    private static TextTransformResult Recase(string input, CaseStyle style)
    {
        IReadOnlyList<string> words = SplitWords(input);
        if (words.Count == 0)
        {
            return TextTransformResult.Fail("Enter text to convert.");
        }

        string output = style switch
        {
            CaseStyle.Camel => string.Concat(words.Select((w, i) => i == 0 ? w.ToLowerInvariant() : Capitalize(w))),
            CaseStyle.Pascal => string.Concat(words.Select(Capitalize)),
            CaseStyle.Snake => string.Join('_', words.Select(static w => w.ToLowerInvariant())),
            CaseStyle.Kebab => string.Join('-', words.Select(static w => w.ToLowerInvariant())),
            CaseStyle.Constant => string.Join('_', words.Select(static w => w.ToUpperInvariant())),
            _ => input,
        };
        return TextTransformResult.Ok(output);
    }

    private static string Capitalize(string word)
        => word.Length == 0 ? word : char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();

    /// <summary>
    /// Splits identifier-ish text into words on whitespace, punctuation, and
    /// camel boundaries. "XMLHttpRequest v2" → ["XML", "Http", "Request", "v2"].
    /// </summary>
    internal static IReadOnlyList<string> SplitWords(string input)
    {
        var words = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length > 0)
            {
                words.Add(current.ToString());
                current.Clear();
            }
        }

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (!char.IsLetterOrDigit(c))
            {
                Flush();
                continue;
            }

            if (current.Length > 0)
            {
                char prev = input[i - 1];
                bool lowerToUpper = char.IsUpper(c) && (char.IsLower(prev) || char.IsDigit(prev));
                bool acronymEnd = char.IsUpper(prev) && char.IsUpper(c)
                    && i + 1 < input.Length && char.IsLower(input[i + 1]);
                if (lowerToUpper || acronymEnd)
                {
                    Flush();
                }
            }

            current.Append(c);
        }

        Flush();
        return words;
    }

    // ---- Hashes ------------------------------------------------------------

    private enum HashKind
    {
        Md5,
        Sha1,
        Sha256,
        Sha512,
    }

    private static TextTransformResult Hash(string input, HashKind kind)
    {
        byte[] data = Encoding.UTF8.GetBytes(input);
#pragma warning disable CA5350, CA5351 // MD5/SHA-1 are offered as developer checksums, not for security.
        byte[] digest = kind switch
        {
            HashKind.Md5 => MD5.HashData(data),
            HashKind.Sha1 => SHA1.HashData(data),
            HashKind.Sha256 => SHA256.HashData(data),
            _ => SHA512.HashData(data),
        };
#pragma warning restore CA5350, CA5351
        return TextTransformResult.Ok(LowerHex(digest, spaced: false));
    }

    private static string LowerHex(ReadOnlySpan<byte> bytes, bool spaced)
    {
        var sb = new StringBuilder(bytes.Length * (spaced ? 3 : 2));
        for (int i = 0; i < bytes.Length; i++)
        {
            if (spaced && i > 0)
            {
                sb.Append(' ');
            }

            sb.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    // ---- Timestamps ----------------------------------------------------------

    private static TextTransformResult UnixToDate(string input)
    {
        string trimmed = input.Trim();
        if (!long.TryParse(trimmed, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long value))
        {
            return TextTransformResult.Fail("Enter a Unix timestamp (seconds or milliseconds).");
        }

        // Heuristic: |values| beyond the year ~33658 in seconds are milliseconds.
        bool isMilliseconds = Math.Abs(value) > 999_999_999_999L / 1000 * 1000 || Math.Abs(value) > 99_999_999_999L;
        try
        {
            DateTimeOffset utc = isMilliseconds
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
            string unit = isMilliseconds ? "milliseconds" : "seconds";
            string output = string.Create(
                CultureInfo.InvariantCulture,
                $"{utc.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.FFF'Z'} UTC\n{utc.ToLocalTime():yyyy-MM-dd HH:mm:ss.FFF zzz} local\n(read as Unix {unit})");
            return TextTransformResult.Ok(output);
        }
        catch (ArgumentOutOfRangeException)
        {
            return TextTransformResult.Fail("That timestamp is out of range.");
        }
    }

    private static TextTransformResult DateToUnix(string input)
    {
        string trimmed = input.Trim();
        if (trimmed.Length == 0)
        {
            return TextTransformResult.Fail("Enter a date, for example 2026-07-04T12:00:00Z.");
        }

        if (!DateTimeOffset.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces,
                out DateTimeOffset parsed))
        {
            return TextTransformResult.Fail("Could not parse that date. Try ISO-8601, for example 2026-07-04T12:00:00Z.");
        }

        string output = string.Create(
            CultureInfo.InvariantCulture,
            $"{parsed.ToUnixTimeSeconds()} seconds\n{parsed.ToUnixTimeMilliseconds()} milliseconds\n({parsed.UtcDateTime:yyyy-MM-dd'T'HH:mm:ss.FFF'Z'} UTC)");
        return TextTransformResult.Ok(output);
    }

    // ---- Line utilities --------------------------------------------------------

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    private static TextTransformResult TrimLines(string input)
    {
        string[] lines = input.Split(LineBreaks, StringSplitOptions.None);
        IEnumerable<string> trimmed = lines.Select(static l => l.TrimEnd());
        return TextTransformResult.Ok(string.Join(Environment.NewLine, trimmed).Trim('\r', '\n'));
    }

    private static TextTransformResult LineOp(string input, Func<string[], string[]> op)
    {
        string[] lines = input.Split(LineBreaks, StringSplitOptions.None);
        return TextTransformResult.Ok(string.Join(Environment.NewLine, op(lines)));
    }

    private static TextTransformResult CountStats(string input)
    {
        int chars = input.Length;
        int lines = input.Length == 0 ? 0 : input.Split(LineBreaks, StringSplitOptions.None).Length;
        int words = input
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Length;
        string output = string.Create(
            CultureInfo.InvariantCulture,
            $"{chars} characters\n{words} words\n{lines} lines");
        return TextTransformResult.Ok(output);
    }
}

/// <summary>UI-facing metadata for one transform in the toolbox catalog.</summary>
/// <param name="Kind">The transform this row describes.</param>
/// <param name="Name">Short display name.</param>
/// <param name="Category">Grouping label: Format, Encode, Decode, Case, Hash, Time, or Lines.</param>
/// <param name="Description">One-line description shown as hint text.</param>
public sealed record TextTransformDescriptor(
    TextTransformKind Kind,
    string Name,
    string Category,
    string Description);
