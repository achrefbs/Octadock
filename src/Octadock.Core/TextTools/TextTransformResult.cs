namespace Octadock.Core.TextTools;

/// <summary>
/// The outcome of applying a text transform. Failed transforms carry a short,
/// user-presentable error instead of throwing, so UI/CLI callers can surface
/// bad input (invalid JSON, malformed Base64, ...) without exception plumbing.
/// </summary>
public sealed record TextTransformResult
{
    private TextTransformResult(bool success, string output, string error)
    {
        Success = success;
        Output = output;
        Error = error;
    }

    /// <summary>Whether the transform produced usable output.</summary>
    public bool Success { get; }

    /// <summary>The transformed text when <see cref="Success"/> is true.</summary>
    public string Output { get; }

    /// <summary>A short user-presentable message when <see cref="Success"/> is false.</summary>
    public string Error { get; }

    /// <summary>Creates a successful result.</summary>
    public static TextTransformResult Ok(string output) => new(true, output ?? string.Empty, string.Empty);

    /// <summary>Creates a failed result with a user-presentable message.</summary>
    public static TextTransformResult Fail(string error) => new(false, string.Empty, error ?? "The transform failed.");
}
