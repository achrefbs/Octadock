namespace Octadock.Core.Projects;

/// <summary>
/// The <c>manifest.json</c> at the root of a <c>.octadock</c> project package.
/// Matches the annotation project manifest in the data spec.
/// </summary>
public sealed record ProjectManifest
{
    /// <summary>Format discriminator. Always <c>octadock-project</c>.</summary>
    public string Format { get; init; } = "octadock-project";

    /// <summary>Manifest schema version.</summary>
    public int Version { get; init; } = 1;

    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>Relative path to the original raster inside the package.</summary>
    public string BaseImage { get; init; } = "original.png";

    public ProjectCanvas Canvas { get; init; } = new();

    /// <summary>Relative path to the serialized vector objects.</summary>
    public string ObjectsFile { get; init; } = "objects.json";

    public ProjectMetadata Metadata { get; init; } = new();
}

/// <summary>Canvas description within a project manifest.</summary>
public sealed record ProjectCanvas
{
    public int Width { get; init; }

    public int Height { get; init; }

    /// <summary>Canvas background: <c>transparent</c> or a hex color.</summary>
    public string Background { get; init; } = "transparent";
}

/// <summary>Provenance metadata within a project manifest.</summary>
public sealed record ProjectMetadata
{
    /// <summary>The source capture id, when the project began from a capture.</summary>
    public string? SourceCaptureId { get; init; }
}
