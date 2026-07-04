using Octadock.Core.Primitives;

namespace Octadock.Core.Annotations;

/// <summary>
/// Type-specific data for an annotation object. Only the members relevant to the
/// object's <see cref="AnnotationObjectType"/> are populated.
/// </summary>
public sealed record AnnotationPayload
{
    /// <summary>Text content for <see cref="AnnotationObjectType.Text"/>.</summary>
    public string? Text { get; init; }

    /// <summary>
    /// Ordered points for line/arrow endpoints and freehand strokes, in
    /// image-pixel space. Empty for shape objects that use only the frame.
    /// </summary>
    public IReadOnlyList<PointD> Points { get; init; } = [];

    /// <summary>The number shown by a <see cref="AnnotationObjectType.Counter"/> step marker.</summary>
    public int? CounterValue { get; init; }

    /// <summary>Relative asset path for an embedded <see cref="AnnotationObjectType.Image"/> object.</summary>
    public string? ImageAssetPath { get; init; }

    public static readonly AnnotationPayload Empty = new();
}
