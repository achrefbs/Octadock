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
        [CommandType.AiActions] = "ai",
        [CommandType.Dictation] = "dictation",
        [CommandType.OpenAnnotate] = "open-annotate",
        [CommandType.OpenFromClipboard] = "open-from-clipboard",
        [CommandType.AddShelfItem] = "add-shelf-item",
        [CommandType.Open] = "open",
        [CommandType.OpenHistory] = "open-history",
        [CommandType.OpenClipboardHistory] = "open-clipboard-history",
        [CommandType.OpenContext] = "open-context",
        [CommandType.OpenTextTools] = "open-text-tools",
        [CommandType.RestoreRecentlyClosed] = "restore-recently-closed",
        [CommandType.ClearHistory] = "clear-history",
        [CommandType.OpenSettings] = "open-settings",
        [CommandType.Quit] = "quit",
        [CommandType.Activate] = "activate",
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

    /// <summary>
    /// Commands whose features were removed in this version. The parser keeps
    /// recognizing their tokens so dispatch can fail truthfully ("feature removed")
    /// instead of a misleading "unknown command"; they never appear in help/usage.
    /// </summary>
    private static readonly FrozenSet<CommandType> RemovedCommands = new[]
    {
        CommandType.Pin,
        CommandType.OpenAnnotate,
        CommandType.OpenFromClipboard,
        CommandType.AddShelfItem,
        CommandType.Open,
    }.ToFrozenSet();

    /// <summary>Tokens whose features still exist — the only ones help/usage may advertise.</summary>
    public static IReadOnlyCollection<string> ActiveTokens { get; } =
        ForwardMap.Where(kv => !RemovedCommands.Contains(kv.Key)).Select(kv => kv.Value).ToArray();

    /// <summary>True when <paramref name="type"/> is a tombstoned (removed) command.</summary>
    public static bool IsRemoved(CommandType type) => RemovedCommands.Contains(type);

    /// <summary>The truthful dispatch failure for a tombstoned command.</summary>
    public static string RemovedMessage(CommandType type)
    {
        string token = ToToken(type);
        string detail = type switch
        {
            CommandType.Pin => "Floating pins were removed in this version of Octadock.",
            CommandType.OpenAnnotate =>
                "Annotating an arbitrary file was removed in this version of Octadock. Annotate a capture from the Shelf or History instead.",
            CommandType.OpenFromClipboard =>
                "Opening a clipboard image was removed in this version of Octadock.",
            CommandType.AddShelfItem =>
                "Adding arbitrary files to the Shelf was removed in this version of Octadock. The Shelf holds Octadock captures and recordings only.",
            CommandType.Open =>
                "Generic file preview was removed in this version of Octadock.",
            _ => "This feature was removed in this version of Octadock.",
        };
        return $"'{token}' is no longer available: {detail}";
    }
}
