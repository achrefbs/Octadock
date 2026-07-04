using System.Collections.Frozen;

namespace Octadock.Core.Commands;

/// <summary>
/// Canonical string tokens for each <see cref="CommandType"/> (the part after
/// <c>octadock://</c> and the first CLI argument), plus the reverse lookup used
/// by the parser. Tokens are lower-kebab-case and stable across the protocol
/// and CLI surfaces.
/// </summary>
public static class CommandTokens
{
    /// <summary>The URI scheme Octadock registers.</summary>
    public const string Scheme = "octadock";

    private static readonly FrozenDictionary<CommandType, string> ForwardMap = new Dictionary<CommandType, string>
    {
        [CommandType.AllInOne] = "all-in-one",
        [CommandType.CaptureArea] = "capture-area",
        [CommandType.CapturePreviousArea] = "capture-previous-area",
        [CommandType.CaptureFullscreen] = "capture-fullscreen",
        [CommandType.CaptureWindow] = "capture-window",
        [CommandType.SelfTimer] = "self-timer",
        [CommandType.ScrollingCapture] = "scrolling-capture",
        [CommandType.Pin] = "pin",
        [CommandType.RecordScreen] = "record-screen",
        [CommandType.CaptureText] = "capture-text",
        [CommandType.ReadAloud] = "read",
        [CommandType.Dictation] = "dictation",
        [CommandType.OpenAnnotate] = "open-annotate",
        [CommandType.OpenFromClipboard] = "open-from-clipboard",
        [CommandType.AddShelfItem] = "add-shelf-item",
        [CommandType.Open] = "open",
        [CommandType.OpenHistory] = "open-history",
        [CommandType.RestoreRecentlyClosed] = "restore-recently-closed",
        [CommandType.ClearHistory] = "clear-history",
        [CommandType.OpenSettings] = "open-settings",
        [CommandType.OpenAiSessions] = "open-ai-sessions",
        [CommandType.Run] = "run",
        [CommandType.Watch] = "watch",
        [CommandType.AiSessionEvent] = "ai-session-event",
    }.ToFrozenDictionary();

    private static readonly FrozenDictionary<string, CommandType> ReverseMap =
        ForwardMap.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the wire token for a command type.</summary>
    public static string ToToken(CommandType type)
        => ForwardMap.TryGetValue(type, out string? token) ? token : throw new ArgumentOutOfRangeException(nameof(type));

    /// <summary>Resolves a wire token (case-insensitive) to a command type.</summary>
    public static CommandType FromToken(string? token)
        => token is not null && ReverseMap.TryGetValue(token, out CommandType type) ? type : CommandType.Unknown;

    /// <summary>All known tokens, for help text and validation.</summary>
    public static IReadOnlyCollection<string> AllTokens => ReverseMap.Keys;
}
