using Octadock.Core.Imaging;

namespace Octadock.App.Clipboard;

/// <summary>
/// One observed clipboard state: the primary payload plus provenance. Produced
/// by <see cref="IClipboardSnapshotSource"/> after private/sensitive content and
/// Octadock's own writes have already been filtered out.
/// </summary>
public sealed record ClipboardSnapshot
{
    /// <summary>Plain-text payload, or null when the clipboard held no usable text.</summary>
    public string? Text { get; init; }

    /// <summary>PNG-encoded image payload, or null.</summary>
    public EncodedImage? Image { get; init; }

    /// <summary>Foreground process image name at copy time (e.g. <c>chrome.exe</c>).</summary>
    public string? SourceProcess { get; init; }

    /// <summary>Foreground window title at copy time.</summary>
    public string? SourceWindow { get; init; }
}

/// <summary>
/// Reads the current clipboard into a <see cref="ClipboardSnapshot"/>, applying
/// the privacy rules: password-manager/private formats are never captured and
/// clipboard contents written by Octadock itself are ignored.
/// </summary>
public interface IClipboardSnapshotSource
{
    /// <summary>
    /// Returns the current clipboard content, or null when there is nothing to
    /// record (empty, excluded by a privacy format, an Octadock-origin write, or
    /// an image while <paramref name="includeImages"/> is false and no text exists).
    /// </summary>
    ClipboardSnapshot? TryRead(bool includeImages);
}
