using System.Text.Json;

namespace Octadock.Core.Trial;

/// <summary>
/// Persists the monotonic trial-clock state to a JSON file OUTSIDE octadock.db
/// (WS4/WS5). A missing or unreadable file simply reseeds the clock — the trial is
/// reset-by-wipe by design, but the high-water mark cannot be rolled backward while
/// the file survives.
/// </summary>
public sealed class FileTrialClockStore : ITrialClockStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };
    private readonly string _path;

    public FileTrialClockStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = path;
    }

    public TrialClockState? Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            return JsonSerializer.Deserialize<TrialClockState>(File.ReadAllText(_path), Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(TrialClockState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(state, Options));
    }
}
