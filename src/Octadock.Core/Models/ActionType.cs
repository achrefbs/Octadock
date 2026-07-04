namespace Octadock.Core.Models;

/// <summary>An action taken against a capture, recorded in the <c>actions</c> table for history/analytics.</summary>
public enum ActionType
{
    Shelved = 0,
    Copied,
    Saved,
    SavedAs,
    Annotated,
    Pinned,
    Discarded,
    Uploaded,
    OcrExtracted,
    Restored,
    Deleted,
    Exported,
    DraggedOut,
}
