using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Services;

/// <summary>
/// Temporary AI explainer that shells out to the user's local Codex or Claude
/// CLI. This keeps the feature useful before Octadock has a dedicated model API.
/// </summary>
public sealed partial class CliTextExplanationProvider : ITextExplanationProvider
{
    private const int MaxInputCharacters = 120_000;
    private static readonly TimeSpan CliTimeout = TimeSpan.FromMinutes(2);
    private static readonly Regex AnsiRegex = CreateAnsiRegex();

    private readonly IStoragePaths _paths;
    private readonly ILogger<CliTextExplanationProvider> _logger;

    /// <summary>Creates the local CLI explanation provider.</summary>
    public CliTextExplanationProvider(IStoragePaths paths, ILogger<CliTextExplanationProvider> logger)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string Id => "local-cli";

    /// <inheritdoc />
    public bool IsAvailable => CanRunCommand("codex") || CanRunCommand("claude");

    /// <inheritdoc />
    public async Task<TextExplanationResult> ExplainAsync(
        TextExplanationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string prompt = BuildPrompt(request);
        List<string> providers = ResolveProviderOrder(request.ProviderPreference);
        var failures = new List<string>();

        foreach (string provider in providers)
        {
            if (!CanRunCommand(provider))
            {
                failures.Add($"{provider}: not found");
                continue;
            }

            try
            {
                CliRunResult result = provider switch
                {
                    "claude" => await RunClaudeAsync(prompt, cancellationToken).ConfigureAwait(false),
                    _ => await RunCodexAsync(prompt, cancellationToken).ConfigureAwait(false),
                };

                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Stdout))
                {
                    string output = CleanModelOutput(result.Stdout);
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        return new TextExplanationResult(output, provider);
                    }
                }

                failures.Add($"{provider}: exit {result.ExitCode} {TrimForDisplay(result.Stderr)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogLocalAiProviderFailed(_logger, ex, provider);
                failures.Add($"{provider}: {ex.Message}");
            }
        }

        throw new InvalidOperationException(
            "No local AI explainer completed successfully. " + string.Join("; ", failures));
    }

    private async Task<CliRunResult> RunCodexAsync(string prompt, CancellationToken cancellationToken)
        => await RunCliAsync(
            "codex",
            [
                "exec",
                "--ephemeral",
                "--skip-git-repo-check",
                "--sandbox",
                "read-only",
                "--ask-for-approval",
                "never",
                "--color",
                "never",
                "-",
            ],
            prompt,
            cancellationToken).ConfigureAwait(false);

    private async Task<CliRunResult> RunClaudeAsync(string prompt, CancellationToken cancellationToken)
        => await RunCliAsync(
            "claude",
            [
                "-p",
                "--output-format",
                "text",
                "--permission-mode",
                "dontAsk",
                "--tools",
                string.Empty,
                "--no-session-persistence",
            ],
            prompt,
            cancellationToken).ConfigureAwait(false);

    private async Task<CliRunResult> RunCliAsync(
        string command,
        IReadOnlyList<string> arguments,
        string stdin,
        CancellationToken cancellationToken)
    {
        _paths.EnsureDirectories();
        Directory.CreateDirectory(_paths.TempExportsDirectory);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CliTimeout);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                WorkingDirectory = _paths.TempExportsDirectory,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
        };
        process.StartInfo.ArgumentList.Add("/D");
        process.StartInfo.ArgumentList.Add("/S");
        process.StartInfo.ArgumentList.Add("/C");
        process.StartInfo.ArgumentList.Add(BuildCommandLine(command, arguments));

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException($"Could not start {command}.");
            }

            Task<string> stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            Task<string> stderr = process.StandardError.ReadToEndAsync(timeout.Token);

            await process.StandardInput.WriteAsync(stdin.AsMemory(), timeout.Token).ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return new CliRunResult(
                process.ExitCode,
                await stdout.ConfigureAwait(false),
                await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string BuildPrompt(TextExplanationRequest request)
    {
        string text = request.Text.Trim();
        bool truncated = text.Length > MaxInputCharacters;
        if (truncated)
        {
            text = text[..MaxInputCharacters];
        }

        int targetWords = ResolveTargetWords(request.Length);
        string source = string.IsNullOrWhiteSpace(request.SourceName) ? "the provided text" : request.SourceName.Trim();
        string style = string.IsNullOrWhiteSpace(request.Style) ? "explain" : request.Style.Trim();

        return $$"""
        You are Octadock's read-aloud explainer.

        Read the source text and produce a spoken explanation, not a transcript.
        Sound like a capable human explaining what matters.
        Do not quote long passages. Do not list every detail.
        Use plain spoken prose instead of Markdown headings, tables, or bullet lists.
        Keep it under about {{targetWords}} words.
        Style hint: {{style}}
        Source label: {{source}}
        {{(truncated ? "The source was truncated before you received it; mention uncertainty only if it matters." : string.Empty)}}

        Source text:
        <source>
        {{text}}
        </source>
        """;
    }

    private static List<string> ResolveProviderOrder(string? requested)
    {
        string preference = FirstNonEmpty(
            requested,
            Environment.GetEnvironmentVariable("OCTADOCK_AI_EXPLAINER"),
            Environment.GetEnvironmentVariable("OCTADOCK_READ_AI_PROVIDER"),
            Environment.GetEnvironmentVariable("SNAPDOCK_AI_EXPLAINER"),
            Environment.GetEnvironmentVariable("SNAPDOCK_READ_AI_PROVIDER"),
            "codex").ToLowerInvariant();

        return preference switch
        {
            "claude" => ["claude", "codex"],
            _ => ["codex", "claude"],
        };
    }

    private static int ResolveTargetWords(string? length)
        => (length ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "short" or "brief" => 120,
            "long" or "detailed" => 420,
            _ => 240,
        };

    private static string BuildCommandLine(string command, IReadOnlyList<string> arguments)
    {
        var sb = new StringBuilder(command);
        foreach (string argument in arguments)
        {
            sb.Append(' ');
            sb.Append(QuoteForCmd(argument));
        }

        return sb.ToString();
    }

    private static string QuoteForCmd(string value)
    {
        if (value.Length == 0)
        {
            return "\"\"";
        }

        if (!value.Any(NeedsCmdQuoting))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
    }

    private static bool NeedsCmdQuoting(char value)
        => char.IsWhiteSpace(value) || value is '&' or '|' or '<' or '>' or '(' or ')' or '^' or '"';

    private static bool CanRunCommand(string command)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                ArgumentList = { "/D", "/S", "/C", $"where {command} >NUL 2>NUL" },
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(2_000))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string CleanModelOutput(string value)
        => AnsiRegex.Replace(value, string.Empty).Trim();

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))!.Trim();

    private static string TrimForDisplay(string? value)
    {
        value = CleanModelOutput(value ?? string.Empty);
        return value.Length <= 300
            ? value
            : value[..300] + "...";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex CreateAnsiRegex();

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Local AI provider {Provider} failed.")]
    private static partial void LogLocalAiProviderFailed(ILogger logger, Exception exception, string provider);

    private sealed record CliRunResult(int ExitCode, string Stdout, string Stderr);
}
