using System.Windows.Media.Imaging;
using Octadock.Core.Annotations;
using Octadock.Core.Geometry;

namespace Octadock.App.Editing;

/// <summary>
/// A single reversible edit to the <see cref="AnnotationDocument"/>. The command
/// pattern keeps undo/redo trivial: each command knows how to apply and revert
/// itself against the document.
/// </summary>
internal interface IEditorCommand
{
    /// <summary>A short label describing the edit (for menus / accessibility).</summary>
    string Label { get; }

    void Apply(AnnotationDocument document);

    void Revert(AnnotationDocument document);
}

/// <summary>Adds a new annotation object to the document.</summary>
internal sealed class AddObjectCommand(AnnotationObject added) : IEditorCommand
{
    public string Label => "Add " + added.Type;

    public void Apply(AnnotationDocument document) => document.Add(added);

    public void Revert(AnnotationDocument document) => document.Remove(added.Id);
}

/// <summary>Removes an object from the document.</summary>
internal sealed class RemoveObjectCommand(AnnotationObject removed) : IEditorCommand
{
    public string Label => "Delete " + removed.Type;

    public void Apply(AnnotationDocument document) => document.Remove(removed.Id);

    public void Revert(AnnotationDocument document) => document.Add(removed);
}

/// <summary>
/// Removes every annotation object in one undoable step; reverting restores the
/// full set exactly as it was (ids and z-order preserved).
/// </summary>
internal sealed class ClearObjectsCommand(IReadOnlyList<AnnotationObject> objectsBefore) : IEditorCommand
{
    public string Label => "Clear all";

    public void Apply(AnnotationDocument document) => document.ReplaceAll([]);

    public void Revert(AnnotationDocument document) => document.ReplaceAll(objectsBefore);
}

/// <summary>
/// Replaces an object with an updated copy (move/resize/style/text edits). Stores
/// both the before and after snapshots so it is fully reversible.
/// </summary>
internal sealed class UpdateObjectCommand(AnnotationObject before, AnnotationObject after) : IEditorCommand
{
    public string Label => "Edit " + after.Type;

    public AnnotationObject Before => before;

    public AnnotationObject After => after;

    public void Apply(AnnotationDocument document) => document.Replace(after);

    public void Revert(AnnotationDocument document) => document.Replace(before);

    public bool CanMergeStyleChange(UpdateObjectCommand next)
    {
        return IsStyleOnly(before, after)
            && IsStyleOnly(next.Before, next.After)
            && after.Id == next.Before.Id
            && after == next.Before;
    }

    public UpdateObjectCommand MergeStyleChange(UpdateObjectCommand next)
        => new(before, next.After);

    private static bool IsStyleOnly(AnnotationObject before, AnnotationObject after)
        => before.Id == after.Id
        && before.Type == after.Type
        && before.Frame == after.Frame
        && before.Payload == after.Payload
        && before.Locked == after.Locked
        && before.ZIndex == after.ZIndex
        && before.Style != after.Style;
}

/// <summary>
/// Resizes the canvas (crop) and swaps in the cropped base raster; on revert it
/// restores the size, the objects and the original raster. The base-image swap is
/// applied through a callback because the base raster lives on the view model, not
/// the document.
/// </summary>
internal sealed class CropCommand : IEditorCommand
{
    private readonly PixelSize _before;
    private readonly PixelSize _after;
    private readonly IReadOnlyList<AnnotationObject> _objectsBefore;
    private readonly IReadOnlyList<AnnotationObject> _objectsAfter;
    private readonly BitmapSource _imageBefore;
    private readonly BitmapSource _imageAfter;
    private readonly Action<BitmapSource> _setImage;

    public CropCommand(
        PixelSize before,
        PixelSize after,
        IReadOnlyList<AnnotationObject> objectsBefore,
        IReadOnlyList<AnnotationObject> objectsAfter,
        BitmapSource imageBefore,
        BitmapSource imageAfter,
        Action<BitmapSource> setImage)
    {
        _before = before;
        _after = after;
        _objectsBefore = objectsBefore;
        _objectsAfter = objectsAfter;
        _imageBefore = imageBefore;
        _imageAfter = imageAfter;
        _setImage = setImage;
    }

    public string Label => "Crop";

    public void Apply(AnnotationDocument document)
    {
        document.Resize(_after);
        document.ReplaceAll(_objectsAfter);
        _setImage(_imageAfter);
    }

    public void Revert(AnnotationDocument document)
    {
        document.Resize(_before);
        document.ReplaceAll(_objectsBefore);
        _setImage(_imageBefore);
    }
}

/// <summary>
/// A bounded undo/redo stack over an <see cref="AnnotationDocument"/>. Pushing a
/// new command clears the redo stack, matching the standard editor model.
/// </summary>
internal sealed class EditorHistory
{
    private readonly AnnotationDocument _document;
    private readonly Stack<IEditorCommand> _undo = new();
    private readonly Stack<IEditorCommand> _redo = new();

    public EditorHistory(AnnotationDocument document) => _document = document;

    /// <summary>Raised whenever the undo/redo availability changes.</summary>
    public event EventHandler? Changed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>Applies a command and records it for undo.</summary>
    public void Do(IEditorCommand command)
    {
        command.Apply(_document);

        if (command is UpdateObjectCommand update
            && _undo.TryPeek(out IEditorCommand? previous)
            && previous is UpdateObjectCommand previousUpdate
            && previousUpdate.CanMergeStyleChange(update))
        {
            _undo.Pop();
            _undo.Push(previousUpdate.MergeStyleChange(update));
            _redo.Clear();
            Changed?.Invoke(this, EventArgs.Empty);
            return;
        }

        _undo.Push(command);
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Undo()
    {
        if (_undo.Count == 0)
        {
            return;
        }

        IEditorCommand command = _undo.Pop();
        command.Revert(_document);
        _redo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
        {
            return;
        }

        IEditorCommand command = _redo.Pop();
        command.Apply(_document);
        _undo.Push(command);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
