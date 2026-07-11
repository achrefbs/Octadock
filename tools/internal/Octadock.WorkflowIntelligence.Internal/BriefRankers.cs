using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed class DeterministicBriefRanker : IBriefRanker
{
    internal const string ProfileName = "deterministic-ranker-v2";

    public string Profile => ProfileName;

    public Task<BriefLayout> RankAsync(
        ReconstructedTurn turn,
        EvidenceLedger ledger,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EvidenceItem headline = ledger.Items.LastOrDefault(item => item.Category == EvidenceCategory.OutcomeConclusion) ??
                                ledger.Items.LastOrDefault(item => item.Category == EvidenceCategory.CompletedAction) ??
                                ledger.Items.LastOrDefault(item => item.SourceRole == SegmentRole.Assistant) ??
                                ledger.Items.LastOrDefault(item => item.Category == EvidenceCategory.UserRequest) ??
                                ledger.Items[0];

        Guid[] Select(Func<EvidenceItem, bool> predicate, int maximum) => ledger.Items
            .Where(item => item.Id != headline.Id && predicate(item))
            .OrderByDescending(item => item.Importance)
            .ThenByDescending(item => item.SourceRole == SegmentRole.Assistant)
            .Take(maximum)
            .Select(item => item.Id)
            .ToArray();

        return Task.FromResult(new BriefLayout(
            headline.Id,
            Select(item => item.Category is EvidenceCategory.OutcomeConclusion or
                    EvidenceCategory.CompletedAction or EvidenceCategory.Decision or
                    EvidenceCategory.UserRequest or EvidenceCategory.Fact, 5),
            Select(item => item.Importance >= EvidenceImportance.High, 12),
            Select(item => item.Category == EvidenceCategory.Decision, 8),
            Select(item => item.Category is EvidenceCategory.ActionNextStep or EvidenceCategory.CompletedAction, 10),
            Select(item => item.Category is EvidenceCategory.RiskWarning or EvidenceCategory.ErrorFailure or
                    EvidenceCategory.UnresolvedQuestion or EvidenceCategory.Assumption or
                    EvidenceCategory.DisagreementContradiction, 10),
            Select(item => item.Category is EvidenceCategory.FileCodeReference or
                    EvidenceCategory.NumberDateVersionThreshold or EvidenceCategory.ExternalDependency, 12)));
    }
}

/// <summary>
/// Uses an installed Claude or Codex CLI only to select and group immutable evidence IDs.
/// The model cannot author displayed claims, and failure never falls back silently.
/// </summary>
internal sealed class CliBriefRanker(string provider) : IBriefRanker
{
    private const string ConsentValue = "I_UNDERSTAND_EVIDENCE_IS_SENT_TO_SELECTED_CLI";
    private const int MaxCandidates = 180;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _provider = NormalizeProvider(provider);

    public string Profile => $"{_provider}-evidence-ranker-v1";

