using Octadock.Core.Geometry;

namespace Octadock.Core.Annotations;

/// <summary>
/// The in-memory model of an annotation editing session: the base raster plus
/// the ordered set of vector objects. This is what the editor mutates and what
/// the project serializer reads and writes. It is a plain data model with no
/// rendering dependencies.
/// </summary>
public sealed class AnnotationDocument
{
    private readonly List<AnnotationObject> _objects = [];

    public AnnotationDocument(PixelSize canvasSize, Guid? sourceCaptureId = null)
    {
        CanvasSize = canvasSize;
        SourceCaptureId = sourceCaptureId;
    }

    /// <summary>Canvas dimensions in image pixels (usually the base capture size).</summary>
    public PixelSize CanvasSize { get; private set; }

    /// <summary>The originating capture, when the document came from history.</summary>
    public Guid? SourceCaptureId { get; }

    /// <summary>The vector objects in stacking order (lowest z-index first).</summary>
    public IReadOnlyList<AnnotationObject> Objects => _objects;

    public void Resize(PixelSize newSize) => CanvasSize = newSize;

    public void Add(AnnotationObject obj)
    {
        _objects.Add(obj);
        Sort();
    }

    public bool Remove(Guid id)
    {
        int index = _objects.FindIndex(o => o.Id == id);
        if (index < 0)
        {
            return false;
        }

        _objects.RemoveAt(index);
        return true;
    }

    public bool Replace(AnnotationObject updated)
    {
        int index = _objects.FindIndex(o => o.Id == updated.Id);
        if (index < 0)
        {
            return false;
        }

        _objects[index] = updated;
        Sort();
        return true;
    }

    public void ReplaceAll(IEnumerable<AnnotationObject> objects)
    {
        _objects.Clear();
        _objects.AddRange(objects);
        Sort();
    }

    /// <summary>The next z-index to place an object on top of the stack.</summary>
    public int NextZIndex() => _objects.Count == 0 ? 0 : _objects.Max(o => o.ZIndex) + 1;

    // Stable ordering by z-index: List.Sort is an unstable introsort, so objects
    // that share a z-index (possible in a loaded objects.json) would swap render
    // order nondeterministically between edits. OrderBy is a stable sort.
    private void Sort()
    {
        AnnotationObject[] ordered = _objects.OrderBy(o => o.ZIndex).ToArray();
        _objects.Clear();
        _objects.AddRange(ordered);
    }
}
