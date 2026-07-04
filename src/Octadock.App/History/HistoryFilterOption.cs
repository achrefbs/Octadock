using Octadock.Core.Models;

namespace Octadock.App.History;

/// <summary>
/// A selectable type filter in the history window. Maps a friendly label to the
/// capture types it includes (or a special "annotated"/"pinned" pseudo-filter).
/// </summary>
public sealed record HistoryFilterOption(string Label, HistoryFilterKind Kind, IReadOnlyList<CaptureType>? Types = null)
{
    /// <summary>The default set of filter chips shown in the toolbar.</summary>
    public static IReadOnlyList<HistoryFilterOption> All { get; } =
    [
        new("All", HistoryFilterKind.All),
        new("Screenshots", HistoryFilterKind.Types, [CaptureType.Area, CaptureType.Window, CaptureType.Fullscreen]),
        new("Scrolling", HistoryFilterKind.Types, [CaptureType.Scrolling]),
        new("Recordings", HistoryFilterKind.Types, [CaptureType.Recording]),
        new("OCR", HistoryFilterKind.Types, [CaptureType.OcrSource]),
        new("Files", HistoryFilterKind.Types, [CaptureType.External]),
        new("Annotated", HistoryFilterKind.Annotated),
        new("Pinned", HistoryFilterKind.Pinned),
    ];

    public override string ToString() => Label;
}

/// <summary>The kind of history filter a <see cref="HistoryFilterOption"/> represents.</summary>
public enum HistoryFilterKind
{
    /// <summary>No type restriction.</summary>
    All = 0,

    /// <summary>Restrict to a set of capture types.</summary>
    Types,

    /// <summary>Only captures that have an annotation project.</summary>
    Annotated,

    /// <summary>Only captures that have (or had) a pin.</summary>
    Pinned,
}
