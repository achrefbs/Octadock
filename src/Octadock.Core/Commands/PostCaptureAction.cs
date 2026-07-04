namespace Octadock.Core.Commands;

/// <summary>
/// What Octadock does with a capture once it exists. Mirrors the <c>action</c>
/// parameter in the automation spec. <see cref="Shelf"/> is the default.
/// </summary>
public enum PostCaptureAction
{
    /// <summary>Show the capture on the Capture Shelf (default).</summary>
    Shelf = 0,
    Copy,
    Save,
    Annotate,
    Upload,
    Pin,
    Discard,
}
