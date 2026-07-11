using System.IO;
using Octadock.Core.Io;

namespace Octadock.App.Ai;

/// <summary>
/// Conservative local-path boundary for AI evidence. Mapped network drives,
/// UNC/device paths, and reparse points are rejected before content is read so
/// a path that looks local cannot silently cross onto SMB, WebDAV, or a link.
/// </summary>
internal static class AgentLocalPathGuard
{
    public static string ValidateExistingFile(string path, string label)
    {
        string full = ValidateLexicalLocalPath(path, label);
        RejectReparseComponents(full, label, allowMissingTail: false);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"{label} was not found.", full);
        }

        return full;
    }

    public static string ValidateExistingDirectory(string path, string label)
    {
        string full = ValidateLexicalLocalPath(path, label);
        RejectReparseComponents(full, label, allowMissingTail: false);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"{label} was not found: {full}");
        }

        return full;
    }

    public static string ValidateDestinationDirectory(string path, string label)
    {
        string full = ValidateLexicalLocalPath(path, label);
        RejectReparseComponents(full, label, allowMissingTail: true);
        return full;
    }

    private static string ValidateLexicalLocalPath(string path, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (PathSafety.IsUncPath(path) ||
            path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{label} must be on a local drive, not a UNC or device path.");
        }

        if (path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException($"{label} cannot contain traversal segments.", nameof(path));
        }

        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"{label} is not a valid local path.", nameof(path), ex);
        }

        string root = Path.GetPathRoot(full)
            ?? throw new ArgumentException($"{label} has no local drive root.", nameof(path));
        try
        {
            if (new DriveInfo(root).DriveType == DriveType.Network)
            {
                throw new InvalidOperationException($"{label} must not use a mapped network drive.");
            }
        }
        catch (DriveNotFoundException ex)
        {
            throw new InvalidOperationException($"{label} uses an unavailable drive.", ex);
        }

        return full;
    }

    private static void RejectReparseComponents(string fullPath, string label, bool allowMissingTail)
    {
        string root = Path.GetPathRoot(fullPath)!;
        string relative = Path.GetRelativePath(root, fullPath);
        string current = root;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (Exception ex) when (
                allowMissingTail && ex is (FileNotFoundException or DirectoryNotFoundException))
            {
                return;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"{label} cannot pass through a symbolic link, junction, mount point, or cloud placeholder.");
            }
        }
    }
}
