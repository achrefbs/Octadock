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
           command.Type is CommandType.ReadAloud
               or CommandType.Dictation
               or CommandType.Quit;

    /// <summary>
    /// True when the launch is an <c>activate</c> request (protocol or CLI). Activation
    /// is a licensing action, not general automation, so it is exempt from the
    /// protocol/CLI enable toggles — a buyer's <c>octadock://activate?key=…</c> deep link
    /// must work even before they turn automation on (WS5, R2).
    /// </summary>
    public static bool IsActivationLaunch(IReadOnlyList<string> args)
    {
        if (args.Count == 0)
        {
            return false;
        }

        string first = args[0].Trim();
        if (first.StartsWith(CommandTokens.Scheme + "://activate", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(first.TrimStart('-'), CommandTokens.ToToken(CommandType.Activate), StringComparison.OrdinalIgnoreCase);
    }
}
