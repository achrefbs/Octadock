using Octadock.Core.Commands;

namespace Octadock.App;

/// <summary>Small policy helpers for launch arguments that cross process/protocol boundaries.</summary>
internal static class AutomationLaunchSafety
{
    public static bool IsProtocolLaunch(IReadOnlyList<string> args)
        => args.Count == 1 &&
           args[0].StartsWith(CommandTokens.Scheme + "://", StringComparison.OrdinalIgnoreCase);

    public static bool BlocksProtocolCommand(IReadOnlyList<string> args, OctadockCommand command)
        => IsProtocolLaunch(args) &&
           command.Type is CommandType.Run
               or CommandType.Watch
               or CommandType.AiSessionEvent
               or CommandType.ReadAloud
               or CommandType.Dictation
               or CommandType.Quit;
}
