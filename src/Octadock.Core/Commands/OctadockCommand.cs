using System.Globalization;
using Octadock.Core.Geometry;

namespace Octadock.Core.Commands;

/// <summary>
/// A parsed, transport-agnostic automation request. Produced by the command
/// parser from either a <c>octadock://</c> URL or CLI arguments and consumed by
/// the command dispatcher. Parameter keys are normalized to lower-case; typed
/// accessors expose the well-known parameters described in the automation spec.
/// </summary>
public sealed record OctadockCommand
{
    /// <summary>The verb.</summary>
    public required CommandType Type { get; init; }

    /// <summary>Raw, normalized (lower-case key) parameter bag.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a command with the given parameters.</summary>
    public static OctadockCommand Create(CommandType type, IReadOnlyDictionary<string, string>? parameters = null)
        => new()
        {
            Type = type,
            Parameters = parameters is null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase),
        };

    // ---- Raw accessors -------------------------------------------------

    public string? Get(string key) => Parameters.TryGetValue(key, out string? v) ? v : null;

    public bool Has(string key) => Parameters.ContainsKey(key);

    public bool GetBool(string key, bool @default = false)
    {
        string? v = Get(key);
        if (v is null)
        {
            return @default;
        }

        // Accept true/false, 1/0, yes/no, on/off.
        return v.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => @default,
        };
    }

    public int? GetInt(string key)
        => int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : null;

    public T? GetEnum<T>(string key) where T : struct, Enum
        => Enum.TryParse(Get(key), ignoreCase: true, out T v) && Enum.IsDefined(v) ? v : null;

    // ---- Typed convenience accessors ----------------------------------

    /// <summary>The requested post-capture action, defaulting to <see cref="PostCaptureAction.Shelf"/>.</summary>
    public PostCaptureAction Action => GetEnum<PostCaptureAction>("action") ?? PostCaptureAction.Shelf;

    /// <summary>Coordinate unit system for any supplied region.</summary>
    public CoordinateUnits Units => GetEnum<CoordinateUnits>("units")
        ?? (string.Equals(Get("units"), "dip", StringComparison.OrdinalIgnoreCase) ? CoordinateUnits.Dip : CoordinateUnits.Pixels);

    /// <summary>Target monitor token (index or stored monitor id), if supplied.</summary>
    public string? Monitor => Get("monitor");

    /// <summary>The raw <c>mode</c> parameter, when it names a capture mode (kept for protocol compatibility).</summary>
    public CaptureMode? Mode => GetEnum<CaptureMode>("mode");

    /// <summary>Silent flag: avoid modal UI where possible.</summary>
    public bool Silent => GetBool("silent");

    /// <summary>Optional destination filename.</summary>
    public string? Filename => Get("filename");

    /// <summary>Optional named capture preset.</summary>
    public string? Preset => Get("preset");

    /// <summary>Optional file path (ocr, read-aloud).</summary>
    public string? FilePath => Get("filepath");

    /// <summary>Optional working directory supplied with a command.</summary>
    public string? WorkingDirectory => Get("cwd");

    /// <summary>Optional display title supplied with a command.</summary>
    public string? Title => Get("title");

    /// <summary>
    /// The region rectangle when all four of x/y/width/height are present.
    /// Values are interpreted in whatever <see cref="Units"/> requests; callers
    /// that need physical pixels for DIP input must convert against a monitor.
    /// Returns <c>null</c> when the region is not fully specified.
    /// </summary>
    public PixelRect? Region
    {
        get
        {
            int? x = GetInt("x");
            int? y = GetInt("y");
            int? w = GetInt("width");
            int? h = GetInt("height");
            if (x is null || y is null || w is null || h is null)
            {
                return null;
            }

            return new PixelRect(x.Value, y.Value, w.Value, h.Value);
        }
    }

    public override string ToString()
    {
        string token = Type == CommandType.Unknown ? "unknown" : CommandTokens.ToToken(Type);
        return Parameters.Count == 0
            ? token
            : $"{token} [{string.Join(", ", Parameters.Select(kv => $"{kv.Key}={kv.Value}"))}]";
    }
}
