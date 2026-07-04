namespace Octadock.Cli;

/// <summary>
/// Process exit codes returned by <c>octadock.exe</c>. Scripts and launchers can
/// branch on these:
/// <list type="bullet">
///   <item><description><see cref="Ok"/> (0): the command was accepted and dispatched.</description></item>
///   <item><description><see cref="RuntimeError"/> (1): a runtime/IPC failure (pipe timeout,
///     broken pipe, the tray app could not be started, or the tray reported failure).</description></item>
///   <item><description><see cref="ParseError"/> (2): the command could not be parsed
///     (unknown verb, malformed option, bad coordinates).</description></item>
/// </list>
/// The parse code deliberately matches <see cref="Core.Commands.CommandParseResult.ExitCode"/>
/// so a local validation failure and a server-side validation failure surface the same code.
/// </summary>
internal static class ExitCodes
{
    /// <summary>The command was accepted and forwarded successfully.</summary>
    public const int Ok = 0;

    /// <summary>A runtime or IPC failure prevented the command from being delivered or run.</summary>
    public const int RuntimeError = 1;

    /// <summary>The command could not be parsed or validated.</summary>
    public const int ParseError = 2;
}
