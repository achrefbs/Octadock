using System.Globalization;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;

namespace Octadock.Core.Services;

/// <summary>
/// Default <see cref="ICommandParser"/>. Parses <c>octadock://verb?query</c>
/// protocol URIs and CLI argument arrays into a transport-agnostic
/// <see cref="OctadockCommand"/>. Verb tokens come from
/// <see cref="CommandTokens"/> with a small set of friendly CLI aliases
/// (for example <c>ocr</c> → <see cref="CommandType.CaptureText"/> and
/// <c>settings</c> → <see cref="CommandType.OpenSettings"/>).
/// </summary>
public sealed class CommandParser : ICommandParser
{
    // Numeric region keys that must parse as integers when present.
    private static readonly string[] NumericKeys = ["x", "y", "width", "height"];

    private static readonly HashSet<string> ValueOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "area",
            "captureid",
            "contextid",
            "criteria",
            "cwd",
            "direction",
            "filename",
            "filepath",
            "height",
            "goal",
            "key",
            "language",
            "length",
            "maxedge",
            "maxstitchededge",
            "message",
            "mode",
            "model",
            "model-id",
            "modelid",
            "monitor",
            "notify",
            "preset",
            "provider",
            "project",
            "source",
            "style",
            "tab",
            "text",
            "title",
            "target",
            "type",
            "workflow",
            "units",
            "voice",
            "voice-id",
            "voiceid",
            "width",
            "window",
            "windowhandle",
            "environment",
            "x",
            "y",
        };

    // Friendly CLI/URI aliases in addition to the canonical kebab tokens.
    private static readonly Dictionary<string, CommandType> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["ocr"] = CommandType.CaptureText,
            ["capture-ocr"] = CommandType.CaptureText,
            ["text"] = CommandType.CaptureText,
            ["read-aloud"] = CommandType.ReadAloud,
            ["ask-ai"] = CommandType.AiActions,
            ["ai-actions"] = CommandType.AiActions,
            ["agent"] = CommandType.AiActions,
            ["agent-workspace"] = CommandType.AiActions,
            ["handoff"] = CommandType.AiActions,
            ["explain"] = CommandType.AiActions,
            ["summarize"] = CommandType.AiActions,
            ["dictate"] = CommandType.Dictation,
            ["speech"] = CommandType.Dictation,
            ["settings"] = CommandType.OpenSettings,
            ["record"] = CommandType.RecordScreen,
            ["recording"] = CommandType.RecordScreen,
            ["history"] = CommandType.OpenHistory,
            ["clipboard"] = CommandType.OpenClipboardHistory,
            ["clipboard-history"] = CommandType.OpenClipboardHistory,
            ["clips"] = CommandType.OpenClipboardHistory,
            ["context"] = CommandType.OpenContext,
            ["context-stack"] = CommandType.OpenContext,
            ["text-tools"] = CommandType.OpenTextTools,
            ["transforms"] = CommandType.OpenTextTools,
            ["exit"] = CommandType.Quit,
            ["shutdown"] = CommandType.Quit,
            ["annotate"] = CommandType.OpenAnnotate,
            ["edit"] = CommandType.OpenAnnotate,
            ["shelf"] = CommandType.AddShelfItem,
            ["fullscreen"] = CommandType.CaptureFullscreen,
            ["window"] = CommandType.CaptureWindow,
            ["area"] = CommandType.CaptureArea,
            ["all"] = CommandType.AllInOne,
            ["allinone"] = CommandType.AllInOne,
        };

    /// <inheritdoc />
    public CommandParseResult ParseUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            return CommandParseResult.Fail("No URI supplied.");
        }

        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out Uri? parsed))
        {
            return CommandParseResult.Fail($"'{uri}' is not a valid absolute URI.");
        }

        if (!string.Equals(parsed.Scheme, CommandTokens.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return CommandParseResult.Fail(
                $"Unsupported URI scheme '{parsed.Scheme}://'. Expected '{CommandTokens.Scheme}://'.");
        }

        // The verb is the authority (octadock://verb) or, when the authority is
        // empty (octadock:///verb), the first path segment.
        string verb = parsed.Authority;
        if (string.IsNullOrEmpty(verb))
        {
            verb = parsed.AbsolutePath.Trim('/');
        }

        verb = Uri.UnescapeDataString(verb).Trim().Trim('/');

        CommandType type = ResolveVerb(verb);
        if (type == CommandType.Unknown)
        {
            return UnknownVerb(verb);
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SeedAliasImplications(verb, parameters);
        string query = parsed.Query;
        if (query.Length > 1)
        {
            foreach (string pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=', StringComparison.Ordinal);
                if (eq < 0)
                {
                    // Bare flag, e.g. ?silent -> silent=true.
                    string flag = Uri.UnescapeDataString(pair).Trim().ToLowerInvariant();
                    if (flag.Length > 0)
                    {
                        parameters[flag] = "true";
                    }

                    continue;
                }

                string key = Uri.UnescapeDataString(pair[..eq]).Trim().ToLowerInvariant();
                string value = Uri.UnescapeDataString(pair[(eq + 1)..]);
                if (key.Length == 0)
                {
                    continue;
                }

                if (string.Equals(key, "area", StringComparison.Ordinal))
                {
                    if (!TryExpandArea(value, parameters, out string? areaError))
                    {
                        return CommandParseResult.Fail(areaError!);
                    }

                    continue;
                }

                parameters[key] = value;
            }
        }

        return Finalize(type, parameters);
    }

    /// <inheritdoc />
    public CommandParseResult ParseArguments(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count == 0)
        {
            return CommandParseResult.Fail(
                $"No command supplied. Expected one of: {string.Join(", ", SortedTokens())}.");
        }

        string verb = arguments[0].Trim();

        // Tolerate a full octadock:// URI passed as the first CLI argument.
        if (verb.StartsWith(CommandTokens.Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            return ParseUri(verb);
        }

        verb = verb.TrimStart('-');
        CommandType type = ResolveVerb(verb);
        if (type == CommandType.Unknown)
        {
            return UnknownVerb(verb);
        }

        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        SeedAliasImplications(verb, parameters);
        for (int i = 1; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (!arg.StartsWith("--", StringComparison.Ordinal) && !arg.StartsWith('/'))
            {
                return CommandParseResult.Fail(
                    $"Unexpected argument '{arg}'. Options must be written as --key value or --key=value.");
            }

            string token = arg.StartsWith("--", StringComparison.Ordinal) ? arg[2..] : arg[1..];

            string key;
            string? value;
            int eq = token.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                key = token[..eq];
                value = token[(eq + 1)..];
            }
            else
            {
                key = token.Trim().ToLowerInvariant();

                // Peek at the next argument: if it is not another option, consume
                // it as this key's value; otherwise treat the key as a boolean flag.
                if (ShouldConsumeValue(key))
                {
                    if (i + 1 >= arguments.Count || IsOptionToken(arguments[i + 1]))
                    {
                        return CommandParseResult.Fail($"Option '--{key}' requires a value.");
                    }

                    value = arguments[++i];
                }
                else
                {
                    value = "true";
                }
            }

            key = key.Trim().ToLowerInvariant();
            if (key.Length == 0)
            {
                return CommandParseResult.Fail($"Malformed option '{arg}'.");
            }

            if (string.Equals(key, "area", StringComparison.Ordinal))
            {
                if (!TryExpandArea(value, parameters, out string? areaError))
                {
                    return CommandParseResult.Fail(areaError!);
                }

                continue;
            }

            parameters[key] = value;
        }

        return Finalize(type, parameters);
    }

    // Only known value-taking options consume the following token. Every other
    // key is a strict boolean flag, so a stray positional after an unknown flag
    // (e.g. "--silent C:\img.png") raises a loud "Unexpected argument" error
    // instead of being silently swallowed as the flag's value.
    private static bool ShouldConsumeValue(string key)
        => ValueOptions.Contains(key);

    private static bool IsOptionToken(string value)
        => value.StartsWith("--", StringComparison.Ordinal);


    /// <summary>
    /// The historical "explain"/"summarize" aliases now open Agent Workspace
    /// with an editable goal. Seeded before option parsing so existing automation
    /// remains valid while the visible commodity action picker stays retired.
    /// </summary>
    private static void SeedAliasImplications(string verb, Dictionary<string, string> parameters)
    {
        if (string.Equals(verb, "explain", StringComparison.OrdinalIgnoreCase))
        {
            parameters["action"] = "explain";
        }
        else if (string.Equals(verb, "summarize", StringComparison.OrdinalIgnoreCase))
        {
            parameters["action"] = "summarize";
        }
    }

    private static CommandType ResolveVerb(string verb)
    {
        if (string.IsNullOrWhiteSpace(verb))
        {
            return CommandType.Unknown;
        }

        CommandType type = CommandTokens.FromToken(verb);
        if (type != CommandType.Unknown)
        {
            return type;
        }

        return Aliases.TryGetValue(verb, out CommandType aliased) ? aliased : CommandType.Unknown;
    }

    private static CommandParseResult Finalize(
        CommandType type,
        Dictionary<string, string> parameters)
    {
        // The 'open' verb previews a file, so a filepath is mandatory. The
        // '--filepath' option already consumes its value via the ValueOptions
        // whitelist (shared with add-shelf-item), so a plain "open C:\data.csv"
        // is rejected upstream as an unexpected positional — the value must be
        // passed as --filepath.
        if (type == CommandType.Open &&
            (!parameters.TryGetValue("filepath", out string? openPath) || string.IsNullOrWhiteSpace(openPath)))
        {
            return CommandParseResult.Fail("Command 'open' requires a 'filepath' parameter.");
        }

        if (type == CommandType.AiActions)
        {
            foreach (string idKey in new[] { "captureid", "contextid" })
            {
                if (parameters.TryGetValue(idKey, out string? rawId) &&
                    !Guid.TryParse(rawId, out _))
                {
                    return CommandParseResult.Fail($"Parameter '{idKey}' must be a valid GUID.");
                }
            }

            if (parameters.TryGetValue("provider", out string? provider) &&
                !string.Equals(provider, "codex", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(provider, "claude", StringComparison.OrdinalIgnoreCase))
            {
                return CommandParseResult.Fail("Agent provider must be 'codex' or 'claude'.");
            }

            if (parameters.TryGetValue("action", out string? action) &&
                action.Trim().ToLowerInvariant() is not
                    ("explain" or "summarize" or "summary" or "clean" or "rewrite" or "clean-rewrite" or
                     "actions" or "action-items" or "extract-action-items" or "tasks"))
            {
                return CommandParseResult.Fail(
                    "Legacy AI action must be explain, summarize, clean-rewrite, or action-items. Prefer --goal for Agent Workspace.");
            }

            if (parameters.TryGetValue("workflow", out string? workflow) &&
                workflow.Trim().ToLowerInvariant() is not
                    ("build" or "investigate" or "verify" or "extract" or "handoff"))
            {
                return CommandParseResult.Fail(
                    "Agent workflow must be build, investigate, verify, extract, or handoff.");
            }
        }

        // Validate numeric region keys.
        foreach (string key in NumericKeys)
        {
            if (parameters.TryGetValue(key, out string? raw) &&
                !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                return CommandParseResult.Fail(
                    $"Parameter '{key}' must be an integer but was '{raw}'.");
            }
        }

        // A region needs positive extents; x/y may be negative on a secondary
        // monitor, but width/height <= 0 would produce a degenerate capture rect.
        foreach (string key in (string[])["width", "height"])
        {
            if (parameters.TryGetValue(key, out string? raw) &&
                int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) &&
                n < 1)
            {
                return CommandParseResult.Fail(
                    $"Parameter '{key}' must be a positive integer but was '{raw}'.");
            }
        }

        return CommandParseResult.Ok(OctadockCommand.Create(type, parameters));
    }

    private static bool TryExpandArea(
        string? value,
        IDictionary<string, string> parameters,
        out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Option 'area' requires four comma-separated integers: x,y,width,height.";
            return false;
        }

        string[] parts = value.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4)
        {
            error = $"Option 'area' must be 'x,y,width,height' but was '{value}'.";
            return false;
        }

        string[] keys = ["x", "y", "width", "height"];
        for (int i = 0; i < 4; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n))
            {
                error = $"Option 'area' component '{parts[i]}' is not a valid integer.";
                return false;
            }

            parameters[keys[i]] = n.ToString(CultureInfo.InvariantCulture);
        }

        return true;
    }

    private static CommandParseResult UnknownVerb(string verb)
        => CommandParseResult.Fail(
            $"Unknown command '{verb}'. Expected one of: {string.Join(", ", SortedTokens())}.");

    private static IEnumerable<string> SortedTokens()
        => CommandTokens.AllTokens.OrderBy(t => t, StringComparer.Ordinal);
}
