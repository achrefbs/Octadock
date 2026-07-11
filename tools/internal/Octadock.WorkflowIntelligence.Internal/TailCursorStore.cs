using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed record TailCursor(
    string CursorId,
    ProviderKind Provider,
    string PathSha256,
    DateTime CreationTimeUtc,
    long Offset,
    DateTimeOffset UpdatedAt);

internal sealed class TailCursorStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly TracePaths _paths;

    internal TailCursorStore(TracePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    internal TailCursor? Load(ProviderKind provider, string path)
    {
        string fullPath = Path.GetFullPath(path);
        string cursorId = BuildCursorId(provider, fullPath);
        string cursorPath = GetCursorPath(cursorId);
        if (!File.Exists(cursorPath))
        {
            return null;
        }

        TailCursor? cursor = JsonSerializer.Deserialize<TailCursor>(File.ReadAllText(cursorPath), JsonOptions);
        return cursor is not null &&
               string.Equals(cursor.CursorId, cursorId, StringComparison.Ordinal) &&
               string.Equals(cursor.PathSha256, HashPath(fullPath), StringComparison.Ordinal)
            ? cursor
            : null;
    }

    internal async Task SaveAsync(
        ProviderKind provider,
        string path,
        DateTime creationTimeUtc,
        long offset,
        CancellationToken cancellationToken = default)
    {
        string fullPath = Path.GetFullPath(path);
        string cursorId = BuildCursorId(provider, fullPath);
        var cursor = new TailCursor(
            cursorId,
            provider,
            HashPath(fullPath),
            creationTimeUtc,
            offset,
            DateTimeOffset.UtcNow);
        Directory.CreateDirectory(_paths.CursorsPath);
        string destination = GetCursorPath(cursorId);
        string temporary = $"{destination}.tmp-{Guid.NewGuid():N}";
        try
        {
            await File.WriteAllTextAsync(
                temporary,
                JsonSerializer.Serialize(cursor, JsonOptions),
                cancellationToken).ConfigureAwait(false);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private string GetCursorPath(string cursorId)
        => Path.Combine(_paths.CursorsPath, $"{cursorId}.json");

    private static string BuildCursorId(ProviderKind provider, string fullPath)
        => Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes($"v1\n{provider}\n{fullPath.ToUpperInvariant()}")));

    private static string HashPath(string fullPath)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath.ToUpperInvariant())));
}
