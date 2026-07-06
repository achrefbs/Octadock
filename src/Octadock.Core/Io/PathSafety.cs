namespace Octadock.Core.Io;

/// <summary>
/// Path classification for the file-safety guards (WS9, R32). Pure string checks —
/// none of these touch the filesystem or network, so they can run BEFORE any I/O
/// (critical for UNC: even <c>File.Exists</c> on a <c>\\host\share</c> path opens a
/// connection that can leak NTLM credentials).
/// </summary>
public static class PathSafety
{
    // Extensions Windows will execute or that carry executable payloads. Kept broad
    // on purpose; the fallback card must never one-click-launch any of these.
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".bat", ".cmd", ".com", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse",
        ".wsf", ".wsh", ".msi", ".msp", ".scr", ".lnk", ".pif", ".hta", ".cpl", ".reg", ".jar",
    };

    /// <summary>True if the path is a UNC / network path (<c>\\server\share</c> or a UNC URI).</summary>
    public static bool IsUncPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string trimmed = path.TrimStart();
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return true;
            }

            return Uri.TryCreate(full, UriKind.Absolute, out Uri? uri) && uri.IsUnc;
        }
        catch (Exception ex) when (
            ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            // An unparseable path is treated as unsafe (fail closed).
            return true;
        }
    }

    /// <summary>True if the path has an executable/script extension that must not one-click launch.</summary>
    public static bool IsExecutableExtension(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string extension = Path.GetExtension(path);
        return !string.IsNullOrEmpty(extension) && ExecutableExtensions.Contains(extension);
    }
}
