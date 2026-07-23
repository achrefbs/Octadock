using System.Text;
using Octadock.Core.Commands;

namespace Octadock.Cli;

/// <summary>
/// Hand-rolled help for <c>octadock.exe</c>. The verb list is generated from
/// <see cref="CommandTokens.ActiveTokens"/> so it can never drift from the set the
/// parser accepts (removed features keep a parse tombstone but are never advertised);
/// the per-verb parameter hints mirror the automation spec
/// (docs/specs/api-and-data-spec.md). Kept dependency-light on purpose.
/// </summary>
internal static class HelpText
{
    /// <summary>One-line synopsis + parameter hint for every known verb, keyed by canonical token.</summary>
    private static readonly Dictionary<string, VerbHelp> Verbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["capture-area"] = new(
                "Capture a rectangular region; opens the selection overlay when no region is given.",
                "[--area x,y,width,height | --x --y --width --height] [--monitor N] [--units px|dip] [--action copy|save|annotate|shelf|upload|discard]"),
            ["capture-previous-area"] = new(
                "Repeat the most recent area selection.",
                "[--action copy|save|annotate|shelf|upload|discard]"),
            ["capture-fullscreen"] = new(
                "Capture the active monitor (or all monitors).",
                "[--monitor N] [--all-monitors] [--action copy|save|annotate|shelf|upload|discard]"),
            ["capture-window"] = new(
                "Capture an application window; opens the window picker unless --hwnd is supplied.",
                "[--hwnd 0x1234] [--include-shadow] [--action copy|save|annotate|shelf|upload|discard]"),
            ["self-timer"] = new(
                "Start an area capture after a countdown.",
                "[--seconds N] [--action copy|save|annotate|shelf|upload|discard]"),
            ["scrolling-capture"] = new(
                "Capture and stitch a manually scrolled vertical region into one image.",
                "[--x --y --width --height] [--monitor N] [--direction vertical] [--action ...]"),
            ["record-screen"] = new(
                "Toggle MP4 video recording for a monitor or selected area.",
                "[--monitor N | --select-area | --area x,y,width,height]"),
            ["capture-text"] = new(
                "Run local OCR on a region or file and copy the recognized text.",
                "[--filepath <path>] [--area x,y,width,height] [--mode compact|lines|layout] [--language <tag>] [--linebreaks]"),
            ["read"] = new(
                "Read text, a file, clipboard text, or an OCR region aloud.",
                "[--filepath <path> | --clipboard | --text <text> | --area x,y,width,height] [--voice-id <id>] [--model-id <id>] [--stop] [--explain [--provider codex|claude]]"),
            ["ai"] = new(
                "Prepare an explicit, source-bound handoff for review before invoking a local agent CLI.",
                string.Empty),
            ["dictation"] = new(
                "Toggle speech-to-text dictation using the configured provider and insertion mode.",
                string.Empty),
            ["open-history"] = new(
                "Open the local history window.",
                string.Empty),
            ["open-clipboard-history"] = new(
                "Open the local clipboard history window.",
                string.Empty),
            ["open-context"] = new(
                "Open the floating Context Stack window.",
                string.Empty),
            ["open-text-tools"] = new(
                "Open the text-transform toolbox (JSON, Base64, JWT, case, hashes, timestamps).",
                string.Empty),
            ["restore-recently-closed"] = new(
                "Restore the most recently closed shelf item.",
                string.Empty),
            ["clear-history"] = new(
                "Clear local history (after confirmation).",
                string.Empty),
            ["open-settings"] = new(
                "Open settings, optionally on a specific tab.",
                "[--tab general|shortcuts|shelf|capture|recording|ocr|speech|history|clipboard|automation|advanced]"),
            ["quit"] = new(
                "Shut down the running Octadock instance cleanly (local only; octadock:// is blocked).",
                string.Empty),
            ["activate"] = new(
                "Activate a license key on this device (also works as octadock://activate?key=…).",
                "--key OCTA-XXXXX-XXXXX-XXXXX-XXXXX"),
        };

    /// <summary>Friendly CLI aliases that resolve to a canonical verb (kept in sync with CommandParser).</summary>
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ocr"] = "capture-text",
            ["capture-ocr"] = "capture-text",
            ["text"] = "capture-text",
            ["read-aloud"] = "read",
            ["ask-ai"] = "ai",
            ["ai-actions"] = "ai",
            ["agent"] = "ai",
            ["agent-workspace"] = "ai",
            ["handoff"] = "ai",
            ["explain"] = "ai",
            ["summarize"] = "ai",
            ["dictate"] = "dictation",
            ["speech"] = "dictation",
            ["settings"] = "open-settings",
            ["record"] = "record-screen",
            ["recording"] = "record-screen",
            ["history"] = "open-history",
            ["clipboard"] = "open-clipboard-history",
            ["clipboard-history"] = "open-clipboard-history",
            ["clips"] = "open-clipboard-history",
            ["context"] = "open-context",
            ["context-stack"] = "open-context",
            ["text-tools"] = "open-text-tools",
            ["transforms"] = "open-text-tools",
            ["exit"] = "quit",
            ["shutdown"] = "quit",
            ["fullscreen"] = "capture-fullscreen",
            ["window"] = "capture-window",
            ["area"] = "capture-area",
        };

    /// <summary>The full top-level help screen.</summary>
    public static string Root()
    {
        var sb = new StringBuilder();
        sb.AppendLine("octadock - Octadock automation CLI");
        sb.AppendLine();
        sb.AppendLine("Forwards a command to the running Octadock tray app over a per-user named");
        sb.AppendLine("pipe, launching Octadock first if it is not already running.");
        sb.AppendLine();
        sb.AppendLine("USAGE:");
        sb.AppendLine("  octadock <command> [options]");
        sb.AppendLine("  octadock \"octadock://<command>?<query>\"      (protocol activation)");
        sb.AppendLine();
        sb.AppendLine("GLOBAL OPTIONS:");
        sb.AppendLine("  --json               Emit machine-readable JSON for the result or error.");
        sb.AppendLine("  --timeout <seconds>  Global timeout for the round-trip (default 15).");
        sb.AppendLine("  --no-launch          Fail instead of starting Octadock if it is not running.");
        sb.AppendLine("  --version            Print the CLI version.");
        sb.AppendLine("  -h, --help           Show this help, or help for a specific command.");
        sb.AppendLine();
        sb.AppendLine("COMMANDS:");

        int width = CommandTokens.ActiveTokens.Max(t => t.Length);
        foreach (string token in CommandTokens.ActiveTokens.OrderBy(t => t, StringComparer.Ordinal))
        {
            string summary = Verbs.TryGetValue(token, out VerbHelp help) ? help.Summary : string.Empty;
            sb.Append("  ").Append(token.PadRight(width + 2)).AppendLine(summary);
        }

        sb.AppendLine();
        sb.AppendLine("EXIT CODES:");
        sb.AppendLine("  0  Command accepted and dispatched.");
        sb.AppendLine("  1  Runtime/IPC error (timeout, broken pipe, app not found).");
        sb.AppendLine("  2  Parse error (unknown command, malformed option).");
        sb.AppendLine();
        sb.AppendLine("Run 'octadock <command> --help' for command-specific options.");
        sb.Append("Aliases: ").Append(string.Join(", ", Aliases.Keys.OrderBy(k => k, StringComparer.Ordinal)));
        return sb.ToString();
    }

    /// <summary>Help for a single verb (or alias). Returns <c>null</c> when the verb is unknown.</summary>
    public static string? ForCommand(string verb)
    {
        string canonical = verb.TrimStart('-').ToLowerInvariant();
        if (Aliases.TryGetValue(canonical, out string? mapped))
        {
            canonical = mapped;
        }

        if (!Verbs.TryGetValue(canonical, out VerbHelp help))
        {
            return null;
        }

        var sb = new StringBuilder();
        sb.Append("octadock ").AppendLine(canonical);
        sb.AppendLine();
        sb.AppendLine(help.Summary);
        sb.AppendLine();
        sb.AppendLine("USAGE:");
        sb.Append("  octadock ").Append(canonical);
        if (!string.IsNullOrEmpty(help.Parameters))
        {
            sb.Append(' ').Append(help.Parameters);
        }

        sb.AppendLine();
        sb.AppendLine();
        if (canonical == "dictation")
        {
            sb.AppendLine("Uses the Speech settings provider/model/language/insertion mode.");
            sb.AppendLine("octadock:// URLs are blocked so websites cannot start the microphone.");
        }
        else if (canonical == "ai")
        {
            sb.AppendLine("Opens the same reviewed handoff used by Shelf, Context, History, and Clipboard.");
            sb.AppendLine("Attach evidence, choose an outcome, inspect the exact redacted packet, then confirm");
            sb.AppendLine("the named read-only Codex or Claude CLI destination. Nothing is sent on open.");
            sb.AppendLine("Results are ephemeral unless you copy them. Octadock stores no API key or prompt history.");
        }
        else if (canonical == "read")
        {
            sb.AppendLine("Verbatim reads use the configured TTS provider; Windows voices work");
            sb.AppendLine("locally with no key. --explain is not exposed yet; the trusted-summary engine");
            sb.AppendLine("remains an internal prototype. ElevenLabs is opt-in via settings or");
            sb.AppendLine("OCTADOCK_ELEVENLABS_API_KEY / ELEVENLABS_API_KEY.");
            sb.AppendLine("octadock:// URLs are blocked so websites cannot trigger AI/TTS reads.");
        }
        else if (canonical == "activate")
        {
            sb.AppendLine("Sends the key + this device's machine hash to the Octadock license");
            sb.AppendLine("service, then verifies the returned entitlement locally. Unlike other");
            sb.AppendLine("automation, octadock://activate works even when protocol automation is off.");
        }
        else
        {
            sb.AppendLine("Regions accept physical pixels by default; pass --units dip for");
            sb.AppendLine("WPF device-independent pixels. Add --json for machine-readable output.");
        }

        return sb.ToString();
    }

    private readonly record struct VerbHelp(string Summary, string Parameters);
}
