using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Cli;

/// <summary>
/// Renders CLI results to stdout/stderr in either human-readable text or, when
/// <c>--json</c> is set, a single line of machine-readable JSON. Successful
/// output goes to stdout; errors go to stderr so scripts can redirect them.
/// </summary>
internal sealed class CliConsole
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly bool _json;
    private readonly TextWriter _out;
    private readonly TextWriter _error;

    public CliConsole(bool json)
        : this(json, Console.Out, Console.Error)
    {
    }

    public CliConsole(bool json, TextWriter output, TextWriter error)
    {
        _json = json;
        _out = output;
        _error = error;
    }

    /// <summary>Reports a success. In text mode prints <paramref name="message"/>; in JSON mode emits a success envelope.</summary>
    public void Success(string? message, int exitCode)
    {
        if (_json)
        {
            WriteJson(new CliResult
            {
                Success = true,
                ExitCode = exitCode,
                Message = message,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            _out.WriteLine(message);
        }
    }

    /// <summary>Reports a failure. In text mode writes <paramref name="message"/> to stderr; in JSON mode emits an error envelope.</summary>
    public void Failure(string message, int exitCode, string? errorCode = null)
    {
        if (_json)
        {
            WriteJson(new CliResult
            {
                Success = false,
                ExitCode = exitCode,
                Message = message,
                Error = errorCode ?? DefaultErrorCode(exitCode),
            });
            return;
        }

        _error.WriteLine($"octadock: {message}");
    }

    /// <summary>Writes plain informational text (help, version). Never JSON-wrapped.</summary>
    public void WriteLine(string text) => _out.WriteLine(text);

    private void WriteJson(CliResult result) => _out.WriteLine(JsonSerializer.Serialize(result, JsonOptions));

    private static string DefaultErrorCode(int exitCode) => exitCode switch
    {
        ExitCodes.ParseError => "parse_error",
        ExitCodes.RuntimeError => "runtime_error",
        _ => "error",
    };

    /// <summary>The JSON envelope emitted under <c>--json</c>.</summary>
    private sealed record CliResult
    {
        public bool Success { get; init; }

        public int ExitCode { get; init; }

        public string? Message { get; init; }

        /// <summary>A stable machine-readable error slug; only present on failures.</summary>
        public string? Error { get; init; }
    }
}
