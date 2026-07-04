namespace Octadock.Core.Abstractions;

/// <summary>
/// Resolves Octadock's on-disk layout under the data root
/// (<c>%LOCALAPPDATA%\Octadock</c> in production). Capture/project/thumbnail
/// paths are stored relative to <see cref="RootDirectory"/> so the library is
/// portable; this service converts between relative and absolute forms.
/// </summary>
public interface IStoragePaths
{
    string RootDirectory { get; }

    string CapturesDirectory { get; }

    string ProjectsDirectory { get; }

    string RecordingsDirectory { get; }

    string ThumbnailsDirectory { get; }

    string TempExportsDirectory { get; }

    string LogsDirectory { get; }

    /// <summary>Absolute path to the SQLite database file.</summary>
    string DatabasePath { get; }

    /// <summary>Creates every Octadock directory if it does not already exist.</summary>
    void EnsureDirectories();

    /// <summary>Resolves a root-relative path to an absolute path.</summary>
    string ToAbsolute(string relativePath);

    /// <summary>Makes an absolute path relative to the root (returns the input if outside the root).</summary>
    string ToRelative(string absolutePath);

    /// <summary>Builds the root-relative capture path <c>Captures\YYYY\MM\DD\{id}{ext}</c>.</summary>
    string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension);

    /// <summary>Builds the root-relative thumbnail path <c>Thumbnails\{id}.jpg</c>.</summary>
    string BuildThumbnailRelativePath(Guid id);

    /// <summary>Builds the root-relative recording path <c>Recordings\{id}{ext}</c>.</summary>
    string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension);

    /// <summary>Builds the root-relative clipboard-image path <c>Clipboard\YYYY\MM\DD\{id}.png</c>.</summary>
    string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt);
}
