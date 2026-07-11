using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class CorpusInspector
{
    private const int DefaultMaxFiles = 5;
    private const long DefaultMaxCharactersPerFile = 16L * 1024 * 1024;

    private static readonly EnumerationOptions Enumeration = new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        ReturnSpecialDirectories = false,
    };

    internal static CorpusInventory Inventory(ProviderKind provider, string? root = null)
    {
        string resolvedRoot = ResolveRoot(provider, root);
        if (!Directory.Exists(resolvedRoot))
        {
            return new CorpusInventory(provider, resolvedRoot, 0, 0, 0, 0, null);
        }

        FileInfo[] allFiles = EnumerateCorpusFiles(resolvedRoot).ToArray();
        FileInfo[] jsonl = allFiles.Where(file => IsJsonl(file.Name)).ToArray();
        FileInfo[] compressed = allFiles.Where(file => IsCompressedJsonl(file.Name)).ToArray();
        return new CorpusInventory(
            provider,
            resolvedRoot,
            jsonl.Length,
            compressed.Length,
            allFiles.Sum(file => file.Length),
            allFiles.Length == 0 ? 0 : allFiles.Max(file => file.Length),
            allFiles.Length == 0 ? null : allFiles.Max(file => new DateTimeOffset(file.LastWriteTimeUtc, TimeSpan.Zero)));
    }

    internal static async Task<ProviderInspection> InspectAsync(
        ProviderKind provider,
        string? root = null,
        int maxFiles = DefaultMaxFiles,
        long maxCharactersPerFile = DefaultMaxCharactersPerFile,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFiles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharactersPerFile);

        string resolvedRoot = ResolveRoot(provider, root);
        if (!Directory.Exists(resolvedRoot))
        {
            return new ProviderInspection(
                provider, false, 0, 0, 0, 0, 0, 0, 0, 0, [], ["Provider corpus root does not exist."]);
        }

        FileInfo[] selected = EnumerateCorpusFiles(resolvedRoot)
            .Where(file => IsJsonl(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(maxFiles)
            .ToArray();

        long characters = 0;
        int parsed = 0;
        int errors = 0;
        int users = 0;
        int assistants = 0;
        int starts = 0;
        int completions = 0;
        var types = new HashSet<string>(StringComparer.Ordinal);
        var warnings = new List<string>();

        foreach (FileInfo file in selected)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = new(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, 64 * 1024, leaveOpen: false);
            long fileCharacters = 0;
            while (fileCharacters < maxCharactersPerFile && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                characters += line.Length;
                fileCharacters += line.Length;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    parsed++;
                    InspectRecord(
                        provider,
                        document.RootElement,
                        types,
                        ref users,
                        ref assistants,
                        ref starts,
                        ref completions);
                }
                catch (JsonException)
                {
                    errors++;
                }
            }

            if (!reader.EndOfStream)
            {
                warnings.Add("At least one large session was sampled incrementally rather than reparsed in full.");
            }
        }

        CorpusInventory inventory = Inventory(provider, resolvedRoot);
        if (inventory.CompressedFiles > 0)
        {
            warnings.Add($"{inventory.CompressedFiles} compressed session file(s) require a separate decompression adapter.");
        }

        bool hasSignature = provider switch
        {
            ProviderKind.Claude => types.Contains("assistant") && types.Contains("user"),
            ProviderKind.Codex => types.Contains("session_meta") || types.Contains("turn_context"),
            _ => false,
        };
        bool healthy = selected.Length > 0 && parsed > 0 && hasSignature && errors <= Math.Max(2, parsed / 100);
        if (!healthy)
        {
            warnings.Add("Adapter signature or JSON error threshold failed; live collection must degrade to metadata-only.");
        }

        return new ProviderInspection(
            provider,
            healthy,
            selected.Length,
            characters,
            parsed,
            errors,
            users,
            assistants,
            starts,
            completions,
            types.Order(StringComparer.Ordinal).ToArray(),
            warnings.Distinct(StringComparer.Ordinal).ToArray());
    }

    internal static CorpusSampleManifest CreateSampleManifest(
        int requestedCount,
        DateTimeOffset createdAt,
        string? claudeRoot = null,
        string? codexRoot = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCount);

        int perProvider = Math.Max(1, requestedCount / 2);
        var samples = new List<CorpusSample>(requestedCount);
        samples.AddRange(SelectSamples(ProviderKind.Claude, ResolveRoot(ProviderKind.Claude, claudeRoot), perProvider));
        samples.AddRange(SelectSamples(ProviderKind.Codex, ResolveRoot(ProviderKind.Codex, codexRoot), requestedCount - samples.Count));
        return new CorpusSampleManifest(
            createdAt,
            requestedCount,
            samples.Take(requestedCount).ToArray(),
            "Metadata-only internal sample manifest. Relative paths may identify projects; keep this file local.");
    }

    private static CorpusSample[] SelectSamples(ProviderKind provider, string root, int count)
    {
        if (count <= 0 || !Directory.Exists(root))
        {
            return [];
        }

        FileInfo[] files = EnumerateCorpusFiles(root).Where(file => IsJsonl(file.Name)).ToArray();
        if (files.Length == 0)
        {
            return [];
        }

        int largestCount = Math.Max(1, count / 3);
        int newestCount = Math.Max(1, count / 3);
        var selected = new Dictionary<string, (FileInfo File, string Reason)>(StringComparer.OrdinalIgnoreCase);
        foreach (FileInfo file in files.OrderByDescending(file => file.Length).Take(largestCount))
        {
            selected[file.FullName] = (file, "largest");
        }
        foreach (FileInfo file in files.OrderByDescending(file => file.LastWriteTimeUtc).Take(newestCount))
        {
            selected.TryAdd(file.FullName, (file, "newest"));
        }
        foreach (FileInfo file in files
                     .OrderBy(file => StablePathHash(file.FullName), StringComparer.Ordinal))
        {
            selected.TryAdd(file.FullName, (file, "deterministic-diversity"));
            if (selected.Count >= count)
            {
                break;
            }
        }

        return selected.Values
            .Take(count)
            .Select(value => new CorpusSample(
                provider,
                Path.GetRelativePath(root, value.File.FullName),
                value.File.Length,
                new DateTimeOffset(value.File.LastWriteTimeUtc, TimeSpan.Zero),
                value.Reason))
            .ToArray();
    }

    internal static TailInspection InspectLines(ProviderKind provider, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        int parsed = 0;
        int errors = 0;
        int users = 0;
        int assistants = 0;
        int starts = 0;
        int completions = 0;
        var types = new HashSet<string>(StringComparer.Ordinal);

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(line);
                parsed++;
                InspectRecord(
                    provider,
                    document.RootElement,
                    types,
                    ref users,
                    ref assistants,
                    ref starts,
                    ref completions);
            }
            catch (JsonException)
            {
                errors++;
            }
        }

        return new TailInspection(
            parsed,
            errors,
            users,
            assistants,
            starts,
            completions,
            types.Order(StringComparer.Ordinal).ToArray());
    }

    private static void InspectRecord(
        ProviderKind provider,
        JsonElement root,
        HashSet<string> types,
        ref int users,
        ref int assistants,
        ref int starts,
        ref int completions)
    {
        string? topType = GetString(root, "type");
        if (topType is not null)
        {
            types.Add(topType);
        }

        if (provider == ProviderKind.Claude)
        {
            if (string.Equals(topType, "user", StringComparison.Ordinal))
            {
                users++;
                starts++;
            }
            else if (string.Equals(topType, "assistant", StringComparison.Ordinal))
            {
                assistants++;
                if (HasCompletionMarker(root))
                {
                    completions++;
                }
            }
            return;
        }

        if (!root.TryGetProperty("payload", out JsonElement payload) || payload.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        string? payloadType = GetString(payload, "type");
        if (payloadType is not null)
        {
            types.Add($"payload:{payloadType}");
        }

        switch (payloadType)
        {
            case "task_started":
                starts++;
                break;
            case "task_complete":
                completions++;
                break;
            case "user_message":
                users++;
                break;
            case "agent_message":
                assistants++;
                break;
            case "message":
                string? role = GetString(payload, "role");
                if (string.Equals(role, "user", StringComparison.Ordinal)) users++;
                if (string.Equals(role, "assistant", StringComparison.Ordinal)) assistants++;
                break;
        }
    }

    private static bool HasCompletionMarker(JsonElement root)
    {
        if (HasNonNullProperty(root, "stopReason") || HasNonNullProperty(root, "stop_reason"))
        {
            return true;
        }
        return root.TryGetProperty("message", out JsonElement message) &&
               message.ValueKind == JsonValueKind.Object &&
               (HasNonNullProperty(message, "stop_reason") || HasNonNullProperty(message, "stopReason"));
    }

    private static bool HasNonNullProperty(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) &&
           value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined);

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<FileInfo> EnumerateCorpusFiles(string root)
        => Directory.EnumerateFiles(root, "*", Enumeration)
            .Where(path => IsJsonl(Path.GetFileName(path)) || IsCompressedJsonl(Path.GetFileName(path)))
            .Select(path => new FileInfo(path));

    private static bool IsJsonl(string name)
        => name.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompressedJsonl(string name)
        => name.EndsWith(".jsonl.zst", StringComparison.OrdinalIgnoreCase) ||
           name.EndsWith(".jsonl.gz", StringComparison.OrdinalIgnoreCase);

    private static string ResolveRoot(ProviderKind provider, string? root)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? ProviderRoots.For(provider) : root);

    private static string StablePathHash(string path)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));
}
