using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="IStoragePaths"/> implementation. Resolves Octadock's
/// on-disk layout under a data root (defaulting to
/// <c>%LOCALAPPDATA%\Octadock</c>). Capture/project/thumbnail paths are stored
/// relative to <see cref="RootDirectory"/> with forward slashes for portability;
/// conversions tolerate both separators.
/// </summary>
public sealed class StoragePaths : IStoragePaths
{
    /// <summary>The folder name used for captures under the data root.</summary>
    public const string CapturesFolder = "Captures";

    /// <summary>The folder name used for annotation projects under the data root.</summary>
    public const string ProjectsFolder = "Projects";

    /// <summary>The folder name used for recordings under the data root.</summary>
    public const string RecordingsFolder = "Recordings";

    /// <summary>The folder name used for cached thumbnails under the data root.</summary>
    public const string ThumbnailsFolder = "Thumbnails";

    /// <summary>The folder name used for clipboard-history images under the data root.</summary>
    public const string ClipboardFolder = "Clipboard";

    /// <summary>The folder name used for scratch export files under the data root.</summary>
    public const string TempExportsFolder = "TempExports";

    /// <summary>The folder name used for logs under the data root.</summary>
    public const string LogsFolder = "Logs";

    /// <summary>The SQLite database file name under the data root.</summary>
    public const string DatabaseFileName = "octadock.db";

    /// <summary>Creates storage paths under an explicit data root.</summary>
    /// <param name="rootDirectory">
    /// The data root. When null or whitespace, defaults to
    /// <c>%LOCALAPPDATA%\Octadock</c>.
    /// </param>
    public StoragePaths(string? rootDirectory = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootDirectory)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Octadock")
            : Path.GetFullPath(rootDirectory);

        CapturesDirectory = Path.Combine(RootDirectory, CapturesFolder);
        ProjectsDirectory = Path.Combine(RootDirectory, ProjectsFolder);
        RecordingsDirectory = Path.Combine(RootDirectory, RecordingsFolder);
        ThumbnailsDirectory = Path.Combine(RootDirectory, ThumbnailsFolder);
        TempExportsDirectory = Path.Combine(RootDirectory, TempExportsFolder);
        LogsDirectory = Path.Combine(RootDirectory, LogsFolder);
        DatabasePath = Path.Combine(RootDirectory, DatabaseFileName);
    }

    /// <inheritdoc />
    public string RootDirectory { get; }

    /// <inheritdoc />
    public string CapturesDirectory { get; }

    /// <inheritdoc />
    public string ProjectsDirectory { get; }

    /// <inheritdoc />
    public string RecordingsDirectory { get; }

    /// <inheritdoc />
    public string ThumbnailsDirectory { get; }

    /// <inheritdoc />
    public string TempExportsDirectory { get; }

    /// <inheritdoc />
    public string LogsDirectory { get; }

    /// <inheritdoc />
    public string DatabasePath { get; }

    /// <inheritdoc />
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(CapturesDirectory);
        Directory.CreateDirectory(ProjectsDirectory);
        Directory.CreateDirectory(RecordingsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(TempExportsDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    /// <inheritdoc />
    public string ToAbsolute(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return RootDirectory;
        }

        // Already absolute: return normalized as-is.
        if (Path.IsPathRooted(relativePath))
        {
            return Path.GetFullPath(relativePath);
        }

        string normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.Combine(RootDirectory, normalized));
    }

    /// <inheritdoc />
    public string ToRelative(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return absolutePath;
        }

        string full = Path.IsPathRooted(absolutePath)
            ? Path.GetFullPath(absolutePath)
            : Path.GetFullPath(Path.Combine(RootDirectory, absolutePath));

        string relative = Path.GetRelativePath(RootDirectory, full);

        // Outside the root (Path.GetRelativePath yields "..\.." or an absolute
        // path when on a different volume): return the input unchanged.
        if (relative == "." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative == ".." ||
            Path.IsPathRooted(relative))
        {
            return absolutePath;
        }

        return NormalizeSeparators(relative);
    }

    /// <inheritdoc />
    public string BuildCaptureRelativePath(Guid id, DateTimeOffset createdAt, string extension)
    {
        string ext = NormalizeExtension(extension);
        DateTimeOffset stamp = createdAt;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{CapturesFolder}/{stamp:yyyy}/{stamp:MM}/{stamp:dd}/{id:D}{ext}");
    }

    /// <inheritdoc />
    public string BuildThumbnailRelativePath(Guid id)
        => $"{ThumbnailsFolder}/{id:D}.jpg";

    /// <inheritdoc />
    public string BuildClipboardImageRelativePath(Guid id, DateTimeOffset createdAt)
    {
        DateTimeOffset stamp = createdAt;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{ClipboardFolder}/{stamp:yyyy}/{stamp:MM}/{stamp:dd}/{id:D}.png");
    }

    /// <inheritdoc />
    public string BuildRecordingRelativePath(Guid id, DateTimeOffset createdAt, string extension)
    {
        string ext = NormalizeExtension(extension);
        DateTimeOffset stamp = createdAt;
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{RecordingsFolder}/{stamp:yyyy}/{stamp:MM}/{stamp:dd}/{id:D}{ext}");
    }

    private static string NormalizeSeparators(string path)
        => path.Replace('\\', '/');

    private static string NormalizeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }
}
