using System.IO;
using Octadock.Core.Ai;
using Octadock.Core.Commands;

namespace Octadock.App.Ai;

/// <summary>
/// Builds the one contextual handoff-review command used by every product
/// surface. The stable <see cref="CommandType.AiActions"/> command remains the
/// compatibility boundary; <c>reviewsource</c> and <c>reviewlabel</c> keep the
/// review visibly bound to the item that opened it.
/// </summary>
public static class AgentReviewLaunch
{
    public const string ReviewSourceParameter = "reviewsource";
    public const string ReviewLabelParameter = "reviewlabel";

    public static OctadockCommand FromShelf(
        Guid captureId,
        string fileName,
        string? targetApplication,
        string? workflow)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["captureid"] = captureId.ToString("D"),
            ["title"] = $"{AgentWorkflowCatalog.Resolve(workflow)?.Title ?? "Use with AI"} · {fileName}",
        };
        if (!string.IsNullOrWhiteSpace(workflow))
        {
            parameters["workflow"] = workflow;
        }

        if (!string.IsNullOrWhiteSpace(targetApplication))
        {
            parameters["target"] = targetApplication;
        }

        return Create("shelf", fileName, parameters);
    }

    public static OctadockCommand FromContext(Guid contextId, IEnumerable<Guid> includedItemIds, string packageName)
        => Create("context", packageName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["contextid"] = contextId.ToString("D"),
            ["contextitems"] = string.Join(",", includedItemIds.Select(id => id.ToString("D"))),
            ["workflow"] = "choose",
            ["title"] = $"Use {packageName} with AI",
        });

    public static OctadockCommand FromHistory(Guid captureId, string fileName, string? targetApplication)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["captureid"] = captureId.ToString("D"),
            ["workflow"] = "choose",
            ["title"] = $"Use {fileName} with AI",
        };
        if (!string.IsNullOrWhiteSpace(targetApplication))
        {
            parameters["target"] = targetApplication;
        }

        return Create("history", fileName, parameters);
    }

    public static OctadockCommand FromClipboardText(string text, string sourceLabel)
        => Create("clipboard", sourceLabel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["text"] = text,
            ["source"] = $"Clipboard history · {NormalizeLabel(sourceLabel)}",
            ["workflow"] = "choose",
            ["title"] = "Use clipboard item with AI",
        });

    public static OctadockCommand FromClipboardImage(string filePath, string sourceLabel)
        => Create("clipboard", sourceLabel, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["filepath"] = filePath,
            ["source"] = $"Clipboard history · {NormalizeLabel(sourceLabel)}",
            ["workflow"] = "choose",
            ["title"] = "Use clipboard item with AI",
        });

    public static OctadockCommand FromAutomation(OctadockCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Type != CommandType.AiActions)
        {
            throw new ArgumentException("Only the AI compatibility command can open a handoff review.", nameof(command));
        }

        string label = command.Title
            ?? (string.IsNullOrWhiteSpace(command.FilePath) ? "CLI or protocol" : Path.GetFileName(command.FilePath));
        return Create("automation", label, command.Parameters);
    }

    public static OctadockCommand FromDock()
        => Create("dock", "New reviewed handoff", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static OctadockCommand FromHud()
        => Create("hud", "New reviewed handoff", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static OctadockCommand FromTray()
        => Create("tray", "New reviewed handoff", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static string DisplaySource(string? source)
        => source?.Trim().ToLowerInvariant() switch
        {
            "shelf" => "Shelf",
            "context" => "Context",
            "history" => "History",
            "clipboard" => "Clipboard",
            "dock" => "Dock",
            "hud" => "Capture tools",
            "tray" => "Tray",
            "automation" => "Automation",
            _ => "Octadock",
        };

    public static string NormalizeLabel(string? label)
    {
        string normalized = string.Join(
            " ",
            (label ?? string.Empty).Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= 120 ? normalized : normalized[..119] + "…";
    }

    private static OctadockCommand Create(
        string source,
        string? label,
        IReadOnlyDictionary<string, string> parameters)
    {
        var contextual = new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase)
        {
            [ReviewSourceParameter] = source,
        };
        if (contextual.TryGetValue("title", out string? title))
        {
            contextual["title"] = NormalizeBounded(title, AgentPacketLimits.MaxTitleCharacters);
        }

        string normalizedLabel = NormalizeLabel(label);
        if (!string.IsNullOrWhiteSpace(normalizedLabel))
        {
            contextual[ReviewLabelParameter] = normalizedLabel;
        }

        return OctadockCommand.Create(CommandType.AiActions, contextual);
    }

    private static string NormalizeBounded(string? value, int maxCharacters)
    {
        string normalized = string.Join(
            " ",
            (value ?? string.Empty).Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return normalized.Length <= maxCharacters
            ? normalized
            : normalized[..(maxCharacters - 1)] + "…";
    }
}
