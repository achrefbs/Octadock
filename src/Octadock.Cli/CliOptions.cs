using System.Globalization;

namespace Octadock.Cli;

/// <summary>
/// The CLI-level switches that <c>octadock.exe</c> consumes itself before the
/// remaining arguments are handed to the Octadock command parser. These are the
/// only tokens the forwarder interprets; everything else is treated as a verb
/// plus its options and is validated by <see cref="Core.Services.CommandParser"/>.
/// </summary>
internal sealed record CliOptions
{
    /// <summary>Emit machine-readable JSON instead of plain text (<c>--json</c>).</summary>
    public bool Json { get; init; }

    /// <summary>Show help and exit (<c>--help</c>, <c>-h</c>, <c>-?</c>).</summary>
    public bool Help { get; init; }

    /// <summary>Print the CLI version and exit (<c>--version</c>).</summary>
    public bool Version { get; init; }

    /// <summary>Global operation timeout. Defaults to 15 seconds; override with <c>--timeout &lt;seconds&gt;</c>.</summary>
    public TimeSpan Timeout { get; init; } = DefaultTimeout;

    /// <summary>Do not launch Octadock if it is not already running (<c>--no-launch</c>); fail instead.</summary>
    public bool NoLaunch { get; init; }

    /// <summary>The arguments left over after the CLI-level switches were removed. These form the command.</summary>
    public IReadOnlyList<string> CommandArguments { get; init; } = [];

    /// <summary>The default global timeout for a forward + reply round-trip.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Splits the raw argument vector into CLI-level switches and the residual
    /// command arguments. CLI-level switches are only recognized before the
    /// command verb, or before a <c>--</c> delimiter. The help switch is also
    /// recognized after a command verb so <c>octadock capture-area --help</c>
    /// shows command help instead of running the command. Unknown <c>--foo</c> tokens are left in
    /// <see cref="CommandArguments"/> so per-command options (for example
    /// <c>--action</c>) are forwarded untouched.
    /// </summary>
    /// <param name="args">The process argument vector.</param>
    /// <param name="options">The parsed switches and the residual command arguments.</param>
    /// <param name="error">A parse error for a malformed CLI-level switch (for example a bad timeout).</param>
    /// <returns><c>true</c> when parsing succeeded; <c>false</c> when a CLI-level switch was malformed.</returns>
    public static bool TryParse(IReadOnlyList<string> args, out CliOptions options, out string? error)
    {
        error = null;
        bool json = false;
        bool help = false;
        bool version = false;
        bool noLaunch = false;
        TimeSpan timeout = DefaultTimeout;
        var rest = new List<string>(args.Count);

        bool parsingGlobals = true;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (!parsingGlobals)
            {
                if (IsHelpSwitch(arg))
                {
                    help = true;
                    continue;
                }

                rest.Add(arg);
                continue;
            }

            if (arg == "--")
            {
                parsingGlobals = false;
                continue;
            }

            switch (arg.ToLowerInvariant())
            {
                case "--json":
                    json = true;
                    continue;
                case var value when IsHelpSwitch(value):
                    help = true;
                    continue;
                case "--version":
                    version = true;
                    continue;
                case "--no-launch":
                    noLaunch = true;
                    continue;
                case "--timeout":
                    if (i + 1 >= args.Count || !TryParseTimeout(args[i + 1], out timeout))
                    {
                        options = new CliOptions();
                        error = "Option '--timeout' requires a positive number of seconds, e.g. --timeout 10.";
                        return false;
                    }

                    i++;
                    continue;
                default:
                    if (arg.StartsWith("--timeout=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!TryParseTimeout(arg["--timeout=".Length..], out timeout))
                        {
                            options = new CliOptions();
                            error = "Option '--timeout' requires a positive number of seconds, e.g. --timeout=10.";
                            return false;
                        }

                        continue;
                    }

                    rest.Add(arg);
                    parsingGlobals = false;
                    continue;
            }
        }

        options = new CliOptions
        {
            Json = json,
            Help = help,
            Version = version,
            NoLaunch = noLaunch,
            Timeout = timeout,
            CommandArguments = rest,
        };
        return true;
    }

    private static bool TryParseTimeout(string value, out TimeSpan timeout)
    {
        timeout = DefaultTimeout;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
            seconds > 0 &&
            seconds <= 600)
        {
            timeout = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return false;
    }

    private static bool IsHelpSwitch(string value)
        => value.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("-?", StringComparison.OrdinalIgnoreCase) ||
           value.Equals("/?", StringComparison.OrdinalIgnoreCase);
}
