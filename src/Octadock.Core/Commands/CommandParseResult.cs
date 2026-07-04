namespace Octadock.Core.Commands;

/// <summary>
/// Outcome of parsing an automation request. A failed result carries a
/// human-readable error and an <see cref="ExitCode"/> suitable for the CLI.
/// </summary>
public sealed record CommandParseResult
{
    private CommandParseResult(bool success, OctadockCommand? command, string? error, int exitCode)
    {
        Success = success;
        Command = command;
        Error = error;
        ExitCode = exitCode;
    }

    public bool Success { get; }

    public OctadockCommand? Command { get; }

    public string? Error { get; }

    /// <summary>Process exit code the CLI should return (0 on success).</summary>
    public int ExitCode { get; }

    public static CommandParseResult Ok(OctadockCommand command)
        => new(true, command, null, 0);

    public static CommandParseResult Fail(string error, int exitCode = 2)
        => new(false, null, error, exitCode);
}
