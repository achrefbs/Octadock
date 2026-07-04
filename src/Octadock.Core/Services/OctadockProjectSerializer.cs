using System.IO.Compression;
using System.Text.Json;
using Octadock.Core.Abstractions;
using Octadock.Core.Annotations;
using Octadock.Core.Common;
using Octadock.Core.Geometry;
using Octadock.Core.Projects;
using Octadock.Core.Services.Json;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="IProjectSerializer"/>. Reads and writes <c>.octadock</c>
/// annotation project packages: a ZIP archive containing <c>manifest.json</c>,
/// <c>original.png</c>, an optional <c>preview.png</c> and <c>objects.json</c>.
/// </summary>
public sealed class OctadockProjectSerializer : IProjectSerializer
{
    /// <summary>The manifest entry name inside the package.</summary>
    public const string ManifestEntry = "manifest.json";

    /// <summary>The base image entry name inside the package.</summary>
    public const string OriginalEntry = "original.png";

    /// <summary>The preview image entry name inside the package.</summary>
    public const string PreviewEntry = "preview.png";

    /// <summary>The vector objects entry name inside the package.</summary>
    public const string ObjectsEntry = "objects.json";

    private readonly IClock _clock;

    /// <summary>Creates the serializer.</summary>
    /// <param name="clock">Clock used to stamp new manifests; defaults to the system clock.</param>
    public OctadockProjectSerializer(IClock? clock = null)
        => _clock = clock ?? SystemClock.Instance;

    /// <inheritdoc />
    public async Task SaveAsync(
        string projectPath,
        AnnotationDocument document,
        ReadOnlyMemory<byte> baseImagePng,
        ReadOnlyMemory<byte>? previewPng,
        CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(projectPath);
        ArgumentNullException.ThrowIfNull(document);

        var manifest = new ProjectManifest
        {
            CreatedAt = _clock.UtcNow,
            BaseImage = OriginalEntry,
            ObjectsFile = ObjectsEntry,
            Canvas = new ProjectCanvas
            {
                Width = document.CanvasSize.Width,
                Height = document.CanvasSize.Height,
                Background = "transparent",
            },
            Metadata = new ProjectMetadata
            {
                SourceCaptureId = document.SourceCaptureId?.ToString("D"),
            },
        };

        string? directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Build the archive fully in memory then flush once, so a failure mid-write
        // never leaves a half-written .octadock on disk.
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            await WriteJsonEntryAsync(archive, ManifestEntry, manifest, cancellationToken).ConfigureAwait(false);
            await WriteBytesEntryAsync(archive, OriginalEntry, baseImagePng, cancellationToken).ConfigureAwait(false);

            if (previewPng is { } preview && preview.Length > 0)
            {
                await WriteBytesEntryAsync(archive, PreviewEntry, preview, cancellationToken).ConfigureAwait(false);
            }

            IReadOnlyList<AnnotationObject> objects = document.Objects;
            await WriteJsonEntryAsync(archive, ObjectsEntry, objects, cancellationToken).ConfigureAwait(false);
        }

        // Flush to a sibling temp file first, then atomically move it into place.
        // A cancellation or disk-full mid-copy then only ever discards the temp
        // file and leaves any existing .octadock package intact.
        buffer.Position = 0;
        string tempPath = projectPath + ".tmp-" + Path.GetRandomFileName();
        try
        {
            await using (FileStream file = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await buffer.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempPath, projectPath, overwrite: true);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of the scratch file; ignore failures.
        }
    }

    /// <inheritdoc />
    public async Task<ProjectLoadResult> LoadAsync(string projectPath, CancellationToken cancellationToken = default)
    {
        Guard.NotNullOrWhiteSpace(projectPath);
        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException("The project package does not exist.", projectPath);
        }

        await using FileStream file = new(projectPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);

        ProjectManifest manifest = await ReadJsonEntryAsync<ProjectManifest>(archive, ManifestEntry, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException($"'{ManifestEntry}' is missing or malformed.");

        // A hand-edited or corrupted manifest can carry explicit JSON nulls for
        // these sections; normalize so the reads below never NullReference.
        manifest = manifest with
        {
            Canvas = manifest.Canvas ?? new ProjectCanvas(),
            Metadata = manifest.Metadata ?? new ProjectMetadata(),
        };

        string baseImageEntry = string.IsNullOrWhiteSpace(manifest.BaseImage) ? OriginalEntry : manifest.BaseImage;
        byte[] baseImage = await ReadBytesEntryAsync(archive, baseImageEntry, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"Base image '{baseImageEntry}' is missing from the package.");

        string objectsEntry = string.IsNullOrWhiteSpace(manifest.ObjectsFile) ? ObjectsEntry : manifest.ObjectsFile;
        List<AnnotationObject> objects =
            await ReadJsonEntryAsync<List<AnnotationObject>>(archive, objectsEntry, cancellationToken)
                .ConfigureAwait(false)
            ?? [];

        Guid? sourceCaptureId = null;
        if (Guid.TryParse(manifest.Metadata.SourceCaptureId, out Guid parsed))
        {
            sourceCaptureId = parsed;
        }

        var document = new AnnotationDocument(
            new PixelSize(manifest.Canvas.Width, manifest.Canvas.Height),
            sourceCaptureId);
        document.ReplaceAll(objects);

        return new ProjectLoadResult(manifest, document, baseImage);
    }

    private static async Task WriteJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        T value,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await JsonSerializer.SerializeAsync(stream, value, OctadockJson.Options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteBytesEntryAsync(
        ZipArchive archive,
        string entryName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using Stream stream = entry.Open();
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonEntryAsync<T>(
        ZipArchive archive,
        string entryName,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return default;
        }

        await using Stream stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(stream, OctadockJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<byte[]?> ReadBytesEntryAsync(
        ZipArchive archive,
        string entryName,
        CancellationToken cancellationToken)
    {
        ZipArchiveEntry? entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return null;
        }

        await using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
