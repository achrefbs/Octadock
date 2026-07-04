namespace Octadock.Core.Annotations;

/// <summary>
/// One vector annotation drawn on top of the base capture. Objects are stored in
/// the project's <c>objects.json</c> and flattened into the raster on export.
/// </summary>
public sealed record AnnotationObject
{
    public required Guid Id { get; init; }

    public required AnnotationObjectType Type { get; init; }

    public AnnotationFrame Frame { get; init; } = AnnotationFrame.Empty;

    public AnnotationStyle Style { get; init; } = AnnotationStyle.Default;

    public AnnotationPayload Payload { get; init; } = AnnotationPayload.Empty;

    /// <summary>When true, the object cannot be selected/moved in the editor.</summary>
    public bool Locked { get; init; }

    /// <summary>Stacking order; higher values render on top.</summary>
    public int ZIndex { get; init; }

    /// <summary>Creates a new object of the given type with a fresh id.</summary>
    public static AnnotationObject Create(AnnotationObjectType type, AnnotationFrame frame, int zIndex)
        => new()
        {
            Id = Guid.NewGuid(),
            Type = type,
            Frame = frame,
            ZIndex = zIndex,
        };
}
