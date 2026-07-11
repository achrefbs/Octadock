using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Octadock.Core.Ai;

namespace Octadock.App.Ai;

/// <summary>
/// One exact, user-reviewed Agent Packet handoff. <see cref="ExactPrompt"/> is
/// sent character-for-character to the selected CLI; the runner never adds hidden context.
/// </summary>
public sealed record AgentAnalyzeRequest
{
    public required string ProviderId { get; init; }

    /// <summary>The local directory containing TASK.md, manifest.json, and packet assets.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>The complete outbound prompt already shown to and approved by the user.</summary>
    public required string ExactPrompt { get; init; }

    /// <summary>Local packet images the selected agent may inspect.</summary>
    public IReadOnlyList<string> ImagePaths { get; init; } = [];
}

/// <summary>Runs a reviewed Agent Packet through one explicitly selected local CLI.</summary>
public interface IAgentCliRunner
{
    IReadOnlyList<AiCliProviderDescriptor> Providers { get; }

    Task<string> AnalyzeAsync(
        AgentAnalyzeRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read-only process boundary for Agent Workspace. Codex receives a read-only,
/// ephemeral workspace and Claude receives only its Read and Glob tools. Neither
/// provider is silently substituted when the selected CLI is unavailable.
/// </summary>
public sealed partial class AgentCliRunner : IAgentCliRunner
{
    internal const int MaxPromptCharacters = AgentPacketLimits.MaxOutboundCharacters;
    internal const int MaxOutputCharacters = 500_000;
    internal const int MaxImages = AgentPacketLimits.MaxSources;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(3);
    private static readonly Regex AnsiRegex = CreateAnsiRegex();

    private readonly IAiCliRunner _providerCatalog;
    private readonly IAgentCliProcessInvoker _processInvoker;
    private readonly ILogger<AgentCliRunner> _logger;

    public AgentCliRunner(
        IAiCliRunner providerCatalog,
        ILogger<AgentCliRunner> logger)
        : this(
            providerCatalog,
            new SystemAgentCliProcessInvoker(DefaultTimeout, MaxOutputCharacters),
            logger)
    {
    }

    internal AgentCliRunner(
        IAiCliRunner providerCatalog,
        IAgentCliProcessInvoker processInvoker,
        ILogger<AgentCliRunner> logger)
    {
        _providerCatalog = providerCatalog ?? throw new ArgumentNullException(nameof(providerCatalog));
        _processInvoker = processInvoker ?? throw new ArgumentNullException(nameof(processInvoker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public IReadOnlyList<AiCliProviderDescriptor> Providers => _providerCatalog.Providers;

    /// <inheritdoc />
    public async Task<string> AnalyzeAsync(
        AgentAnalyzeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        AiCliProviderDescriptor provider = FindSelectedProvider(request.ProviderId);
        if (!provider.IsAvailable)
        {
            throw new InvalidOperationException(
                provider.UnavailableReason ?? $"{provider.DisplayName} CLI was not found on PATH.");
        }

        ValidatedAgentAnalyzeRequest validated = Validate(request);
        AgentCliInvocation invocation = BuildInvocation(provider.Id, validated);

        try
        {
            AgentCliProcessResult result = await _processInvoker
                .RunAsync(invocation, cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"{provider.DisplayName} CLI exited with code {result.ExitCode}. Run it once in a terminal to verify sign-in and configuration.");
            }

            if (result.StandardOutput.Length > MaxOutputCharacters)
            {
                throw new InvalidOperationException(
                    $"{provider.DisplayName} returned more than {MaxOutputCharacters:N0} characters.");
            }

            string output = AnsiRegex.Replace(result.StandardOutput, string.Empty).Trim();
            if (output.Length == 0)
            {
                throw new InvalidOperationException($"{provider.DisplayName} returned an empty result.");
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never log the prompt, packet path, stderr, or provider output.
            LogProviderRunFailed(_logger, ex, provider.Id);
            throw;
        }
    }

    internal static AgentCliInvocation BuildInvocation(
        string providerId,
        ValidatedAgentAnalyzeRequest request)
    {
        IReadOnlyList<string> arguments = providerId switch
        {
            AiCliProviderIds.Codex => BuildCodexArguments(request),
            AiCliProviderIds.Claude => BuildClaudeArguments(request),
            _ => throw new ArgumentException(
                $"Unsupported Agent Workspace provider '{providerId}'.",
                nameof(providerId)),
        };

        return new AgentCliInvocation(
            providerId,
            request.WorkingDirectory,
            arguments,
            request.ExactPrompt);
    }

    private AiCliProviderDescriptor FindSelectedProvider(string providerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerId);

        string normalized = providerId.Trim().ToLowerInvariant();
        if (normalized is not (AiCliProviderIds.Codex or AiCliProviderIds.Claude))
        {
            throw new ArgumentException(
                $"Unsupported Agent Workspace provider '{providerId}'.",
                nameof(providerId));
        }

        return Providers.FirstOrDefault(item =>
                string.Equals(item.Id, normalized, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"The selected AI CLI provider '{providerId}' is not configured. Octadock will not choose a fallback provider.");
    }

    private static ValidatedAgentAnalyzeRequest Validate(AgentAnalyzeRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ExactPrompt);
        if (request.ExactPrompt.Length > MaxPromptCharacters)
        {
            throw new ArgumentException(
                $"The reviewed Agent Packet prompt exceeds {MaxPromptCharacters:N0} characters.",
                nameof(request));
        }

        string workingDirectory = ValidateExistingLocalDirectory(request.WorkingDirectory);
        string physicalWorkingDirectory = ResolvePhysicalPath(workingDirectory);

        IReadOnlyList<string> requestedImages = request.ImagePaths
            ?? throw new ArgumentException("Agent Packet image paths cannot be null.", nameof(request));
        if (requestedImages.Count > MaxImages)
        {
            throw new ArgumentException(
                $"An Agent Packet can attach at most {MaxImages} images.",
                nameof(request));
        }

        var images = new List<string>(requestedImages.Count);
        foreach (string imagePath in requestedImages)
        {
            string image = ValidateExistingLocalFile(imagePath, "image");
            EnsureWithinDirectory(workingDirectory, image, "Image paths must stay inside the Agent Packet directory.");

            string physicalImage = ResolvePhysicalPath(image);
            EnsureWithinDirectory(
                physicalWorkingDirectory,
                physicalImage,
                "An Agent Packet image resolves outside the packet directory through a file-system link.");
            images.Add(image);
        }

        return new ValidatedAgentAnalyzeRequest(
            workingDirectory,
            request.ExactPrompt,
            images.AsReadOnly());
    }

    private static ReadOnlyCollection<string> BuildCodexArguments(ValidatedAgentAnalyzeRequest request)
    {
        var arguments = new List<string>(16 + (request.ImagePaths.Count * 2))
        {
            "-a",
            "never",
            "exec",
            "--ephemeral",
            "--skip-git-repo-check",
            "--sandbox",
            "read-only",
            "--ignore-user-config",
            "--ignore-rules",
            // Agent Workspace passes the complete reviewed text through stdin
            // and images explicitly. Disabling Codex's shell/exec tools closes
            // read access to unrelated files outside the packet directory.
            "--disable",
            "shell_tool",
            "--disable",
            "unified_exec",
            "--disable",
            "shell_snapshot",
            "--color",
            "never",
            "-C",
            request.WorkingDirectory,
        };

        foreach (string image in request.ImagePaths)
        {
            arguments.Add("--image");
            arguments.Add(image);
        }

        // A literal '-' makes Codex consume the exact reviewed prompt from stdin.
        arguments.Add("-");
        return arguments.AsReadOnly();
    }

    private static IReadOnlyList<string> BuildClaudeArguments(ValidatedAgentAnalyzeRequest request)
        =>
        [
            "-p",
            "--output-format",
            "text",
            "--permission-mode",
            "dontAsk",
            "--tools",
            "Read,Glob",
            "--allowedTools",
            "Read(./**),Glob(./**)",
            "--no-session-persistence",
            "--safe-mode",
        ];

    private static string ValidateExistingLocalDirectory(string path)
    {
        string lexical = ValidateLocalAbsolutePath(path, "Agent Packet directory");
        return AgentLocalPathGuard.ValidateExistingDirectory(lexical, "Agent Packet directory");
    }

    private static string ValidateExistingLocalFile(string path, string label)
    {
        string lexical = ValidateLocalAbsolutePath(path, $"Agent Packet {label}");
        return AgentLocalPathGuard.ValidateExistingFile(lexical, $"Agent Packet {label}");
    }

    private static string ValidateLocalAbsolutePath(string path, string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"{label} must be an absolute local path.", nameof(path));
        }

        if (HasTraversalSegment(path))
        {
            throw new ArgumentException($"{label} cannot contain traversal segments.", nameof(path));
        }

        // Device paths and UNC shares expand the trust boundary to another host
        // (or bypass normal path parsing), so Agent Workspace rejects both.
        if (path.StartsWith(@"\\", StringComparison.Ordinal) ||
            path.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{label} must be on a local drive, not a UNC or device path.", nameof(path));
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException($"{label} is not a valid local path.", nameof(path), ex);
        }

        string driveRoot = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException($"{label} does not have a local drive root.", nameof(path));
        if (new DriveInfo(driveRoot).DriveType == DriveType.Network)
        {
            throw new ArgumentException($"{label} must be on a local drive, not a mapped network drive.", nameof(path));
        }

        return fullPath;
    }

    private static bool HasTraversalSegment(string path)
        => path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Any(segment => segment is "." or "..");

    private static void EnsureWithinDirectory(string directory, string candidate, string message)
    {
        string relative = Path.GetRelativePath(directory, candidate);
        if (Path.IsPathFullyQualified(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException(message, nameof(candidate));
        }
    }

    /// <summary>
    /// Resolves every existing reparse-point component so a junction inside the
    /// packet cannot make a lexically in-packet image point somewhere else.
    /// </summary>
    private static string ResolvePhysicalPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string root = Path.GetPathRoot(fullPath)
            ?? throw new ArgumentException("Path does not have a local drive root.", nameof(path));
        string current = root;
        string relative = Path.GetRelativePath(root, fullPath);

        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileSystemInfo info = Directory.Exists(current)
                ? new DirectoryInfo(current)
                : new FileInfo(current);

            if (info.LinkTarget is not null)
            {
                FileSystemInfo target = info.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException($"Could not resolve file-system link '{info.Name}'.");
                current = Path.GetFullPath(target.FullName);
            }
        }

        return Path.GetFullPath(current);
    }

    [GeneratedRegex(@"\x1B\[[0-?]*[ -/]*[@-~]", RegexOptions.Compiled)]
    private static partial Regex CreateAnsiRegex();

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Debug,
        Message = "Selected Agent Workspace CLI provider {Provider} failed.")]
    private static partial void LogProviderRunFailed(
        ILogger logger,
        Exception exception,
        string provider);
}

