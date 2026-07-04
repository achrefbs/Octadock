using Octadock.Core.Abstractions;

namespace Octadock.Data.Tests.Infrastructure;

/// <summary>
/// A minimal <see cref="IStoragePaths"/> rooted at a temp directory, used to
/// exercise the DI wiring and the storage-path-based connection factory overload.
/// </summary>
internal sealed class FakeStoragePaths : IStoragePaths
{
    public FakeStoragePaths(string root)
    {
        RootDirectory = root;
        DatabasePath = Path.Combine(root, "octadock.db");
    }

    public string RootDirectory { get; }

    public string CapturesDirectory => Path.Combine(RootDirectory, "Captures");

    public string ProjectsDirectory => Path.Combine(RootDirectory, "Projects");

    public string RecordingsDirectory => Path.Combine(RootDirectory, "Recordings");

    public string ThumbnailsDirectory => Path.Combine(RootDirectory, "Thumbnails");

    public string TempExportsDirectory => Path.Combine(RootDirectory, "TempExports");

    public string LogsDirectory => Path.Combine(RootDirectory, "Logs");

    public string DatabasePath { get; }

    public void EnsureDirectories() => Directory.CreateDirectory(RootDirectory);

    public string ToAbsolute(string relativePath) => Path.Combine(RootDirectory, relativePath);

    public string ToRelative(string absolutePath) => Path.GetRelativePath(RootDirectory, absolutePath);

    public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension) =>
        Path.Combine("Captures", $"{id}{extension}");

    public string BuildThumbnailRelativePath(Guid id) => Path.Combine("Thumbnails", $"{id}.jpg");

    public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension) =>
        Path.Combine("Recordings", $"{id}{extension}");

    public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt) =>
        Path.Combine("Clipboard", $"{id}.png");
}
