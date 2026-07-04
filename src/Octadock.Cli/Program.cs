using System.Reflection;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Ipc;
using Octadock.Core.Services;

namespace Octadock.Cli;

/// <summary>
/// Entry point for <c>octadock.exe</c>, the thin automation forwarder.
///
/// <para>
/// The CLI never performs a capture itself. It (1) pulls off its own pre-verb switches
/// (<c>--json</c>, <c>--help</c>, <c>--timeout</c>, <c>--no-launch</c>,
/// <c>--version</c>); (2) validates the remaining command locally with the same
/// <see cref="CommandParser"/> the tray app uses, so bad input fails fast without
/// a round-trip; (3) connects to the running Octadock instance over the per-user
/// named pipe defined by <see cref="IpcProtocol"/>, launching <c>Octadock.exe</c>
/// (found next to the CLI) if nothing is listening; and (4) sends one
/// <see cref="IpcRequest"/> line, reads one <see cref="IpcResponse"/> line, prints
/// the message and returns the response's exit code.
/// </para>
///
/// <para>Exit codes: 0 success, 1 runtime/IPC error, 2 parse error. See <see cref="ExitCodes"/>.</para>
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        // Split CLI-level switches from the command tokens forwarded to the app.
        if (!CliOptions.TryParse(args, out CliOptions options, out string? optionError))
        {
            bool jsonRequested = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
            new CliConsole(jsonRequested).Failure(optionError!, ExitCodes.ParseError);
            return ExitCodes.ParseError;
        }

        var console = new CliConsole(options.Json);

        if (options.Version)
        {
            console.WriteLine($"octadock {CliVersion()}");
            return ExitCodes.Ok;
        }

        // A bare invocation or pre-verb `octadock --help`.
        if (options.Help || options.CommandArguments.Count == 0)
        {
            return ShowHelp(console, options);
        }

        // Validate locally with Core so obviously-bad input never opens a pipe.
        var parser = new CommandParser();
        CommandParseResult parseResult = ParseCommand(parser, options.CommandArguments);
        if (!parseResult.Success)
        {
            console.Failure(parseResult.Error ?? "Invalid command.", parseResult.ExitCode);
            return parseResult.ExitCode;
        }

        // The tray app is the source of truth: forward the original arguments so
        // it re-parses and executes with full platform context.
        var request = new IpcRequest
        {
            Arguments = options.CommandArguments,
            WorkingDirectory = Environment.CurrentDirectory,
        };

        using var timeout = new CancellationTokenSource(options.Timeout);
        using var cancellation = LinkConsoleCancel(timeout);

        var client = new PipeClient(allowLaunch: !options.NoLaunch);
        ForwardOutcome outcome;
        try
        {
            outcome = await client.ForwardAsync(request, cancellation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            console.Failure(
                $"Timed out after {options.Timeout.TotalSeconds:0.#}s waiting for Octadock.",
                ExitCodes.RuntimeError,
                "timeout");
            return ExitCodes.RuntimeError;
        }

        if (outcome.Response is { } response)
        {
            int exitCode = response.Success || response.ExitCode != 0
                ? response.ExitCode
                : ExitCodes.RuntimeError;

            if (response.Success)
            {
                console.Success(response.Message, exitCode);
            }
            else
            {
                console.Failure(
                    response.Message ?? "Octadock reported a failure.",
                    exitCode);
            }

            return exitCode;
        }

        console.Failure(outcome.ErrorMessage ?? "Unknown error.", outcome.ExitCode);
        return outcome.ExitCode;
    }

    /// <summary>
    /// Parses the command tokens. A single <c>octadock://</c> URI (protocol
    /// activation) is routed to <see cref="ICommandParser.ParseUri"/>; everything
    /// else is treated as a CLI argument array.
    /// </summary>
    private static CommandParseResult ParseCommand(CommandParser parser, IReadOnlyList<string> commandArgs)
    {
        if (commandArgs.Count == 1 &&
            commandArgs[0].StartsWith(CommandTokens.Scheme + "://", StringComparison.OrdinalIgnoreCase))
        {
            return parser.ParseUri(commandArgs[0]);
        }

        return parser.ParseArguments(commandArgs);
    }

    private static int ShowHelp(CliConsole console, CliOptions options)
    {
        // `octadock --help <verb>` shows command-specific help when a verb is present.
        if (options.CommandArguments.Count > 0)
        {
            string verb = options.CommandArguments[0];
            string? commandHelp = HelpText.ForCommand(verb);
            if (commandHelp is not null)
            {
                console.WriteLine(commandHelp);
                return ExitCodes.Ok;
            }

            // Unknown verb with --help: surface a parse error so the exit code is 2.
            var parser = new CommandParser();
            CommandParseResult probe = parser.ParseArguments([verb]);
            if (!probe.Success)
            {
                console.Failure(probe.Error ?? $"Unknown command '{verb}'.", probe.ExitCode);
                return probe.ExitCode;
            }
        }

        console.WriteLine(HelpText.Root());
        return ExitCodes.Ok;
    }

    /// <summary>Links Ctrl+C to the timeout token so an interactive user can abort cleanly.</summary>
    private static ConsoleCancellation LinkConsoleCancel(CancellationTokenSource timeout)
        => new(timeout);

    private sealed class ConsoleCancellation : IDisposable
    {
        private readonly CancellationTokenSource _linked;
        private readonly ConsoleCancelEventHandler _handler;

        public ConsoleCancellation(CancellationTokenSource timeout)
        {
            _linked = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            _handler = (_, e) =>
            {
                e.Cancel = true;
                _linked.Cancel();
            };
            Console.CancelKeyPress += _handler;
        }

        public CancellationToken Token => _linked.Token;

        public void Dispose()
        {
            Console.CancelKeyPress -= _handler;
            _linked.Dispose();
        }
    }

    private static string CliVersion()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string? informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // Strip the source-revision suffix MSBuild appends (e.g. "0.2.0+abc123").
            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? informational[..plus] : informational;
        }

        Version? version = assembly.GetName().Version;
        return version is null ? "0.0.0" : version.ToString(3);
    }
}
