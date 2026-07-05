using System.IO;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// Filesystem watcher over one provider state directory (e.g. ~/.codex or
/// ~/.claude/projects). Any change simply requests a debounced rescan; the
/// collectors re-read state themselves, so the watcher never inspects content.
/// </summary>
public sealed class ProviderStateWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;

    private ProviderStateWatcher(FileSystemWatcher watcher)
    {
        _watcher = watcher;
    }

    /// <summary>Watched directory path, for diagnostics.</summary>
    public string Directory => _watcher.Path;

    /// <summary>Creates and starts a watcher, or null when the directory is unavailable.</summary>
    public static ProviderStateWatcher? TryCreate(
        string? directory,
        bool includeSubdirectories,
        Action onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);

        if (string.IsNullOrWhiteSpace(directory) || !System.IO.Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            var watcher = new FileSystemWatcher(directory)
            {
                IncludeSubdirectories = includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            };
            watcher.Changed += (_, _) => onChanged();
            watcher.Created += (_, _) => onChanged();
            watcher.Deleted += (_, _) => onChanged();
            watcher.Renamed += (_, _) => onChanged();
            watcher.EnableRaisingEvents = true;
            return new ProviderStateWatcher(watcher);
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or PlatformNotSupportedException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            _watcher.EnableRaisingEvents = false;
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            // Best-effort shutdown.
        }

        _watcher.Dispose();
    }
}
