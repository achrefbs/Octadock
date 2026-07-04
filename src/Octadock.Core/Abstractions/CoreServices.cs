using Octadock.Core.Annotations;
using Octadock.Core.Commands;
using Octadock.Core.Models;
using Octadock.Core.Naming;
using Octadock.Core.Projects;
using Octadock.Core.Settings;

namespace Octadock.Core.Abstractions;

/// <summary>Parses automation requests from protocol URIs and CLI arguments.</summary>
public interface ICommandParser
{
    /// <summary>Parses a <c>octadock://</c> URI.</summary>
    CommandParseResult ParseUri(string uri);

    /// <summary>Parses CLI arguments (the array passed to <c>octadock.exe</c>).</summary>
    CommandParseResult ParseArguments(IReadOnlyList<string> arguments);
}

/// <summary>Builds a canonical <c>octadock://</c> URI from a command (deep links, docs, tests).</summary>
public interface ICommandFormatter
{
    string ToUri(OctadockCommand command);
}

/// <summary>
/// Expands a filename template (<c>{yyyy}</c>, <c>{process}</c>, <c>{counter}</c>,
/// …) into a filesystem-safe base name (without extension).
/// </summary>
public interface IFilenameGenerator
{
    string Generate(string template, FilenameContext context);

    /// <summary>Replaces characters that are illegal in Windows filenames with safe equivalents.</summary>
    string Sanitize(string candidate);
}

/// <summary>The base image plus vector objects loaded from a <c>.octadock</c> package.</summary>
public sealed record ProjectLoadResult(ProjectManifest Manifest, AnnotationDocument Document, byte[] BaseImagePng);

/// <summary>
/// Reads and writes <c>.octadock</c> annotation project packages (a zip archive
/// containing manifest.json, original.png, preview.png and objects.json).
/// </summary>
public interface IProjectSerializer
{
    Task SaveAsync(
        string projectPath,
        AnnotationDocument document,
        ReadOnlyMemory<byte> baseImagePng,
        ReadOnlyMemory<byte>? previewPng,
        CancellationToken cancellationToken = default);

    Task<ProjectLoadResult> LoadAsync(string projectPath, CancellationToken cancellationToken = default);
}

/// <summary>Which captures a retention pass should purge or clean up.</summary>
public sealed record RetentionPlan(
    IReadOnlyList<Guid> ExpiredToDelete,
    IReadOnlyList<Guid> SoftDeletedToPurge)
{
    public bool IsEmpty => ExpiredToDelete.Count == 0 && SoftDeletedToPurge.Count == 0;

    public static readonly RetentionPlan Empty = new([], []);
}

/// <summary>Summary of what a retention pass actually removed.</summary>
public sealed record RetentionResult(int CapturesDeleted, int FilesDeleted, long BytesReclaimed)
{
    public static readonly RetentionResult Nothing = new(0, 0, 0);
}

/// <summary>
/// Applies the history retention policy: soft-deletes/purges captures older than
/// the configured window and permanently removes long-soft-deleted items and
/// their files. Runs opportunistically and never blocks capture.
/// </summary>
public interface IRetentionService
{
    /// <summary>Computes the retention plan for the given settings and time without mutating anything.</summary>
    Task<RetentionPlan> PlanAsync(OctadockSettings settings, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>Runs a full retention pass and returns what was reclaimed.</summary>
    Task<RetentionResult> RunAsync(OctadockSettings settings, CancellationToken cancellationToken = default);
}
