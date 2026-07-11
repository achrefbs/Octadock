using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.WorkflowIntelligence.Internal;

internal static class Program
{
    private const string DeleteConfirmation = "DELETE_INTERNAL_TRACES";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    internal static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || IsHelp(args[0]))
            {
                WriteHelp();
                return 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "inventory" => RunInventory(args[1..]),
                "self-test" => await RunSelfTestAsync(args[1..]).ConfigureAwait(false),
                "sample" => await RunSampleAsync(args[1..]).ConfigureAwait(false),
                "tail" => await RunTailAsync(args[1..]).ConfigureAwait(false),
                "reconstruct" => await RunReconstructAsync(args[1..]).ConfigureAwait(false),
                "brief" => await RunBriefAsync(args[1..]).ConfigureAwait(false),
                "evaluate-briefs" => await RunEvaluateBriefsAsync(args[1..]).ConfigureAwait(false),
                "process-hook" => await RunProcessHookAsync(args[1..]).ConfigureAwait(false),
                "ingest-hook" => await RunIngestHookAsync(args[1..]).ConfigureAwait(false),
                "purge-expired" => await RunPurgeAsync(args[1..]).ConfigureAwait(false),
                "delete-all" => await RunDeleteAllAsync(args[1..]).ConfigureAwait(false),
                "paths" => RunPaths(),
                _ => throw new ArgumentException($"Unknown internal command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static int RunInventory(string[] args)
    {
        ProviderKind? provider = ParseOptionalProvider(GetOption(args, "--provider"));
        object result = provider is { } one
            ? CorpusInspector.Inventory(one, GetOption(args, "--root"))
            : new[]
            {
                CorpusInspector.Inventory(ProviderKind.Claude),
                CorpusInspector.Inventory(ProviderKind.Codex),
            };
        WriteJson(result);
        return 0;
    }

    private static async Task<int> RunSelfTestAsync(string[] args)
    {
        ProviderKind? provider = ParseOptionalProvider(GetOption(args, "--provider"));
        ProviderInspection[] results = provider is { } one
            ? [await CorpusInspector.InspectAsync(one, GetOption(args, "--root")).ConfigureAwait(false)]
            :
            [
                await CorpusInspector.InspectAsync(ProviderKind.Claude).ConfigureAwait(false),
                await CorpusInspector.InspectAsync(ProviderKind.Codex).ConfigureAwait(false),
            ];
        WriteJson(results);
        return results.All(result => result.Healthy) ? 0 : 2;
    }

    private static async Task<int> RunSampleAsync(string[] args)
    {
        int count = ParsePositiveInt(GetOption(args, "--count"), defaultValue: 50);
        TracePaths paths = ResolveTracePaths(args);
        string output = Path.GetFullPath(GetOption(args, "--output") ?? paths.SampleManifestPath);
        CorpusSampleManifest manifest = CorpusInspector.CreateSampleManifest(count, DateTimeOffset.UtcNow);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllTextAsync(output, JsonSerializer.Serialize(manifest, JsonOptions)).ConfigureAwait(false);
        WriteJson(new { output, samples = manifest.Samples.Count, manifest.Notice });
        return manifest.Samples.Count == 0 ? 2 : 0;
    }

    private static async Task<int> RunIngestHookAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        ProviderKind provider = ParseRequiredProvider(GetOption(args, "--provider"));
        string payload = await Console.In.ReadToEndAsync().ConfigureAwait(false);
        HookPayload hook = HookPayloadParser.Parse(provider, payload);
        if (string.IsNullOrEmpty(hook.LastAssistantMessage))
        {
            WriteJson(new { accepted = true, stored = false, reason = "No last assistant message in hook payload.", hook.EventName });
            return 0;
        }

        TracePaths paths = ResolveTracePaths(args);
        int retentionDays = ParsePositiveInt(GetOption(args, "--retention-days"), defaultValue: 7);
        var store = new EncryptedRawTraceStore(paths);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        Guid id = await store.StoreAsync(
            $"{provider.ToString().ToLowerInvariant()}-hook-message",
            hook.LastAssistantMessage,
            createdAt,
            createdAt.AddDays(retentionDays)).ConfigureAwait(false);
        WriteJson(new
        {
            accepted = true,
            stored = true,
            rawContentId = id,
            hook.EventName,
            provider = provider.ToString(),
            characters = hook.LastAssistantMessage.Length,
            hook.SessionId,
            hook.TurnId,
            hook.PromptId,
        });
        return 0;
    }

    private static async Task<int> RunProcessHookAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        ProviderKind provider = ParseRequiredProvider(GetOption(args, "--provider"));
        HookPayload hook = HookPayloadParser.Parse(
            provider,
            await Console.In.ReadToEndAsync().ConfigureAwait(false));
        if (hook.StopHookActive)
        {
            WriteJson(new { accepted = true, generated = false, reason = "Recursive Stop hook invocation was ignored.", hook.EventName });
            return 0;
        }
        if (string.Equals(hook.EventName, "SubagentStop", StringComparison.OrdinalIgnoreCase) &&
            !HasFlag(args, "--include-subagents"))
        {
            WriteJson(new { accepted = true, generated = false, reason = "Subagent completion is retained for the parent turn, not emitted as a separate brief.", hook.EventName });
            return 0;
        }
        if (!string.Equals(hook.EventName, "Stop", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(hook.EventName, "SubagentStop", StringComparison.OrdinalIgnoreCase))
        {
            WriteJson(new { accepted = true, generated = false, reason = "The hook is not a completion boundary.", hook.EventName });
            return 0;
        }
        if (string.IsNullOrWhiteSpace(hook.TranscriptPath) || !File.Exists(hook.TranscriptPath))
        {
            WriteJson(new { accepted = true, generated = false, reason = "The completion hook has no readable transcript path.", hook.EventName });
            return 2;
        }

        string? requestedId = provider == ProviderKind.Codex ? hook.TurnId : hook.PromptId;
        TurnReconstructionResult reconstruction = await TurnReconstructor.ReconstructLatestAsync(
            provider,
            hook.TranscriptPath,
            requestedId).ConfigureAwait(false);
        bool usedLatestFallback = false;
        if (reconstruction.Turn is null && provider == ProviderKind.Claude && requestedId is not null)
        {
            reconstruction = await TurnReconstructor.ReconstructLatestAsync(
                provider,
                hook.TranscriptPath).ConfigureAwait(false);
            usedLatestFallback = reconstruction.Turn is not null;
        }
        if (reconstruction.Turn is not { } turn)
        {
            WriteJson(new { accepted = true, generated = false, reason = "No provider turn matched the completion hook.", hook.EventName });
            return 2;
        }
        if (!string.IsNullOrWhiteSpace(hook.SessionId) &&
            !string.IsNullOrWhiteSpace(turn.SessionId) &&
            !string.Equals(hook.SessionId, turn.SessionId, StringComparison.Ordinal))
        {
            WriteJson(new { accepted = true, generated = false, reason = "Hook and transcript session identities do not match.", hook.EventName });
            return 3;
        }

        IBriefRanker ranker = ParseBriefRanker(GetOption(args, "--ranker"));
        BriefGenerationResult generation = await TrustedBriefPipeline.GenerateAsync(
            turn,
            ranker,
            HasFlag(args, "--force")).ConfigureAwait(false);
        if (generation.Brief is not { } draft || generation.Verification is not { } verification)
        {
            WriteJson(new
            {
                accepted = true,
                generated = false,
                suppressed = true,
                generation.Eligibility.Reason,
                generation.Eligibility.SourceWords,
                usedLatestFallback,
            });
            return 0;
        }

        SourceAnchorValidationResult anchorValidation = await SourceAnchorValidator.ValidateAsync(
            hook.TranscriptPath,
            turn,
            draft).ConfigureAwait(false);
        TrustedBrief brief = SourceAnchorValidator.ApplyPolicy(draft, anchorValidation);
        var stored = new StoredTrustedBrief(
            "octadock-trusted-brief/v2",
            provider,
            turn.IdentityHash,
            turn.TurnId,
            brief,
            verification,
            anchorValidation,
            DateTimeOffset.UtcNow);
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        int retentionDays = ParsePositiveInt(GetOption(args, "--retention-days"), defaultValue: 7);
        Guid stableId = StableGuid(
            $"trusted-brief\n{provider}\n{turn.IdentityHash}\n{TrustedBriefComposer.Profile}\n{ranker.Profile}");
        var store = new EncryptedRawTraceStore(ResolveTracePaths(args));
        RawTraceStoreResult storage = await store.StoreOnceAsync(
            stableId,
            "trusted-brief",
            JsonSerializer.Serialize(stored, JsonOptions),
            createdAt,
            createdAt.AddDays(retentionDays)).ConfigureAwait(false);
        WriteJson(new
        {
            accepted = true,
            generated = true,
            status = brief.Status,
            artifactId = storage.Id,
            artifactCreated = storage.Created,
            duplicateCompletion = !storage.Created,
            evidenceItems = generation.Ledger.Items.Count,
            highImportanceEvidence = generation.Ledger.Items.Count(item => item.Importance >= EvidenceImportance.High),
            ledgerHighImportanceRecall = verification.LedgerHighImportanceRecall,
            sourceNavigationSuccessRate = anchorValidation.SuccessRate,
            usedLatestFallback,
            warnings = brief.Warnings,
        });
        return brief.Status switch
        {
            BriefStatus.Trusted => 0,
            BriefStatus.CoverageWarning => 2,
            BriefStatus.Withheld => 3,
            _ => 2,
        };
    }

    private static async Task<int> RunTailAsync(string[] args)
    {
        ProviderKind provider = ParseRequiredProvider(GetOption(args, "--provider"));
        string file = Path.GetFullPath(
            GetOption(args, "--file") ?? throw new ArgumentException("--file PATH is required."));
        int maxBytes = ParsePositiveInt(GetOption(args, "--max-bytes"), defaultValue: 8 * 1024 * 1024);
        TracePaths paths = ResolveTracePaths(args);
        var cursors = new TailCursorStore(paths);
        TailCursor? cursor = cursors.Load(provider, file);
        DateTime creationTimeUtc = File.GetCreationTimeUtc(file);
        bool replaced = cursor is not null && cursor.CreationTimeUtc != creationTimeUtc;
        long requestedOffset = replaced ? 0 : cursor?.Offset ?? 0;
        IncrementalTailBatch batch = await IncrementalJsonlTailer.ReadAsync(
            file,
            requestedOffset,
            maxBytes).ConfigureAwait(false);
        TailInspection inspection = CorpusInspector.InspectLines(provider, batch.Lines);
        await cursors.SaveAsync(provider, file, creationTimeUtc, batch.NextOffset).ConfigureAwait(false);
        WriteJson(new
        {
            provider,
            filePathSha256 = cursor?.PathSha256 ?? Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(file.ToUpperInvariant()))),
            previousOffset = cursor?.Offset ?? 0,
            batch.EffectiveOffset,
            batch.NextOffset,
            batch.FileLength,
            reset = replaced || batch.ResetAfterTruncation,
            batch.HasIncompleteLine,
            batch.HasUnreadBytes,
            batch.BlockedByOversizedLine,
            completeLines = batch.Lines.Count,
            inspection,
        });
        return batch.BlockedByOversizedLine || inspection.ParseErrors > 0 ? 2 : 0;
    }

    private static async Task<int> RunReconstructAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        ProviderKind provider = ParseRequiredProvider(GetOption(args, "--provider"));
        string file = Path.GetFullPath(
            GetOption(args, "--file") ?? throw new ArgumentException("--file PATH is required."));
        TurnReconstructionResult result = await TurnReconstructor.ReconstructLatestAsync(
            provider,
            file,
            GetOption(args, "--turn-id")).ConfigureAwait(false);
        ReconstructedTurn? turn = result.Turn;
        WriteJson(new
        {
            provider,
            result.TurnsObserved,
            result.CompleteTurnsObserved,
            result.RecordsParsed,
            result.ParseErrors,
            result.SourceBytesRead,
            durationMilliseconds = result.Duration.TotalMilliseconds,
            found = turn is not null,
            turn = turn is null ? null : new
            {
                turn.IdentityHash,
                turn.TurnId,
                turn.PromptId,
                workingDirectorySha256 = HashSensitiveValue(turn.WorkingDirectory),
                turn.Model,
                turn.Boundary,
                segments = turn.Segments.Count,
                segmentKinds = turn.Segments.GroupBy(segment => segment.Kind)
                    .ToDictionary(group => group.Key.ToString(), group => group.Count(), StringComparer.Ordinal),
                turn.Warnings,
            },
        });
        return turn is null || !turn.Boundary.Complete ? 2 : 0;
    }

    private static async Task<int> RunBriefAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        ProviderKind provider = ParseRequiredProvider(GetOption(args, "--provider"));
        string file = Path.GetFullPath(
            GetOption(args, "--file") ?? throw new ArgumentException("--file PATH is required."));
        TurnReconstructionResult reconstruction = await TurnReconstructor.ReconstructLatestAsync(
            provider,
            file,
            GetOption(args, "--turn-id")).ConfigureAwait(false);
        if (reconstruction.Turn is not { } turn)
        {
            WriteJson(new { generated = false, reason = "No matching provider turn was reconstructed.", reconstruction.Warnings });
            return 2;
        }

        IBriefRanker ranker = ParseBriefRanker(GetOption(args, "--ranker"));
        BriefGenerationResult result = await TrustedBriefPipeline.GenerateAsync(
            turn,
            ranker,
            HasFlag(args, "--force")).ConfigureAwait(false);
        SourceAnchorValidationResult? anchorValidation = result.Brief is { } generatedBrief
            ? await SourceAnchorValidator.ValidateAsync(file, turn, generatedBrief).ConfigureAwait(false)
            : null;
        TrustedBrief? policyCheckedBrief = result.Brief is { } uncheckedBrief && anchorValidation is not null
            ? SourceAnchorValidator.ApplyPolicy(uncheckedBrief, anchorValidation)
            : result.Brief;
        Guid? storedId = null;
        if (HasFlag(args, "--store") && policyCheckedBrief is { } brief && result.Verification is { } verification && anchorValidation is not null)
        {
            TracePaths paths = ResolveTracePaths(args);
            int retentionDays = ParsePositiveInt(GetOption(args, "--retention-days"), defaultValue: 7);
            var stored = new StoredTrustedBrief(
                "octadock-trusted-brief/v2",
                provider,
                turn.IdentityHash,
                turn.TurnId,
                brief,
                verification,
                anchorValidation,
                DateTimeOffset.UtcNow);
            var store = new EncryptedRawTraceStore(paths);
            DateTimeOffset createdAt = DateTimeOffset.UtcNow;
            storedId = await store.StoreAsync(
                "trusted-brief",
                JsonSerializer.Serialize(stored, JsonOptions),
                createdAt,
                createdAt.AddDays(retentionDays)).ConfigureAwait(false);
        }

        if (HasFlag(args, "--show"))
        {
            WriteJson(new
            {
                sensitiveRawDerivedContent = true,
                result.Eligibility,
                brief = policyCheckedBrief,
                result.Verification,
                sourceAnchorValidation = anchorValidation,
                storedId,
            });
        }
        else
        {
            WriteJson(new
            {
                generated = policyCheckedBrief is not null,
                result.Eligibility,
                status = policyCheckedBrief?.Status,
                evidenceItems = result.Ledger.Items.Count,
                highImportanceEvidence = result.Ledger.Items.Count(item => item.Importance >= EvidenceImportance.High),
                ledgerHighImportanceRecall = result.Verification?.LedgerHighImportanceRecall,
                policyCheckedBrief?.SourceCoverage,
                policyCheckedBrief?.EstimatedOriginalReadMinutes,
                policyCheckedBrief?.EstimatedBriefReadMinutes,
                policyCheckedBrief?.Warnings,
                sourceNavigationSuccessRate = anchorValidation?.SuccessRate,
                storedId,
            });
        }

        return policyCheckedBrief?.Status switch
        {
            null or BriefStatus.Suppressed => 0,
            BriefStatus.Trusted => 0,
            BriefStatus.CoverageWarning => 2,
            BriefStatus.Withheld => 3,
            _ => 2,
        };
    }

    private static async Task<int> RunEvaluateBriefsAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        ProviderKind? provider = ParseOptionalProvider(GetOption(args, "--provider"));
        int maximumFiles = ParsePositiveInt(GetOption(args, "--max-files"), defaultValue: 25);
        bool force = HasFlag(args, "--force");
        IBriefRanker ranker = ParseBriefRanker(GetOption(args, "--ranker"));
        BriefEvaluationReport[] reports;
        if (provider is { } one)
        {
            string root = GetOption(args, "--root") ?? ProviderRoots.For(one);
            reports = [await BriefEvaluationRunner.EvaluateAsync(one, root, maximumFiles, force, ranker).ConfigureAwait(false)];
        }
        else
        {
            if (GetOption(args, "--root") is not null)
            {
                throw new ArgumentException("--root requires one explicit --provider.");
            }
            reports =
            [
                await BriefEvaluationRunner.EvaluateAsync(
                    ProviderKind.Claude, ProviderRoots.Claude, maximumFiles, force, ranker).ConfigureAwait(false),
                await BriefEvaluationRunner.EvaluateAsync(
                    ProviderKind.Codex, ProviderRoots.Codex, maximumFiles, force, ranker).ConfigureAwait(false),
            ];
        }
        WriteJson(reports);
        return reports.All(report =>
            report.FilesEvaluated > 0 &&
            report.TurnsReconstructed > 0 &&
            report.OmittedHighImportanceEvidence == 0 &&
            report.UnsupportedClaims == 0 &&
            report.SourceAnchorFailures == 0)
            ? 0
            : 2;
    }

    private static async Task<int> RunPurgeAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        var store = new EncryptedRawTraceStore(ResolveTracePaths(args));
        int purged = await store.PurgeExpiredAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);
        WriteJson(new { purged });
        return 0;
    }

    private static async Task<int> RunDeleteAllAsync(string[] args)
    {
        InternalConsent.DemandFullDeveloperTrace();
        string? confirmation = GetOption(args, "--confirm");
        if (!string.Equals(confirmation, DeleteConfirmation, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Delete-all requires --confirm {DeleteConfirmation}.");
        }

        TracePaths paths = ResolveTracePaths(args);
        var store = new EncryptedRawTraceStore(paths);
        await store.DeleteAllAsync().ConfigureAwait(false);
        WriteJson(new { deleted = true, root = paths.Root });
        return 0;
    }

    private static int RunPaths()
    {
        TracePaths paths = TracePaths.CreateDefault();
        WriteJson(new
        {
            paths.Root,
            paths.DatabasePath,
            paths.KeyPath,
            paths.TempPath,
            paths.ExportPath,
            paths.CursorsPath,
            paths.SampleManifestPath,
            claudeCorpus = ProviderRoots.Claude,
            codexCorpus = ProviderRoots.Codex,
        });
        return 0;
    }

    private static TracePaths ResolveTracePaths(string[] args)
        => GetOption(args, "--store-root") is { } root ? new TracePaths(root) : TracePaths.CreateDefault();

    private static string? GetOption(string[] args, string name)
    {
        for (int index = 0; index < args.Length; index++)
        {
            if (!string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }
            return args[index + 1];
        }
        return null;
    }

    private static bool HasFlag(string[] args, string name)
        => args.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    private static string? HashSensitiveValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(value.ToUpperInvariant())));

    private static Guid StableGuid(string value)
    {
        byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static ProviderKind ParseRequiredProvider(string? value)
        => ParseOptionalProvider(value) ?? throw new ArgumentException("--provider claude|codex is required.");

    private static ProviderKind? ParseOptionalProvider(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Enum.TryParse(value, ignoreCase: true, out ProviderKind provider))
        {
            return provider;
        }
        throw new ArgumentException("Provider must be claude, codex, or all.");
    }

    private static int ParsePositiveInt(string? value, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }
        if (!int.TryParse(value, out int parsed) || parsed <= 0)
        {
            throw new ArgumentException("Expected a positive integer option value.");
        }
        return parsed;
    }

    private static IBriefRanker ParseBriefRanker(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           string.Equals(value, "deterministic", StringComparison.OrdinalIgnoreCase)
            ? new DeterministicBriefRanker()
            : new CliBriefRanker(value);

    private static bool IsHelp(string value)
        => value is "help" or "--help" or "-h" or "/?";

    private static void WriteJson(object value)
        => Console.WriteLine(JsonSerializer.Serialize(value, JsonOptions));

    private static void WriteHelp()
    {
        Console.WriteLine(
            """
            Octadock Workflow Intelligence — INTERNAL read-only harness

            Commands:
              inventory [--provider all|claude|codex] [--root PATH]
              self-test [--provider all|claude|codex] [--root PATH]
              sample [--count N] [--output PATH] [--store-root PATH]
              tail --provider claude|codex --file PATH [--max-bytes N] [--store-root PATH]
              reconstruct --provider claude|codex --file PATH [--turn-id ID]
              brief --provider claude|codex --file PATH [--turn-id ID] [--force] [--show]
                    [--ranker deterministic|claude|codex]
                    [--store] [--retention-days 7] [--store-root PATH]
              evaluate-briefs [--provider all|claude|codex] [--root PATH] [--max-files 25] [--force]
                    [--ranker deterministic|claude|codex]
              process-hook --provider claude|codex [--force] [--include-subagents]
                    [--ranker deterministic|claude|codex]
                    [--retention-days 7] [--store-root PATH]
              ingest-hook --provider claude|codex [--retention-days 7] [--store-root PATH]
              purge-expired [--store-root PATH]
              delete-all --confirm DELETE_INTERNAL_TRACES [--store-root PATH]
              paths

            Raw-content commands require:
              OCTADOCK_INTERNAL_FULL_CONSENT=I_UNDERSTAND_RAW_CONTENT_IS_CAPTURED

            This project is intentionally excluded from Octadock.sln and installs no hooks automatically.
            """);
    }
}
