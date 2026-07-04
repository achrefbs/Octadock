using Octadock.Core.Annotations;

namespace Octadock.App.Editing;

/// <summary>
/// The window-level operations the <see cref="EditorViewModel"/> delegates to.
/// Kept as a small callback surface so the view model stays free of WPF window /
/// dialog / clipboard dependencies (which live in the code-behind) while still
/// owning the commands.
/// </summary>
public abstract class EditorHost
{
    /// <summary>Copies the flattened image to the clipboard.</summary>
    public abstract Task CopyAsync();

    /// <summary>
    /// Saves the editable <c>.octadock</c> project (to its existing path, or prompts).
    /// Returns <see langword="true"/> only when the project was actually written;
    /// <see langword="false"/> when the user cancelled the save dialog or the write failed,
    /// so callers can keep the dirty flag set.
    /// </summary>
    public abstract Task<bool> SaveAsync();

    /// <summary>
    /// Prompts for a new project path and saves. Returns <see langword="true"/> only on a
    /// successful write; <see langword="false"/> when cancelled or on failure.
    /// </summary>
    public abstract Task<bool> SaveAsAsync();

    /// <summary>Prompts for a raster path and exports the flattened PNG/JPEG.</summary>
    public abstract Task ExportAsync();

    /// <summary>Starts inline editing of a text object just created on the canvas.</summary>
    public abstract void BeginTextEditing(AnnotationObject textObject);
}
