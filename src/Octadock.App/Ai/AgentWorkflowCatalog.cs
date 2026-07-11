namespace Octadock.App.Ai;

/// <summary>
/// Outcome-first workflows that make Octadock's collected evidence useful.
/// These are intentionally not model verbs such as summarize or explain: each
/// workflow represents a job the user can complete from a capture, pin, Context,
/// clipboard item, history item, or dictated intent.
/// </summary>
public sealed record AgentWorkflowDefinition(
    string Key,
    string Title,
    string Description,
    string Goal,
    IReadOnlyList<string> AcceptanceCriteria,
    bool IsVisualVerification = false)
{
    public string CriteriaText => string.Join(Environment.NewLine, AcceptanceCriteria);
}

public static class AgentWorkflowCatalog
{
    public const string BuildKey = "build";
    public const string InvestigateKey = "investigate";
    public const string VerifyKey = "verify";
    public const string ExtractKey = "extract";
    public const string HandoffKey = "handoff";

    public static IReadOnlyList<AgentWorkflowDefinition> All { get; } =
    [
        new(
            BuildKey,
            "Build from this",
            "Turn what is on screen into an implementation-ready task.",
            "Use the supplied Octadock evidence to create an implementation-ready build task. Describe the required behavior, states, edge cases, and smallest safe implementation path. Infer only what the evidence supports and label every assumption.",
            [
                "Every visible or stated requirement maps to a concrete implementation change.",
                "Unknowns and assumptions are explicit rather than invented.",
                "The plan includes functional tests and visual checks where applicable.",
                "Unrelated existing behavior remains unchanged.",
            ]),
        new(
            InvestigateKey,
            "Investigate an issue",
            "Find the likely cause, the evidence gap, and the smallest safe fix.",
            "Investigate the supplied Octadock evidence as a real defect. Separate observations from hypotheses, identify the most likely underlying cause, name any evidence still needed, propose the smallest safe fix, and give a concrete verification plan.",
            [
                "Observed facts are separated from hypotheses.",
                "The most likely root cause is tied to specific evidence.",
                "The proposed fix is scoped and preserves unrelated behavior.",
                "The response includes reproduction and verification steps.",
            ]),
        new(
            VerifyKey,
            "Verify a result",
            "Compare before and after, then decide whether the result is acceptable.",
            "Verify the supplied baseline and result evidence. Identify material visual or behavioral differences, distinguish intended changes from regressions, evaluate the result against the stated intent, and give a clear pass, fail, or needs-review conclusion.",
            [
                "Changed regions and their impact are identified precisely.",
                "Intended changes are distinguished from likely regressions.",
                "The conclusion is pass, fail, or needs review with concrete evidence.",
                "Any unverified requirement is reported explicitly.",
            ],
            IsVisualVerification: true),
        new(
            ExtractKey,
            "Extract usable data",
            "Turn pixels or copied content into clean structured output.",
            "Extract the useful information from the supplied Octadock evidence into a clean, reusable structure. Preserve exact values, mark unreadable or ambiguous fields, infer no missing data, and propose the most useful output shape such as a table, CSV, JSON, or checklist.",
            [
                "Extracted values remain faithful to the source evidence.",
                "Unreadable, missing, and ambiguous values are clearly marked.",
                "The output uses a consistent reusable structure.",
                "No unsupported facts are introduced.",
            ]),
        new(
            HandoffKey,
            "Prepare a handoff",
            "Package the context, decisions, unknowns, and next action for someone else.",
            "Turn the supplied Octadock evidence into a concise handoff that another person or capable agent can act on without reconstructing the situation. Include the objective, relevant context, constraints, decisions already made, open questions, and the next concrete action.",
            [
                "The objective and current state are immediately clear.",
                "Relevant evidence, constraints, and decisions are preserved.",
                "Open questions and risks are explicit.",
                "The handoff ends with one concrete next action and a definition of done.",
            ]),
    ];

    public static AgentWorkflowDefinition? Resolve(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        string normalized = key.Trim().ToLowerInvariant();
        normalized = normalized switch
        {
            "implement" or "implementation" or "code" => BuildKey,
            "debug" or "diagnose" or "explain" => InvestigateKey,
            "compare" or "before-after" or "visual" => VerifyKey,
            "ocr" or "table" or "structured-data" => ExtractKey,
            "brief" or "ticket" or "agent-task" or "summary" or "summarize" => HandoffKey,
            _ => normalized,
        };

        return All.FirstOrDefault(item => string.Equals(item.Key, normalized, StringComparison.Ordinal));
    }
}
