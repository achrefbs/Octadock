using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// Discovers already-running local AI tools and mirrors them into the durable
/// Active AI Sessions table. This complements explicit <c>octadock run/watch</c>
/// commands; it is intentionally conservative so Electron helper processes do
/// not flood the UI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionDiscoveryService : IDisposable
{
    private const string DiscoverySource = "process-discovery";
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CodexThreadRecencyWindow = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan CodexActiveRolloutWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CodexCompletedThreadWindow = TimeSpan.FromHours(6);
    private static readonly HashSet<AiSessionStatus> ActiveStatuses =
    [
        AiSessionStatus.Queued,
        AiSessionStatus.Running,
        AiSessionStatus.WaitingForInput,
        AiSessionStatus.Paused,
    ];

    private readonly IAiSessionRepository _sessions;
    private readonly IClock _clock;
    private readonly ILogger<AiSessionDiscoveryService> _logger;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly object _timerGate = new();

    private CancellationTokenSource? _timerCts;
    private Task? _timerTask;
    private bool _disposed;

    /// <summary>Creates the process discovery service.</summary>
    public AiSessionDiscoveryService(
        IAiSessionRepository sessions,
        IClock clock,
        ILogger<AiSessionDiscoveryService> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts periodic background discovery.</summary>
    public void Start()
    {
        lock (_timerGate)
        {
            if (_disposed || _timerTask is not null)
            {
                return;
            }

            _timerCts = new CancellationTokenSource();
            _timerTask = RunLoopAsync(_timerCts.Token);
        }
    }

    /// <summary>Stops periodic background discovery.</summary>
    public void Stop()
    {
        lock (_timerGate)
        {
            if (_timerCts is null)
            {
                return;
            }

            _timerCts.Cancel();
            _timerCts.Dispose();
            _timerCts = null;
            _timerTask = null;
        }
    }

    /// <summary>Runs one scan and syncs discovered process rows.</summary>
    public async Task<AiSessionDiscoveryResult> ScanOnceAsync(CancellationToken cancellationToken = default)
    {
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DateTimeOffset observedAt = _clock.UtcNow;
            AiSessionCandidateScan scan = await Task.Run(
                () => EnumerateCandidateProcesses(observedAt),
                cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AiSessionProcessCandidate> candidates = scan.Candidates;

            var filter = new AiSessionFilter
            {
                Statuses = ActiveStatuses,
                Limit = 1000,
            };
            IReadOnlyList<AiSessionRecord> activeSessions = await _sessions.ListAsync(filter, cancellationToken)
                .ConfigureAwait(false);
            List<AiSessionRecord> discoveredSessions = activeSessions
                .Where(IsDiscoverySession)
                .ToList();

            var activeKeys = candidates
                .Select(c => c.ProcessKey)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var codexStateWorkspaces = candidates
                .Where(c => string.Equals(c.Detector, "codex-state-thread", StringComparison.OrdinalIgnoreCase))
                .Select(c => NormalizeWorkspacePath(c.WorkingDirectory))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            int completed = await CompleteMissingProcessesAsync(
                discoveredSessions,
                activeKeys,
                codexStateWorkspaces,
                scan.InactiveCodexWorkspaces,
                scan.CodexStateHealthy,
                observedAt,
                cancellationToken).ConfigureAwait(false);

            // Run/watch rows are normally completed by an in-memory process-exit
            // monitor; after an app restart that monitor is gone and the rows
            // would spin "running" forever. Reconcile them against live PIDs.
            completed += await ReconcileOrphanedTrackedSessionsAsync(
                activeSessions.Where(s => !IsDiscoverySession(s)).ToList(),
                observedAt,
                cancellationToken).ConfigureAwait(false);

            int added = 0;
            foreach (AiSessionProcessCandidate candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                AiSessionRecord? existingDiscovery = FindExistingDiscoverySession(discoveredSessions, candidate);
                if (existingDiscovery is not null)
                {
                    await UpdateExistingDiscoverySessionAsync(
                        existingDiscovery,
                        candidate,
                        cancellationToken).ConfigureAwait(false);
                    completed += await CompleteDuplicateDiscoverySessionsAsync(
                        discoveredSessions,
                        existingDiscovery.Id,
                        candidate,
                        observedAt,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (FindExistingActiveSession(activeSessions, candidate) is not null)
                {
                    continue;
                }

                AiSessionRecord record = CreateRecord(candidate, observedAt);
                await _sessions.AddAsync(record, cancellationToken).ConfigureAwait(false);
                await AddEventAsync(
                    record.Id,
                    AiSessionEventType.Created,
                    observedAt,
                    BuildCreatedMessage(record, candidate),
                    cancellationToken).ConfigureAwait(false);
                await AddEventAsync(
                    record.Id,
                    AiSessionEventType.Started,
                    observedAt,
                    "Tracking started from Windows process discovery.",
                    cancellationToken).ConfigureAwait(false);

                discoveredSessions.Add(record);
                added++;
            }

            return new AiSessionDiscoveryResult(candidates.Count, added, completed);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <summary>Classifies a process snapshot as a trackable AI session process.</summary>
    public static AiSessionProcessCandidate? ClassifyProcess(
        AiSessionProcessSnapshot snapshot,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? executablePath = Clean(snapshot.ExecutablePath);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        string processName = NormalizeProcessName(snapshot.ProcessName);
        string fileName = Path.GetFileName(executablePath);
        string? commandLine = Clean(snapshot.CommandLine);
        DateTimeOffset startedAt = snapshot.StartedAt ?? observedAt;

        if (IsCodexRuntimeSession(executablePath, processName, fileName, commandLine))
        {
            string? workingDirectory = ReadCommandOption(commandLine, "--working-dir");
            string? sessionId = ReadCommandOption(commandLine, "--session-id");
            return Candidate(
                AiSessionProvider.Codex,
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? "Codex session"
                    : $"Codex - {Path.GetFileName(workingDirectory)}",
                snapshot,
                executablePath,
                startedAt,
                "codex-runtime-session",
                executablePath,
                workingDirectory,
                sessionId);
        }

        if (IsClaudeCode(executablePath, processName, fileName, commandLine))
        {
            string? workingDirectory = ReadCommandOption(commandLine, "--cwd") ??
                ReadCommandOption(commandLine, "--working-dir");
            return Candidate(
                AiSessionProvider.ClaudeCode,
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? "Claude Code"
                    : $"Claude Code - {Path.GetFileName(workingDirectory)}",
                snapshot,
                executablePath,
                startedAt,
                "claude-code",
                executablePath,
                workingDirectory);
        }

        if (IsOllama(processName, fileName))
        {
            // Only long-lived Ollama processes are sessions: the server itself
            // and per-model runner hosts. One-shot CLI calls (list, pull) are
            // ignored.
            if (CommandLineContainsWord(commandLine, "runner"))
            {
                string? modelPath = ReadCommandOption(commandLine, "--model");
                string model = string.IsNullOrWhiteSpace(modelPath)
                    ? "model"
                    : Path.GetFileNameWithoutExtension(modelPath);
                return Candidate(
                    AiSessionProvider.Ollama,
                    $"Ollama - {model}",
                    snapshot,
                    executablePath,
                    startedAt,
                    "ollama-runner",
                    executablePath);
            }

            if (CommandLineContainsWord(commandLine, "serve"))
            {
                return Candidate(
                    AiSessionProvider.Ollama,
                    "Ollama server",
                    snapshot,
                    executablePath,
                    startedAt,
                    "ollama-server",
                    executablePath);
            }

            return null;
        }

        if (IsCursorAgent(fileName, commandLine))
        {
            string? workingDirectory = ReadCommandOption(commandLine, "--cwd") ??
                ReadCommandOption(commandLine, "--working-dir") ??
                ReadCommandOption(commandLine, "--workspace");
            return Candidate(
                AiSessionProvider.Cursor,
                string.IsNullOrWhiteSpace(workingDirectory)
                    ? "Cursor agent"
                    : $"Cursor agent - {Path.GetFileName(workingDirectory)}",
                snapshot,
                executablePath,
                startedAt,
                "cursor-agent",
                executablePath,
                workingDirectory);
        }

        if (IsCopilotCli(fileName, commandLine))
        {
            return Candidate(
                AiSessionProvider.GitHubCopilot,
                "Copilot CLI",
                snapshot,
                executablePath,
                startedAt,
                "copilot-cli",
                executablePath);
        }

        if (IsGeminiCli(fileName, commandLine))
        {
            return Candidate(
                AiSessionProvider.Gemini,
                "Gemini CLI",
                snapshot,
                executablePath,
                startedAt,
                "gemini-cli",
                executablePath);
        }

        return null;
    }

    private static bool IsOllama(string processName, string fileName)
        => string.Equals(processName, "ollama", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "ollama.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsCursorAgent(string fileName, string? commandLine)
    {
        if (string.Equals(fileName, "cursor-agent.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // The npm/node distribution runs as node.exe with the package on the
        // command line.
        return IsNodeHost(fileName) && commandLine is not null &&
            (commandLine.Contains(@"\cursor-agent\", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains("/cursor-agent/", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"\cursor-agent.js", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsCopilotCli(string fileName, string? commandLine)
    {
        if (string.Equals(fileName, "copilot.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsNodeHost(fileName) && commandLine is not null &&
            (commandLine.Contains("@github/copilot", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"\github-copilot-cli\", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains("@githubnext/github-copilot-cli", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsGeminiCli(string fileName, string? commandLine)
    {
        if (string.Equals(fileName, "gemini.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IsNodeHost(fileName) && commandLine is not null &&
            (commandLine.Contains("@google/gemini-cli", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"\gemini-cli\", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains("/gemini-cli/", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsNodeHost(string fileName)
        => string.Equals(fileName, "node.exe", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "bun.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the command line contains the token as a standalone word.</summary>
    private static bool CommandLineContainsWord(string? commandLine, string word)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        int index = commandLine.IndexOf(word, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            bool startOk = index == 0 || !char.IsLetterOrDigit(commandLine[index - 1]);
            int end = index + word.Length;
            bool endOk = end >= commandLine.Length || !char.IsLetterOrDigit(commandLine[end]);
            if (startOk && endOk)
            {
                return true;
            }

            index = commandLine.IndexOf(word, end, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _scanGate.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        await ScanSafeAsync(cancellationToken).ConfigureAwait(false);

        using var timer = new PeriodicTimer(ScanInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await ScanSafeAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task ScanSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ScanOnceAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            LogScanFailed(ex);
        }
    }

    private static AiSessionCandidateScan EnumerateCandidateProcesses(DateTimeOffset observedAt)
    {
        var candidates = new List<AiSessionProcessCandidate>();
        foreach (AiSessionProcessSnapshot snapshot in EnumerateProcessSnapshots())
        {
            AiSessionProcessCandidate? candidate = ClassifyProcess(snapshot, observedAt);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }

        // A locked/unreadable Codex state DB must read as "state unknown", not
        // "no threads exist" — otherwise one transient failure completes every
        // tracked thread session and the next scan recreates them as duplicates.
        List<AiSessionProcessCandidate>? codexThreads = EnumerateCodexThreadCandidates(observedAt);
        HashSet<string>? inactiveCodexWorkspaces = EnumerateInactiveCodexThreadWorkspaces(observedAt);
        bool codexStateHealthy = codexThreads is not null && inactiveCodexWorkspaces is not null;
        if (codexThreads is not null)
        {
            candidates.AddRange(codexThreads);
        }

        HashSet<string> inactive = inactiveCodexWorkspaces ?? [];
        AiSessionProcessCandidate[] canonical = SelectCanonicalCandidates(DropChildClaudeCandidates(candidates), inactive)
            .GroupBy(c => c.ProcessKey, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToArray();
        return new AiSessionCandidateScan(canonical, codexStateHealthy, inactive);
    }

    /// <summary>
    /// Drops Claude Code candidates whose parent process is itself a Claude Code
    /// candidate in the same scan (launcher + node child, or spawned workers), so
    /// one session cannot appear as several overlay circles/rows.
    /// </summary>
    internal static IReadOnlyList<AiSessionProcessCandidate> DropChildClaudeCandidates(
        IReadOnlyList<AiSessionProcessCandidate> candidates)
    {
        var claudePids = candidates
            .Where(c => string.Equals(c.Detector, "claude-code", StringComparison.OrdinalIgnoreCase))
            .Where(c => c.Pid is not null)
            .Select(c => c.Pid!.Value)
            .ToHashSet();
        if (claudePids.Count < 2)
        {
            return candidates;
        }

        return candidates
            .Where(c => !(
                string.Equals(c.Detector, "claude-code", StringComparison.OrdinalIgnoreCase) &&
                c.ParentPid is int parent &&
                claudePids.Contains(parent)))
            .ToArray();
    }

    internal static IReadOnlyList<AiSessionProcessCandidate> SelectCanonicalCandidates(
        IReadOnlyList<AiSessionProcessCandidate> candidates,
        HashSet<string> inactiveCodexWorkspaces)
    {
        HashSet<string> codexThreadWorkspaces = candidates
            .Where(c => string.Equals(c.Detector, "codex-state-thread", StringComparison.OrdinalIgnoreCase))
            .Select(c => NormalizeWorkspacePath(c.WorkingDirectory))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (codexThreadWorkspaces.Count == 0 && inactiveCodexWorkspaces.Count == 0)
        {
            return candidates;
        }

        return candidates
            .Where(c => !(
                string.Equals(c.Detector, "codex-runtime-session", StringComparison.OrdinalIgnoreCase) &&
                (codexThreadWorkspaces.Contains(NormalizeWorkspacePath(c.WorkingDirectory)) ||
                    inactiveCodexWorkspaces.Contains(NormalizeWorkspacePath(c.WorkingDirectory)))))
            .ToArray();
    }

    /// <summary>Returns null when the Codex state DB exists but could not be read this pass.</summary>
    private static List<AiSessionProcessCandidate>? EnumerateCodexThreadCandidates(DateTimeOffset observedAt)
    {
        string? databasePath = FindLatestCodexStateDatabase();
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return [];
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT
                    t.id,
                    t.title,
                    t.cwd,
                    t.created_at,
                    t.updated_at,
                    t.archived,
                    t.rollout_path,
                    t.source,
                    t.model,
                    t.model_provider,
                    e.status AS spawn_status,
                    e.parent_thread_id,
                    p.updated_at AS parent_updated_at
                FROM threads t
                LEFT JOIN thread_spawn_edges e ON e.child_thread_id = t.id
                LEFT JOIN threads p ON p.id = e.parent_thread_id
                WHERE t.archived = 0
                  AND COALESCE(e.status, '') <> 'closed'
                  AND (
                      t.updated_at >= $cutoff
                      OR (
                          COALESCE(e.status, '') = 'open'
                          AND COALESCE(p.updated_at, 0) >= $cutoff
                      )
                  )
                ORDER BY t.updated_at DESC, t.id DESC
                LIMIT 30;
                """;
            command.Parameters.AddWithValue(
                "$cutoff",
                observedAt.Subtract(CodexThreadRecencyWindow).ToUnixTimeSeconds());

            var candidates = new List<AiSessionProcessCandidate>();
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                AiSessionCodexThreadRunSnapshot runState = ReadCodexThreadRunState(
                    ReadSqliteString(reader, "rollout_path"));
                var snapshot = new AiSessionCodexThreadSnapshot(
                    ReadSqliteString(reader, "id") ?? string.Empty,
                    ReadSqliteString(reader, "title"),
                    ReadSqliteString(reader, "cwd"),
                    ReadSqliteInt64(reader, "created_at") ?? observedAt.ToUnixTimeSeconds(),
                    ReadSqliteInt64(reader, "updated_at") ?? observedAt.ToUnixTimeSeconds(),
                    ReadSqliteInt64(reader, "archived") is > 0,
                    runState.State,
                    runState.ObservedAt,
                    ReadSqliteString(reader, "spawn_status"),
                    ReadSqliteString(reader, "parent_thread_id"),
                    ReadSqliteInt64(reader, "parent_updated_at"),
                    ReadSqliteString(reader, "source"),
                    ReadSqliteString(reader, "model"),
                    ReadSqliteString(reader, "model_provider"),
                    databasePath);

                AiSessionProcessCandidate? candidate = ClassifyCodexThread(snapshot, observedAt);
                if (candidate is not null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }
        catch (Exception ex) when (ex is SqliteException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Locked/busy DB: unknown state, not "no threads".
            return null;
        }
    }

    /// <summary>Returns null when the Codex state DB exists but could not be read this pass.</summary>
    private static HashSet<string>? EnumerateInactiveCodexThreadWorkspaces(DateTimeOffset observedAt)
    {
        string? databasePath = FindLatestCodexStateDatabase();
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            return [];
        }

        try
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
            };
            using var connection = new SqliteConnection(builder.ToString());
            connection.Open();

            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT cwd, rollout_path
                FROM threads
                WHERE archived = 0
                  AND cwd IS NOT NULL
                  AND updated_at >= $cutoff
                ORDER BY updated_at DESC, id DESC
                LIMIT 100;
                """;
            command.Parameters.AddWithValue(
                "$cutoff",
                observedAt.Subtract(CodexCompletedThreadWindow).ToUnixTimeSeconds());

            var seenWorkspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var inactiveWorkspaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using SqliteDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                string workspace = NormalizeWorkspacePath(ReadSqliteString(reader, "cwd"));
                if (string.IsNullOrWhiteSpace(workspace) || !seenWorkspaces.Add(workspace))
                {
                    continue;
                }

                if (IsInactiveCodexRunState(
                    ReadCodexThreadRunState(ReadSqliteString(reader, "rollout_path")),
                    observedAt))
                {
                    inactiveWorkspaces.Add(workspace);
                }
            }

            return inactiveWorkspaces;
        }
        catch (Exception ex) when (ex is SqliteException
            or IOException
            or UnauthorizedAccessException
            or InvalidOperationException)
        {
            // Locked/busy DB: unknown state, not "everything is inactive-free".
            return null;
        }
    }

    /// <summary>Classifies a Codex desktop thread row as an active session candidate.</summary>
    public static AiSessionProcessCandidate? ClassifyCodexThread(
        AiSessionCodexThreadSnapshot snapshot,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        string? threadId = Clean(snapshot.ThreadId);
        if (string.IsNullOrWhiteSpace(threadId) || snapshot.Archived)
        {
            return null;
        }

        if (string.Equals(snapshot.SpawnStatus, "closed", StringComparison.OrdinalIgnoreCase) ||
            snapshot.RunState != AiSessionCodexThreadRunState.Active)
        {
            return null;
        }

        DateTimeOffset startedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, snapshot.CreatedAtUnixSeconds));
        DateTimeOffset updatedAt = DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, snapshot.UpdatedAtUnixSeconds));
        DateTimeOffset? runStateObservedAt = snapshot.RunStateObservedAt;
        if (runStateObservedAt is null ||
            runStateObservedAt.Value < observedAt.Subtract(CodexActiveRolloutWindow))
        {
            return null;
        }

        bool isOpenSubagent = string.Equals(snapshot.SpawnStatus, "open", StringComparison.OrdinalIgnoreCase);
        DateTimeOffset cutoff = observedAt.Subtract(CodexThreadRecencyWindow);
        bool parentIsRecent = snapshot.ParentUpdatedAtUnixSeconds is long parentUpdatedAtUnixSeconds &&
            DateTimeOffset.FromUnixTimeSeconds(Math.Max(0, parentUpdatedAtUnixSeconds)) >= cutoff;
        if (updatedAt < cutoff && (!isOpenSubagent || !parentIsRecent))
        {
            return null;
        }

        string? workingDirectory = NormalizeWindowsPath(Clean(snapshot.Cwd));
        string title = BuildCodexThreadTitle(snapshot, workingDirectory);
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["codexThreadId"] = threadId,
            ["codexThreadUpdatedAt"] = updatedAt.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["codexThreadRunState"] = snapshot.RunState.ToString(),
            ["codexThreadRunStateAt"] = runStateObservedAt.Value.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture),
        };

        if (!string.IsNullOrWhiteSpace(snapshot.RawTitle))
        {
            metadata["codexThreadTitle"] = snapshot.RawTitle;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.SpawnStatus))
        {
            metadata["spawnStatus"] = snapshot.SpawnStatus;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ParentThreadId))
        {
            metadata["parentThreadId"] = snapshot.ParentThreadId;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Model))
        {
            metadata["model"] = snapshot.Model;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ModelProvider))
        {
            metadata["modelProvider"] = snapshot.ModelProvider;
        }

        return new AiSessionProcessCandidate(
            AiSessionProvider.Codex,
            title,
            $"Codex thread {threadId}",
            null,
            startedAt,
            $"Codex:thread:{threadId}",
            "codex-thread",
            snapshot.StateDatabasePath ?? "Codex state",
            null,
            "codex-state-thread",
            null,
            threadId,
            workingDirectory,
            runStateObservedAt,
            metadata);
    }

    private static bool IsInactiveCodexRunState(
        AiSessionCodexThreadRunSnapshot runState,
        DateTimeOffset observedAt)
        => runState.State != AiSessionCodexThreadRunState.Active ||
            runState.ObservedAt is null ||
            runState.ObservedAt.Value < observedAt.Subtract(CodexActiveRolloutWindow);

    private static string? FindLatestCodexStateDatabase()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            return null;
        }

        string codexDirectory = Path.Combine(userProfile, ".codex");
        if (!Directory.Exists(codexDirectory))
        {
            return null;
        }

        try
        {
            return Directory
                .EnumerateFiles(codexDirectory, "state_*.sqlite", SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or ArgumentException)
        {
            return null;
        }
    }

    private static AiSessionCodexThreadRunSnapshot ReadCodexThreadRunState(string? rolloutPath)
    {
        string? path = NormalizeWindowsPath(rolloutPath);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        try
        {
            const int tailByteLimit = 128 * 1024;
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            long offset = Math.Max(0, stream.Length - tailByteLimit);
            stream.Seek(offset, SeekOrigin.Begin);

            int bytesToRead = (int)(stream.Length - offset);
            byte[] buffer = new byte[bytesToRead];
            int read = stream.Read(buffer, 0, buffer.Length);
            string text = Encoding.UTF8.GetString(buffer, 0, read);
            string[] lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            for (int index = lines.Length - 1; index >= 0; index--)
            {
                // Codex is appending to this file while we read it; the last
                // line is frequently torn mid-write. One unparseable line must
                // not abort the whole classification (that made live threads
                // flap to Completed and get re-created as duplicate sessions).
                AiSessionCodexThreadRunSnapshot state;
                try
                {
                    state = ClassifyCodexRolloutLine(lines[index]);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (state.State != AiSessionCodexThreadRunState.Unknown)
                {
                    return state;
                }
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or ArgumentException
            or NotSupportedException)
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        return AiSessionCodexThreadRunSnapshot.Unknown;
    }

    private static AiSessionCodexThreadRunSnapshot ClassifyCodexRolloutLine(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        string? type = ReadJsonString(root, "type");
        if (!root.TryGetProperty("payload", out JsonElement payload))
        {
            return AiSessionCodexThreadRunSnapshot.Unknown;
        }

        string? payloadType = ReadJsonString(payload, "type");
        DateTimeOffset? timestamp = ReadJsonTimestamp(root, "timestamp");
        if (string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(payloadType, "task_complete", StringComparison.OrdinalIgnoreCase))
        {
            return new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Completed, timestamp);
        }

        if (string.Equals(type, "response_item", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(payloadType, "function_call", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payloadType, "function_call_output", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payloadType, "message", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(payloadType, "reasoning", StringComparison.OrdinalIgnoreCase)))
        {
            return new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Active, timestamp);
        }

        if (string.Equals(type, "event_msg", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(payloadType, "agent_message", StringComparison.OrdinalIgnoreCase))
        {
            return new AiSessionCodexThreadRunSnapshot(AiSessionCodexThreadRunState.Active, timestamp);
        }

        return AiSessionCodexThreadRunSnapshot.Unknown;
    }

    private static string? ReadJsonString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static DateTimeOffset? ReadJsonTimestamp(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                property.GetString(),
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out DateTimeOffset timestamp)
            ? timestamp
            : null;

    private static string BuildCodexThreadTitle(AiSessionCodexThreadSnapshot snapshot, string? workingDirectory)
    {
        string? nickname = TryReadSubagentNickname(snapshot.Source);
        if (!string.IsNullOrWhiteSpace(nickname))
        {
            return $"Codex subagent - {nickname}";
        }

        string folder = WorkspaceLabel(workingDirectory);
        return string.IsNullOrWhiteSpace(folder)
            ? "Codex session"
            : $"Codex - {folder}";
    }

    private static string? TryReadSubagentNickname(string? sourceJson)
    {
        if (string.IsNullOrWhiteSpace(sourceJson) ||
            !sourceJson.TrimStart().StartsWith('{'))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(sourceJson);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("subagent", out JsonElement subagent) &&
                subagent.TryGetProperty("thread_spawn", out JsonElement spawn) &&
                spawn.TryGetProperty("agent_nickname", out JsonElement nickname) &&
                nickname.ValueKind == JsonValueKind.String)
            {
                return Clean(nickname.GetString());
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static string WorkspaceLabel(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string clean = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(clean);
        }
        catch (ArgumentException)
        {
            return path;
        }
    }

    private static string NormalizeWorkspacePath(string? path)
        => NormalizeWindowsPath(path) ?? string.Empty;

    private static string? NormalizeWindowsPath(string? path)
    {
        string? clean = Clean(path);
        if (string.IsNullOrWhiteSpace(clean))
        {
            return null;
        }

        if (clean.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            clean = clean[4..];
        }

        return clean.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? ReadSqliteString(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : Clean(reader.GetString(ordinal));
    }

    private static long? ReadSqliteInt64(SqliteDataReader reader, string columnName)
    {
        int ordinal = reader.GetOrdinal(columnName);
        return reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);
    }

    private static IReadOnlyList<AiSessionProcessSnapshot> EnumerateProcessSnapshots()
    {
        IReadOnlyList<AiSessionProcessSnapshot> wmiSnapshots = TryReadWmiProcessSnapshots();
        if (wmiSnapshots.Count > 0)
        {
            IReadOnlyDictionary<int, string> windowTitles = ReadMainWindowTitles();
            return wmiSnapshots
                .Select(s => s with
                {
                    MainWindowTitle = windowTitles.GetValueOrDefault(s.Pid) ?? s.MainWindowTitle,
                })
                .ToArray();
        }

        return Process.GetProcesses()
            .Select(TryCreateSnapshot)
            .Where(s => s is not null)
            .Cast<AiSessionProcessSnapshot>()
            .ToArray();
    }

    private static IReadOnlyList<AiSessionProcessSnapshot> TryReadWmiProcessSnapshots()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, ExecutablePath, CommandLine, CreationDate FROM Win32_Process");
            using ManagementObjectCollection objects = searcher.Get();

            var snapshots = new List<AiSessionProcessSnapshot>();
            foreach (ManagementObject process in objects.Cast<ManagementObject>())
            {
                using (process)
                {
                    if (TryReadUInt(process, "ProcessId") is not uint processId)
                    {
                        continue;
                    }

                    snapshots.Add(new AiSessionProcessSnapshot(
                        checked((int)processId),
                        TryReadString(process, "Name") ?? string.Empty,
                        TryReadString(process, "ExecutablePath"),
                        null,
                        TryReadWmiDateTime(process, "CreationDate"),
                        TryReadUInt(process, "ParentProcessId") is uint parentPid
                            ? checked((int)parentPid)
                            : null,
                        TryReadString(process, "CommandLine")));
                }
            }

            return snapshots;
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static IReadOnlyDictionary<int, string> ReadMainWindowTitles()
    {
        var titles = new Dictionary<int, string>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                string? title = TryGetMainWindowTitle(process);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    titles[process.Id] = title;
                }
            }
        }

        return titles;
    }

    private static AiSessionProcessSnapshot? TryCreateSnapshot(Process process)
    {
        string processName;
        try
        {
            processName = process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }

        return new AiSessionProcessSnapshot(
            process.Id,
            processName,
            TryGetExecutablePath(process),
            TryGetMainWindowTitle(process),
            TryGetStartTime(process),
            null,
            null);
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }

    private static string? TryGetMainWindowTitle(Process process)
    {
        try
        {
            return process.MainWindowTitle;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }

    private static DateTimeOffset? TryGetStartTime(Process process)
    {
        try
        {
            return new DateTimeOffset(process.StartTime);
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or System.ComponentModel.Win32Exception
            or NotSupportedException)
        {
            return null;
        }
    }

    private async Task<int> CompleteMissingProcessesAsync(
        IReadOnlyList<AiSessionRecord> discoveredSessions,
        HashSet<string> activeProcessKeys,
        HashSet<string> codexStateWorkspaces,
        HashSet<string> inactiveCodexWorkspaces,
        bool codexStateHealthy,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (AiSessionRecord session in discoveredSessions)
        {
            string? processKey = ReadMetadataString(session.MetadataJson, "processKey");
            if (!string.IsNullOrWhiteSpace(processKey) &&
                activeProcessKeys.Contains(processKey))
            {
                continue;
            }

            // When the Codex state DB could not be read this pass, thread rows
            // are missing for an unknown reason — leave them untouched instead
            // of completing them and re-creating duplicates next pass.
            if (!codexStateHealthy &&
                string.Equals(
                    ReadMetadataString(session.MetadataJson, "detector"),
                    "codex-state-thread",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (codexStateHealthy &&
                (ShouldCompleteSupersededCodexRuntimeSession(session, codexStateWorkspaces) ||
                    ShouldCompleteInactiveCodexRuntimeSession(session, inactiveCodexWorkspaces)))
            {
                await _sessions.UpdateAsync(session with
                {
                    Status = AiSessionStatus.Completed,
                    EndedAt = observedAt,
                    LastEventAt = observedAt,
                }, cancellationToken).ConfigureAwait(false);
                await AddEventAsync(
                    session.Id,
                    AiSessionEventType.Completed,
                    observedAt,
                    "Merged into Codex desktop thread discovery.",
                    cancellationToken).ConfigureAwait(false);
                completed++;
                continue;
            }

            if (!ShouldCompleteMissingSession(session))
            {
                continue;
            }

            await _sessions.UpdateAsync(session with
            {
                Status = AiSessionStatus.Completed,
                EndedAt = observedAt,
                LastEventAt = observedAt,
            }, cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                session.Id,
                AiSessionEventType.Completed,
                observedAt,
                "Discovered process is no longer running.",
                cancellationToken).ConfigureAwait(false);
            completed++;
        }

        return completed;
    }

    private static bool ShouldCompleteSupersededCodexRuntimeSession(
        AiSessionRecord session,
        HashSet<string> codexStateWorkspaces)
    {
        if (codexStateWorkspaces.Count == 0 ||
            session.Provider != AiSessionProvider.Codex ||
            !string.Equals(
                ReadMetadataString(session.MetadataJson, "detector"),
                "codex-runtime-session",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string workspace = NormalizeWorkspacePath(
            session.Cwd ?? ReadMetadataString(session.MetadataJson, "workingDirectory"));
        return !string.IsNullOrWhiteSpace(workspace) &&
            codexStateWorkspaces.Contains(workspace);
    }

    private static bool ShouldCompleteInactiveCodexRuntimeSession(
        AiSessionRecord session,
        HashSet<string> inactiveCodexWorkspaces)
    {
        if (inactiveCodexWorkspaces.Count == 0 ||
            session.Provider != AiSessionProvider.Codex ||
            !string.Equals(
                ReadMetadataString(session.MetadataJson, "detector"),
                "codex-runtime-session",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string workspace = NormalizeWorkspacePath(
            session.Cwd ?? ReadMetadataString(session.MetadataJson, "workingDirectory"));
        return !string.IsNullOrWhiteSpace(workspace) &&
            inactiveCodexWorkspaces.Contains(workspace);
    }

    private static bool ShouldCompleteMissingSession(AiSessionRecord session)
    {
        string? detector = ReadMetadataString(session.MetadataJson, "detector");
        if (string.Equals(detector, "codex-app-server", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(detector, "codex-desktop", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (session.Pid is not int pid)
        {
            return true;
        }

        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
            {
                return true;
            }

            string? startTicks = ReadMetadataString(session.MetadataJson, "processStartTicks") ??
                ReadStartTicksFromProcessKey(ReadMetadataString(session.MetadataJson, "processKey"));
            DateTimeOffset? liveStart = TryGetStartTime(process);

            // The candidate vanished from the scan but a process still holds
            // the PID. If we cannot even read that process's start time it is
            // almost certainly an elevated/system process that reused the PID —
            // our user-level dev tools are always readable. Treat as exited so
            // the session does not stay "live" for days.
            if (liveStart is null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(startTicks) &&
                !StartTicksClose(startTicks, liveStart.Value.UtcTicks))
            {
                return true;
            }

            return false;
        }
        catch (Exception ex) when (ex is ArgumentException
            or InvalidOperationException
            or System.ComponentModel.Win32Exception)
        {
            return true;
        }
    }

    /// <summary>
    /// Completes non-discovery (run/watch) sessions whose watched PID is gone.
    /// Re-fetches each row first so a completion raced by the in-memory exit
    /// monitor is not double-written.
    /// </summary>
    private async Task<int> ReconcileOrphanedTrackedSessionsAsync(
        IReadOnlyList<AiSessionRecord> trackedSessions,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (AiSessionRecord session in trackedSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Conservative: only PID-backed rows can be reconciled generically.
            if (session.Pid is not int || !ShouldCompleteMissingSession(session))
            {
                continue;
            }

            AiSessionRecord? current = await _sessions.GetAsync(session.Id, cancellationToken).ConfigureAwait(false);
            if (current is null || !ActiveStatuses.Contains(current.Status))
            {
                continue;
            }

            await _sessions.UpdateAsync(current with
            {
                Status = AiSessionStatus.Completed,
                EndedAt = observedAt,
                LastEventAt = observedAt,
            }, cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                current.Id,
                AiSessionEventType.Completed,
                observedAt,
                "The tracked process is no longer running (reconciled by discovery).",
                cancellationToken).ConfigureAwait(false);
            completed++;
        }

        return completed;
    }

    private static AiSessionRecord? FindExistingDiscoverySession(
        IReadOnlyList<AiSessionRecord> discoveredSessions,
        AiSessionProcessCandidate candidate)
    {
        AiSessionRecord? sameProcess = null;
        foreach (AiSessionRecord session in discoveredSessions)
        {
            string? processKey = ReadMetadataString(session.MetadataJson, "processKey");
            if (string.Equals(processKey, candidate.ProcessKey, StringComparison.OrdinalIgnoreCase))
            {
                return session;
            }

            if (sameProcess is null && IsSameProcessIdentity(session, candidate))
            {
                sameProcess = session;
            }
        }

        return sameProcess;
    }

    private async Task<int> CompleteDuplicateDiscoverySessionsAsync(
        IReadOnlyList<AiSessionRecord> discoveredSessions,
        Guid canonicalId,
        AiSessionProcessCandidate candidate,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken)
    {
        var completed = 0;
        foreach (AiSessionRecord duplicate in discoveredSessions)
        {
            if (duplicate.Id == canonicalId ||
                !IsSameProcessIdentity(duplicate, candidate))
            {
                continue;
            }

            await _sessions.UpdateAsync(duplicate with
            {
                Status = AiSessionStatus.Completed,
                EndedAt = observedAt,
                LastEventAt = observedAt,
            }, cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                duplicate.Id,
                AiSessionEventType.Completed,
                observedAt,
                "Merged into refreshed process-discovery tracking.",
                cancellationToken).ConfigureAwait(false);
            completed++;
        }

        return completed;
    }

    private static bool IsSameProcessIdentity(AiSessionRecord session, AiSessionProcessCandidate candidate)
    {
        if (session.Pid is null || candidate.Pid is null)
        {
            return false;
        }

        if (session.Provider != candidate.Provider ||
            session.Pid != candidate.Pid)
        {
            return false;
        }

        string? sessionStartTicks = ReadMetadataString(session.MetadataJson, "processStartTicks") ??
            ReadStartTicksFromProcessKey(ReadMetadataString(session.MetadataJson, "processKey"));
        return string.IsNullOrWhiteSpace(sessionStartTicks) ||
            StartTicksClose(sessionStartTicks, candidate.StartedAt.UtcTicks);
    }

    private static AiSessionRecord? FindExistingActiveSession(
        IReadOnlyList<AiSessionRecord> activeSessions,
        AiSessionProcessCandidate candidate)
        => candidate.Pid is null
            ? null
            : activeSessions.FirstOrDefault(session =>
            session.Pid == candidate.Pid.Value &&
            !IsDiscoverySession(session));

    private async Task UpdateExistingDiscoverySessionAsync(
        AiSessionRecord existing,
        AiSessionProcessCandidate candidate,
        CancellationToken cancellationToken)
    {
        string metadata = BuildMetadata(candidate);
        DateTimeOffset? nextLastEventAt = string.Equals(
            candidate.Detector,
            "codex-state-thread",
            StringComparison.OrdinalIgnoreCase)
            ? candidate.LastActivityAt
            : existing.LastEventAt;
        if (existing.Provider == candidate.Provider &&
            string.Equals(existing.Title, candidate.Title, StringComparison.Ordinal) &&
            string.Equals(existing.Cwd, candidate.WorkingDirectory, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.Command, candidate.Command, StringComparison.Ordinal) &&
            string.Equals(existing.MetadataJson, metadata, StringComparison.Ordinal) &&
            NullableDateTimesEqual(existing.LastEventAt, nextLastEventAt))
        {
            return;
        }

        await _sessions.UpdateAsync(existing with
        {
            Provider = candidate.Provider,
            Title = candidate.Title,
            Cwd = candidate.WorkingDirectory,
            Command = candidate.Command,
            MetadataJson = metadata,
            LastEventAt = nextLastEventAt,
        }, cancellationToken).ConfigureAwait(false);
    }

    private AiSessionRecord CreateRecord(AiSessionProcessCandidate candidate, DateTimeOffset observedAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Provider = candidate.Provider,
            Title = candidate.Title,
            Cwd = candidate.WorkingDirectory,
            Command = candidate.Command,
            Pid = candidate.Pid,
            Status = AiSessionStatus.Running,
            StartedAt = candidate.StartedAt,
            LastEventAt = candidate.LastActivityAt ?? observedAt,
            NotificationMode = AiSessionNotificationMode.Silent,
            MetadataJson = BuildMetadata(candidate),
        };

    private static string BuildCreatedMessage(AiSessionRecord record, AiSessionProcessCandidate candidate)
        => candidate.Pid is int pid
            ? $"Discovered running {record.Title} process (PID {pid})."
            : $"Discovered running {record.Title} session.";

    private static bool NullableDateTimesEqual(DateTimeOffset? left, DateTimeOffset? right)
        => left is null && right is null ||
            left is not null &&
            right is not null &&
            left.Value.Equals(right.Value);

    private Task AddEventAsync(
        Guid sessionId,
        AiSessionEventType eventType,
        DateTimeOffset createdAt,
        string message,
        CancellationToken cancellationToken)
        => _sessions.AddEventAsync(new AiSessionEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            EventType = eventType,
            CreatedAt = createdAt,
            Message = message,
        }, cancellationToken);

    private static string BuildMetadata(AiSessionProcessCandidate candidate)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["source"] = DiscoverySource,
            ["processKey"] = candidate.ProcessKey,
            ["processStartTicks"] = candidate.StartedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["processName"] = candidate.ProcessName,
            ["executablePath"] = candidate.ExecutablePath,
            ["detector"] = candidate.Detector,
        };

        if (!string.IsNullOrWhiteSpace(candidate.MainWindowTitle))
        {
            metadata["mainWindowTitle"] = candidate.MainWindowTitle;
        }

        if (candidate.ParentPid is not null)
        {
            metadata["parentPid"] = candidate.ParentPid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        if (!string.IsNullOrWhiteSpace(candidate.SessionId))
        {
            metadata["sessionId"] = candidate.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(candidate.WorkingDirectory))
        {
            metadata["workingDirectory"] = candidate.WorkingDirectory;
        }

        if (candidate.ExtraMetadata is not null)
        {
            foreach (KeyValuePair<string, string> item in candidate.ExtraMetadata)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) &&
                    !string.IsNullOrWhiteSpace(item.Value))
                {
                    metadata[item.Key] = item.Value;
                }
            }
        }

        return JsonSerializer.Serialize(metadata);
    }

    private static bool IsDiscoverySession(AiSessionRecord record)
        => string.Equals(
            ReadMetadataString(record.MetadataJson, "source"),
            DiscoverySource,
            StringComparison.OrdinalIgnoreCase);

    private static string? ReadMetadataString(string? metadataJson, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(metadataJson);
            return document.RootElement.TryGetProperty(propertyName, out JsonElement property) &&
                property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadStartTicksFromProcessKey(string? processKey)
    {
        if (string.IsNullOrWhiteSpace(processKey))
        {
            return null;
        }

        int index = processKey.LastIndexOf(':');
        return index >= 0 && index < processKey.Length - 1
            ? processKey[(index + 1)..]
            : null;
    }

    private static bool StartTicksClose(string startTicks, long expectedUtcTicks)
    {
        if (!long.TryParse(
            startTicks,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture,
            out long parsed))
        {
            return true;
        }

        return Math.Abs(parsed - expectedUtcTicks) <= TimeSpan.TicksPerSecond;
    }

    private static bool IsCodexRuntimeSession(
        string executablePath,
        string processName,
        string fileName,
        string? commandLine)
        => ProcessNameEquals(processName, "node") &&
            FileNameEquals(fileName, "node.exe") &&
            executablePath.Contains(@"\OpenAI\Codex\runtimes\", StringComparison.OrdinalIgnoreCase) &&
            commandLine?.Contains("--session-id", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsClaudeCode(
        string executablePath,
        string processName,
        string fileName,
        string? commandLine)
    {
        if (commandLine?.Contains("--chrome-native-host", StringComparison.OrdinalIgnoreCase) == true)
        {
            return false;
        }

        if (ProcessNameEquals(processName, "claude") &&
            FileNameEquals(fileName, "claude.exe"))
        {
            return executablePath.Contains(@"\Claude\claude-code\", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains(@"\@anthropic-ai\claude-code\bin\claude.exe", StringComparison.OrdinalIgnoreCase) ||
                executablePath.Contains(@"\node_modules\@anthropic-ai\claude-code\bin\claude.exe", StringComparison.OrdinalIgnoreCase);
        }

        return ProcessNameEquals(processName, "node") &&
            FileNameEquals(fileName, "node.exe") &&
            commandLine is not null &&
            (commandLine.Contains(@"\@anthropic-ai\claude-code\", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"/@anthropic-ai/claude-code/", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"\node_modules\@anthropic-ai\claude-code\", StringComparison.OrdinalIgnoreCase) ||
                commandLine.Contains(@"/node_modules/@anthropic-ai/claude-code/", StringComparison.OrdinalIgnoreCase));
    }

    private static AiSessionProcessCandidate Candidate(
        AiSessionProvider provider,
        string title,
        AiSessionProcessSnapshot snapshot,
        string executablePath,
        DateTimeOffset startedAt,
        string detector,
        string? command = null,
        string? workingDirectory = null,
        string? sessionId = null)
    {
        string normalizedProcessName = NormalizeProcessName(snapshot.ProcessName);
        return new AiSessionProcessCandidate(
            provider,
            title,
            command ?? executablePath,
            snapshot.Pid,
            startedAt,
            BuildProcessKey(provider, snapshot.Pid, snapshot.StartedAt),
            normalizedProcessName,
            executablePath,
            Clean(snapshot.MainWindowTitle),
            detector,
            snapshot.ParentPid,
            sessionId,
            workingDirectory,
            null,
            null);
    }

    private static string BuildProcessKey(
        AiSessionProvider provider,
        int? pid,
        DateTimeOffset? startedAt)
        => $"{provider}:{pid}:{(startedAt?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown")}";

    private static bool ProcessNameEquals(string processName, string expected)
        => string.Equals(NormalizeProcessName(processName), expected, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProcessName(string? processName)
    {
        string clean = Clean(processName) ?? string.Empty;
        return clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? clean[..^4]
            : clean;
    }

    private static bool FileNameEquals(string? fileName, string expected)
        => string.Equals(fileName, expected, StringComparison.OrdinalIgnoreCase);

    private static string? Clean(string? text)
        => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static string? ReadCommandOption(string? commandLine, string option)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        int index = commandLine.IndexOf(option, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        index += option.Length;
        while (index < commandLine.Length && char.IsWhiteSpace(commandLine[index]))
        {
            index++;
        }

        if (index >= commandLine.Length)
        {
            return null;
        }

        if (commandLine[index] == '"')
        {
            int endQuote = commandLine.IndexOf('"', index + 1);
            return endQuote > index
                ? commandLine[(index + 1)..endQuote]
                : null;
        }

        int end = index;
        while (end < commandLine.Length && !char.IsWhiteSpace(commandLine[end]))
        {
            end++;
        }

        return commandLine[index..end];
    }

    private static uint? TryReadUInt(ManagementBaseObject process, string propertyName)
        => process.Properties[propertyName]?.Value switch
        {
            uint value => value,
            int value and >= 0 => checked((uint)value),
            _ => null,
        };

    private static string? TryReadString(ManagementBaseObject process, string propertyName)
        => Clean(process.Properties[propertyName]?.Value as string);

    private static DateTimeOffset? TryReadWmiDateTime(ManagementBaseObject process, string propertyName)
    {
        string? raw = TryReadString(process, propertyName);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(raw));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "AI session process discovery scan failed.")]
    private partial void LogScanFailed(Exception exception);
}

/// <summary>Result counters from one AI process discovery pass.</summary>
public sealed record AiSessionDiscoveryResult(int DetectedCount, int AddedCount, int CompletedCount);

/// <summary>Minimal process snapshot used by the process classifier.</summary>
public sealed record AiSessionProcessSnapshot(
    int Pid,
    string ProcessName,
    string? ExecutablePath,
    string? MainWindowTitle,
    DateTimeOffset? StartedAt,
    int? ParentPid,
    string? CommandLine);

/// <summary>Minimal Codex desktop thread metadata used by state-db discovery.</summary>
public sealed record AiSessionCodexThreadSnapshot(
    string ThreadId,
    string? RawTitle,
    string? Cwd,
    long CreatedAtUnixSeconds,
    long UpdatedAtUnixSeconds,
    bool Archived,
    AiSessionCodexThreadRunState RunState,
    DateTimeOffset? RunStateObservedAt,
    string? SpawnStatus,
    string? ParentThreadId,
    long? ParentUpdatedAtUnixSeconds,
    string? Source,
    string? Model,
    string? ModelProvider,
    string? StateDatabasePath);

/// <summary>Best-effort state and timestamp inferred from a Codex desktop rollout log.</summary>
public sealed record AiSessionCodexThreadRunSnapshot(
    AiSessionCodexThreadRunState State,
    DateTimeOffset? ObservedAt)
{
    public static AiSessionCodexThreadRunSnapshot Unknown { get; } =
        new(AiSessionCodexThreadRunState.Unknown, null);
}

/// <summary>Best-effort state inferred from Codex desktop rollout logs.</summary>
public enum AiSessionCodexThreadRunState
{
    Unknown,
    Active,
    Completed,
}

/// <summary>A classified AI process ready to be persisted as an active session.</summary>
/// <summary>One discovery pass: canonical candidates plus Codex state-DB health.</summary>
/// <param name="Candidates">The deduplicated candidates observed this pass.</param>
/// <param name="CodexStateHealthy">False when the Codex state DB exists but could not be read.</param>
/// <param name="InactiveCodexWorkspaces">Workspaces whose latest thread is idle/complete (empty when unhealthy).</param>
internal sealed record AiSessionCandidateScan(
    IReadOnlyList<AiSessionProcessCandidate> Candidates,
    bool CodexStateHealthy,
    HashSet<string> InactiveCodexWorkspaces);

public sealed record AiSessionProcessCandidate(
    AiSessionProvider Provider,
    string Title,
    string Command,
    int? Pid,
    DateTimeOffset StartedAt,
    string ProcessKey,
    string ProcessName,
    string ExecutablePath,
    string? MainWindowTitle,
    string Detector,
    int? ParentPid,
    string? SessionId,
    string? WorkingDirectory,
    DateTimeOffset? LastActivityAt,
    IReadOnlyDictionary<string, string>? ExtraMetadata);