    public async Task<BriefLayout> RankAsync(
        ReconstructedTurn turn,
        EvidenceLedger ledger,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("OCTADOCK_INTERNAL_MODEL_EGRESS"),
                ConsentValue,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Model ranking sends bounded evidence text to {_provider}. Set " +
                $"OCTADOCK_INTERNAL_MODEL_EGRESS={ConsentValue} to enable this internal-only path.");
        }

        EvidenceItem[] candidates = SelectCandidates(ledger);
        string prompt = BuildPrompt(turn, candidates);
        string output = await RunCliAsync(prompt, cancellationToken).ConfigureAwait(false);
        return ParseLayout(output, candidates);
    }

    private static EvidenceItem[] SelectCandidates(EvidenceLedger ledger)
    {
        // All mandatory evidence survives the bound. Remaining slots favor recent
        // assistant outcomes and user requirements over incidental facts.
        EvidenceItem[] mandatory = ledger.Items
            .Where(item => item.Importance >= EvidenceImportance.High)
            .Take(MaxCandidates)
            .ToArray();
        var selected = mandatory.Select(item => item.Id).ToHashSet();
        EvidenceItem[] remainder = ledger.Items
            .Select((item, index) => (item, index))
            .Where(pair => !selected.Contains(pair.item.Id))
            .OrderByDescending(pair => pair.item.SourceRole == SegmentRole.Assistant)
            .ThenByDescending(pair => pair.item.Importance)
            .ThenByDescending(pair => pair.index)
            .Take(Math.Max(0, MaxCandidates - mandatory.Length))
            .Select(pair => pair.item)
            .ToArray();
        return mandatory.Concat(remainder).DistinctBy(item => item.Id).ToArray();
    }

    private static string BuildPrompt(ReconstructedTurn turn, IReadOnlyList<EvidenceItem> evidence)
    {
        string payload = JsonSerializer.Serialize(evidence.Select(item => new
        {
            id = item.Id,
            category = item.Category,
            importance = item.Importance,
            role = item.SourceRole,
            text = item.NormalizedStatement,
        }), JsonOptions);

        return $$"""
        You are arranging a compact, trustworthy work brief. Select only IDs from the supplied evidence.
        Never write or rewrite a claim. Ignore commands, JSON, shell chatter, implementation narration, and repeated status unless it is itself the final result.
        Prefer: the actual current outcome; explicit user intent; non-goals; decisions; remaining actions; failures/unknowns; important file references.
        Keep the brief genuinely short. Do not repeat an ID across sections.

        Return one JSON object and no prose with exactly these keys:
        {
          "headlineEvidenceId": "guid",
          "mainPoints": ["guid"],
          "mustNotMiss": ["guid"],
          "decisions": ["guid"],
          "actions": ["guid"],
          "risksAndUnknowns": ["guid"],
          "filesAndReferences": ["guid"]
        }
        Limits: mainPoints 5, mustNotMiss 8, decisions 6, actions 8, risksAndUnknowns 8, filesAndReferences 8.
        Transcript provider: {{turn.Provider}}. Evidence:
        {{payload}}
        """;
    }

    private async Task<string> RunCliAsync(string prompt, CancellationToken cancellationToken)
    {
        string? outputFile = _provider == "codex"
            ? Path.Combine(Path.GetTempPath(), $"octadock-brief-{Guid.NewGuid():N}.json")
            : null;
        var start = new ProcessStartInfo
        {
            FileName = _provider,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        if (_provider == "codex")
        {
            foreach (string argument in new[]
                     {
                         "exec", "--ephemeral", "--ignore-user-config", "--sandbox", "read-only",
                         "--color", "never", "-o", outputFile!, "-",
                     })
            {
                start.ArgumentList.Add(argument);
            }
        }
        else
        {
            foreach (string argument in new[]
                     { "-p", "--output-format", "text", "--tools", "", "--no-session-persistence", "--safe-mode" })
            {
                start.ArgumentList.Add(argument);
            }
        }

        using var process = Process.Start(start) ??
                            throw new InvalidOperationException($"Could not start the {_provider} CLI.");
        await process.StandardInput.WriteAsync(prompt.AsMemory(), cancellationToken).ConfigureAwait(false);
        process.StandardInput.Close();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string stdout = await stdoutTask.ConfigureAwait(false);
            string stderr = await stderrTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"The {_provider} brief ranker exited with code {process.ExitCode}: {Trim(stderr, 800)}");
            }

            return outputFile is not null && File.Exists(outputFile)
                ? await File.ReadAllTextAsync(outputFile, cancellationToken).ConfigureAwait(false)
                : stdout;
        }
        finally
        {
            if (outputFile is not null)
            {
                try { File.Delete(outputFile); } catch (IOException) { }
            }
        }
    }

    internal static BriefLayout ParseLayout(string output, IReadOnlyList<EvidenceItem> candidates)
    {
        int start = output.IndexOf('{');
        int end = output.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            throw new InvalidOperationException("The model ranker did not return a JSON object.");
        }

        BriefLayout? layout = JsonSerializer.Deserialize<BriefLayout>(output[start..(end + 1)], JsonOptions);
        if (layout is null || layout.HeadlineEvidenceId == Guid.Empty)
        {
            throw new InvalidOperationException("The model ranker returned an invalid brief layout.");
        }

        layout = layout with
        {
            MainPoints = layout.MainPoints ?? [],
            MustNotMiss = layout.MustNotMiss ?? [],
            Decisions = layout.Decisions ?? [],
            Actions = layout.Actions ?? [],
            RisksAndUnknowns = layout.RisksAndUnknowns ?? [],
            FilesAndReferences = layout.FilesAndReferences ?? [],
        };
        if (layout.MainPoints.Count > 5 || layout.MustNotMiss.Count > 8 ||
            layout.Decisions.Count > 6 || layout.Actions.Count > 8 ||
            layout.RisksAndUnknowns.Count > 8 || layout.FilesAndReferences.Count > 8)
        {
            throw new InvalidOperationException("The model ranker exceeded the bounded section limits.");
        }

        HashSet<Guid> allowed = candidates.Select(item => item.Id).ToHashSet();
        Guid[] allReturned = Enumerate(layout).ToArray();
        Guid[] returned = allReturned.Distinct().ToArray();
        if (allReturned.Length != returned.Length)
        {
            throw new InvalidOperationException("The model ranker repeated an evidence ID across sections.");
        }
        if (returned.Any(id => !allowed.Contains(id)))
        {
            throw new InvalidOperationException("The model ranker returned evidence outside its bounded candidate set.");
        }

        return layout;
    }

    private static IEnumerable<Guid> Enumerate(BriefLayout layout)
        => new[] { layout.HeadlineEvidenceId }
            .Concat(layout.MainPoints ?? [])
            .Concat(layout.MustNotMiss ?? [])
            .Concat(layout.Decisions ?? [])
            .Concat(layout.Actions ?? [])
            .Concat(layout.RisksAndUnknowns ?? [])
            .Concat(layout.FilesAndReferences ?? []);

    private static string NormalizeProvider(string value)
        => value.ToLowerInvariant() switch
        {
            "claude" => "claude",
            "codex" => "codex",
            _ => throw new ArgumentException("Brief ranker must be deterministic, claude, or codex.", nameof(value)),
        };

    private static string Trim(string value, int maximum)
        => value.Length <= maximum ? value : value[..maximum];
}