internal sealed record ValidatedAgentAnalyzeRequest(
    string WorkingDirectory,
    string ExactPrompt,
    IReadOnlyList<string> ImagePaths);

internal sealed record AgentCliInvocation(
    string Command,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    string StandardInput);

internal sealed record AgentCliProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError = "");

internal interface IAgentCliProcessInvoker
{
    Task<AgentCliProcessResult> RunAsync(
        AgentCliInvocation invocation,
        CancellationToken cancellationToken);
}

/// <summary>Launches an installed native CLI (or its Windows command shim).</summary>
internal sealed class SystemAgentCliProcessInvoker : IAgentCliProcessInvoker
{
    private readonly TimeSpan _timeout;
    private readonly int _maxOutputCharacters;

    public SystemAgentCliProcessInvoker(TimeSpan timeout, int maxOutputCharacters)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxOutputCharacters);

        _timeout = timeout;
        _maxOutputCharacters = maxOutputCharacters;
    }

    public async Task<AgentCliProcessResult> RunAsync(
        AgentCliInvocation invocation,
        CancellationToken cancellationToken)
    {
        string executable = ResolveExecutable(invocation.Command)
            ?? throw new InvalidOperationException(
                $"{DisplayName(invocation.Command)} CLI was not found on PATH.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        using var process = new Process
        {
            StartInfo = CreateStartInfo(executable, invocation),
        };

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(
                    $"Could not start {DisplayName(invocation.Command)} CLI.");
            }

            Task<string> stdout = ReadBoundedAsync(
                process.StandardOutput,
                _maxOutputCharacters,
                timeout.Token);
            Task<string> stderr = ReadBoundedAsync(
                process.StandardError,
                Math.Min(_maxOutputCharacters, 128_000),
                timeout.Token);

            await process.StandardInput
                .WriteAsync(invocation.StandardInput.AsMemory(), timeout.Token)
                .ConfigureAwait(false);
            process.StandardInput.Close();

            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderr).ConfigureAwait(false);
            return new AgentCliProcessResult(process.ExitCode, stdout.Result, stderr.Result);
        }
        catch (OperationCanceledException) when (
            !cancellationToken.IsCancellationRequested && timeout.IsCancellationRequested)
        {
            await TryKillAndWaitAsync(process).ConfigureAwait(false);
            throw new TimeoutException(
                $"{DisplayName(invocation.Command)} did not finish within {_timeout.TotalMinutes:N0} minutes.");
        }
        catch (OperationCanceledException)
        {
            await TryKillAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await TryKillAndWaitAsync(process).ConfigureAwait(false);
            throw;
        }
    }

    internal static ProcessStartInfo CreateStartInfo(
        string executable,
        AgentCliInvocation invocation)
    {
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = invocation.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        SanitizeProviderEnvironment(startInfo, invocation.Command);

        string extension = Path.GetExtension(executable);
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            // cmd.exe's /S /C quote rules require one raw outer quote pair
            // around the already-quoted command. ProcessStartInfo.ArgumentList
            // escapes those quotes as literal characters, causing cmd to look
            // for a filename that contains `\"`. Use the raw Arguments string
            // for this one audited Windows-shim boundary.
            startInfo.Arguments =
                "/D /V:OFF /S /C \"" + BuildCommandLine(executable, invocation.Arguments) + "\"";
            return startInfo;
        }

        startInfo.FileName = executable;
        foreach (string argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    internal static string BuildCommandLine(string executable, IReadOnlyList<string> arguments)
    {
        var line = new StringBuilder(QuoteForCmd(executable));
        foreach (string argument in arguments)
        {
            line.Append(' ').Append(QuoteForCmd(argument));
        }

        return line.ToString();
    }

    private static string QuoteForCmd(string value)
    {
        // Executable names and existing Windows paths cannot contain quotes.
        // Rejecting one here keeps cmd-shim quoting auditable instead of trying
        // to emulate cmd.exe's context-sensitive embedded-quote grammar.
        if (value.Contains('"', StringComparison.Ordinal))
        {
            throw new ArgumentException("A CLI argument contains an invalid quote character.", nameof(value));
        }

        // Percent expansion still occurs inside cmd quotes. A caret makes it
        // literal; delayed ! expansion is disabled by /V:OFF above.
        string escaped = value
            .Replace("^", "^^", StringComparison.Ordinal)
            .Replace("%", "^%", StringComparison.Ordinal);
        return '"' + escaped + '"';
    }

    internal static string? ResolveExecutable(string command)
    {
        string? preferred = ResolvePreferredUserExecutable(
            command,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        if (preferred is not null)
        {
            return preferred;
        }

        string? pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        string[] extensions = (Environment.GetEnvironmentVariable("PATHEXT")
                ?? ".COM;.EXE;.BAT;.CMD")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string rawDirectory in pathValue.Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string directory = rawDirectory.Trim('"');
            foreach (string extension in extensions)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, command + extension.ToLowerInvariant());
                }
                catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            string extensionless = Path.Combine(directory, command);
            if (File.Exists(extensionless))
            {
                return Path.GetFullPath(extensionless);
            }
        }

        return null;
    }

    internal static string? ResolvePreferredUserExecutable(string command, string applicationData)
    {
        if (!string.Equals(command, AiCliProviderIds.Codex, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(applicationData))
        {
            return null;
        }

        string npmDirectory = Path.Combine(applicationData, "npm");
        foreach (string filename in new[] { "codex.cmd", "codex.exe", "codex.bat" })
        {
            string candidate = Path.Combine(npmDirectory, filename);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static void SanitizeProviderEnvironment(ProcessStartInfo startInfo, string command)
    {
        if (!string.Equals(command, AiCliProviderIds.Codex, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Octadock is frequently launched from Codex Desktop during development.
        // Those host-only variables describe the parent task and must not turn a
        // fresh CLI invocation into a nested/attached Codex process.
        foreach (string variable in new[]
                 {
                     "CODEX_INTERNAL_ORIGINATOR_OVERRIDE",
                     "CODEX_PERMISSION_PROFILE",
                     "CODEX_SHELL",
                     "CODEX_THREAD_ID",
                     "OPENAI_API_KEY",
                 })
        {
            startInfo.Environment.Remove(variable);
        }

        if (!startInfo.Environment.TryGetValue("CODEX_HOME", out string? codexHome) ||
            string.IsNullOrWhiteSpace(codexHome))
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(userProfile))
            {
                startInfo.Environment["CODEX_HOME"] = Path.Combine(userProfile, ".codex");
            }
        }
    }

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maxCharacters,
        CancellationToken cancellationToken)
    {
        var output = new StringBuilder(Math.Min(maxCharacters, 16_384));
        var buffer = new char[8_192];
        bool exceeded = false;
        while (true)
        {
            int read = await reader
                .ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                if (exceeded)
                {
                    throw new InvalidOperationException(
                        $"Agent CLI output exceeded {maxCharacters:N0} characters.");
                }

                return output.ToString();
            }

            if (exceeded)
            {
                continue;
            }

            int remaining = maxCharacters - output.Length;
            if (read > remaining)
            {
                output.Append(buffer, 0, remaining);
                exceeded = true;
                continue;
            }

            output.Append(buffer, 0, read);
        }
    }

    private static string DisplayName(string providerId)
        => string.Equals(providerId, AiCliProviderIds.Claude, StringComparison.OrdinalIgnoreCase)
            ? "Claude"
            : "Codex";

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
            // Best effort: cleanup failure must not replace the original error.
        }
    }

    private static async Task TryKillAndWaitAsync(Process process)
    {
        TryKill(process);
        try
        {
            using var wait = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            // Best effort. Retention scavenges any packet that remains locked
            // after this bounded process-tree shutdown attempt.
        }
    }
}
