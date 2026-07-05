namespace Octadock.Core.TextTools;

/// <summary>
/// The built-in text transforms offered by the developer toolbox. Values are
/// persisted in history/automation surfaces by name, so renames require a
/// migration.
/// </summary>
public enum TextTransformKind
{
    /// <summary>Pretty-print JSON with two-space indentation.</summary>
    JsonPretty,

    /// <summary>Minify JSON to a single line.</summary>
    JsonMinify,

    /// <summary>Encode UTF-8 text as Base64.</summary>
    Base64Encode,

    /// <summary>Decode Base64 (standard or URL-safe alphabet) to UTF-8 text.</summary>
    Base64Decode,

    /// <summary>Percent-encode text for use in a URL component.</summary>
    UrlEncode,

    /// <summary>Decode percent-encoded text.</summary>
    UrlDecode,

    /// <summary>HTML-encode reserved characters.</summary>
    HtmlEncode,

    /// <summary>Decode HTML entities.</summary>
    HtmlDecode,

    /// <summary>Decode a JWT's header and payload (no signature verification).</summary>
    JwtDecode,

    /// <summary>UPPERCASE.</summary>
    UpperCase,

    /// <summary>lowercase.</summary>
    LowerCase,

    /// <summary>Title Case Words.</summary>
    TitleCase,

    /// <summary>camelCase identifier.</summary>
    CamelCase,

    /// <summary>PascalCase identifier.</summary>
    PascalCase,

    /// <summary>snake_case identifier.</summary>
    SnakeCase,

    /// <summary>kebab-case identifier.</summary>
    KebabCase,

    /// <summary>CONSTANT_CASE identifier.</summary>
    ConstantCase,

    /// <summary>MD5 hex digest of the UTF-8 bytes (checksum use only).</summary>
    HashMd5,

    /// <summary>SHA-1 hex digest of the UTF-8 bytes (checksum use only).</summary>
    HashSha1,

    /// <summary>SHA-256 hex digest of the UTF-8 bytes.</summary>
    HashSha256,

    /// <summary>SHA-512 hex digest of the UTF-8 bytes.</summary>
    HashSha512,

    /// <summary>Convert a Unix timestamp (seconds or milliseconds) to ISO-8601 UTC and local time.</summary>
    UnixToDate,

    /// <summary>Convert an ISO-8601 (or common) date string to Unix seconds and milliseconds.</summary>
    DateToUnix,

    /// <summary>Trim trailing whitespace from every line and surrounding blank lines.</summary>
    TrimLines,

    /// <summary>Sort lines ordinally, case-insensitive first.</summary>
    SortLines,

    /// <summary>Remove duplicate lines, keeping the first occurrence.</summary>
    DedupeLines,

    /// <summary>Reverse the order of lines.</summary>
    ReverseLines,

    /// <summary>Count characters, words, and lines.</summary>
    CountStats,
}
