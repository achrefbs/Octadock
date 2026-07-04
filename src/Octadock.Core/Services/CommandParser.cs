using System.Globalization;
using System.Text;
using System.Text.Json;
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
    private static readonly string[] NumericKeys = ["x", "y", "width", "height", "pid"];

    private static readonly HashSet<string> ValueOptions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "action",
            "area",
            "captureid",
            "command",
            "cwd",
            "direction",
            "event",
            "event-type",
            "eventtype",
            "exit-code",
            "exitcode",
            "filename",
            "filepath",
            "height",
            "language",
            "length",
            "maxedge",
            "maxstitchededge",
            "message",
            "metadata",
            "metadata-json",
            "metadatajson",
            "mode",
            "model",
            "model-id",
            "modelid",
            "monitor",
            "notify",
            "pid",
            "preset",
            "provider",
            "session-id",
            "sessionid",
            "source",
            "status",
            "style",
            "tab",
            "text",
            "title",
            "type",
            "units",
            "voice",
            "voice-id",
            "voiceid",
            "width",
            "window",
            "windowhandle",
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
            ["explain"] = CommandType.ReadAloud,
            ["summarize"] = CommandType.ReadAloud,
            ["dictate"] = CommandType.Dictation,
            ["speech"] = CommandType.Dictation,
            ["settings"] = CommandType.OpenSettings,
            ["ai"] = CommandType.OpenAiSessions,
            ["ai-sessions"] = CommandType.OpenAiSessions,
            ["sessions"] = CommandType.OpenAiSessions,
            ["ai-event"] = CommandType.AiSessionEvent,
            ["session-event"] = CommandType.AiSessionEvent,
            ["record"] = CommandType.RecordScreen,
            ["recording"] = CommandType.RecordScreen,
            ["history"] = CommandType.OpenHistory,
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
        for (int i = 1; i < arguments.Count; i++)
        {
            string arg = arguments[i];
            if (string.IsNullOrWhiteSpace(arg))
            {
                continue;
            }

            if (type == CommandType.Run && string.Equals(arg, "--", StringComparison.Ordinal))
            {
                string[] commandTokens = arguments.Skip(i + 1).ToArray();
                if (commandTokens.Length == 0 || commandTokens.All(string.IsNullOrWhiteSpace))
                {
                    return CommandParseResult.Fail("Command 'run' requires '--' followed by a command to watch.");
                }

                parameters["command"] = JoinCommandTokens(commandTokens);
                parameters["argv"] = JsonSerializer.Serialize(commandTokens);
                break;
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

        if (type == CommandType.Run &&
            (!parameters.TryGetValue("command", out string? watchedCommand) || string.IsNullOrWhiteSpace(watchedCommand)))
        {
            return CommandParseResult.Fail("Command 'run' requires '--' followed by a command to watch.");
        }

        if (type == CommandType.Watch &&
            (!parameters.TryGetValue("pid", out string? watchedPid) || string.IsNullOrWhiteSpace(watchedPid)))
        {
            return CommandParseResult.Fail("Command 'watch' requires a 'pid' parameter.");
        }

        if (type == CommandType.AiSessionEvent)
        {
            if (!TryGetParameter(parameters, out string? sessionId, "session-id", "sessionid") ||
                string.IsNullOrWhiteSpace(sessionId))
            {
                return CommandParseResult.Fail("Command 'ai-session-event' requires a 'session-id' parameter.");
            }

            if (!Guid.TryParse(sessionId, out _))
            {
                return CommandParseResult.Fail($"Parameter 'session-id' must be a GUID but was '{sessionId}'.");
            }

            if (!TryGetParameter(parameters, out string? eventType, "event", "event-type", "eventtype", "type") ||
                string.IsNullOrWhiteSpace(eventType))
            {
                return CommandParseResult.Fail("Command 'ai-session-event' requires an 'event' parameter.");
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

        if (parameters.TryGetValue("pid", out string? rawPid) &&
            int.TryParse(rawPid, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid) &&
            pid < 1)
        {
            return CommandParseResult.Fail(
                $"Parameter 'pid' must be a positive integer but was '{rawPid}'.");
        }

        return CommandParseResult.Ok(OctadockCommand.Create(type, parameters));
    }

    private static bool TryGetParameter(
        Dictionary<string, string> parameters,
        out string? value,
        params string[] keys)
    {
        foreach (string key in keys)
        {
            if (parameters.TryGetValue(key, out value))
            {
                return true;
            }
        }

        value = null;
        return false;
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

    private static string JoinCommandTokens(IReadOnlyList<string> tokens)
        => string.Join(" ", tokens.Select(QuoteCommandToken));

    private static string QuoteCommandToken(string token)
    {
        if (token.Length == 0)
        {
            return "\"\"";
        }

        if (!token.Any(static c => char.IsWhiteSpace(c) || c == '"'))
        {
            return token;
        }

        var sb = new StringBuilder(token.Length + 2);
        sb.Append('"');
        int backslashes = 0;
        foreach (char c in token)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            if (c == '"')
            {
                sb.Append('\\', backslashes * 2 + 1);
                sb.Append('"');
                backslashes = 0;
                continue;
            }

            sb.Append('\\', backslashes);
            backslashes = 0;
            sb.Append(c);
        }

        sb.Append('\\', backslashes * 2);
        sb.Append('"');
        return sb.ToString();
    }
}
