using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// First app-layer slice for Active AI Sessions: persist a generic watched
/// command or existing PID, then update the session when the process exits.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class AiSessionCommandService
{
    private const int MaxTitleLength = 96;
    private const int MaxOutputEvents = 200;
    private const int MaxOutputLineLength = 2000;
    internal const string AiSessionLogsFolder = "AiSessionLogs";

    private readonly IAiSessionRepository _sessions;
    private readonly INotificationService _notifications;
    private readonly IWindowPresenter _presenter;
    private readonly IStoragePaths _paths;
    private readonly IClock _clock;
    private readonly ILogger<AiSessionCommandService> _logger;

    /// <summary>Creates the session command service.</summary>
    public AiSessionCommandService(
        IAiSessionRepository sessions,
        INotificationService notifications,
        IWindowPresenter presenter,
        IStoragePaths paths,
        IClock clock,
        ILogger<AiSessionCommandService> logger)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts and watches a generic command from <c>octadock run -- ...</c>.</summary>
    public async Task<CommandResult> RunAsync(
        OctadockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        string? commandLine = command.WatchedCommand;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return CommandResult.Fail("run requires a command after '--'.");
        }

        if (!TryResolveWorkingDirectory(command.WorkingDirectory, out string cwd, out string? cwdError))
        {
            return CommandResult.Fail(cwdError!);
        }

        DateTimeOffset createdAt = _clock.UtcNow;
        var record = new AiSessionRecord
        {
            Id = Guid.NewGuid(),
            Provider = AiSessionProvider.Generic,
            Title = ResolveTitle(command.Title, commandLine),
            Cwd = cwd,
            Command = commandLine,
            Pid = null,
            Status = AiSessionStatus.Queued,
            StartedAt = createdAt,
            LastEventAt = createdAt,
            NotificationMode = ResolveNotificationMode(command),
            MetadataJson = Metadata("run"),
        };

        await _sessions.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await AddEventAsync(
            record.Id,
            AiSessionEventType.Created,
            createdAt,
            "Queued watched command.",
            cancellationToken).ConfigureAwait(false);

        WatchedProcess watched;
        try
        {
            ProcessStartInfo startInfo = BuildStartInfo(command, commandLine, cwd);
            watched = StartProcessWithOutputCapture(record.Id, startInfo, createdAt);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            DateTimeOffset failedAt = _clock.UtcNow;
            await _sessions.UpdateAsync(record with
            {
                Status = AiSessionStatus.Failed,
                EndedAt = failedAt,
                LastEventAt = failedAt,
            }, cancellationToken).ConfigureAwait(false);
            await AddEventAsync(
                record.Id,
                AiSessionEventType.Error,
                failedAt,
                $"Failed to start command: {ex.Message}",
                cancellationToken).ConfigureAwait(false);

            return CommandResult.Fail($"Could not start watched command: {ex.Message}");
        }

        DateTimeOffset startedAt = _clock.UtcNow;
        var running = record with
        {
            Pid = watched.Process.Id,
            Status = AiSessionStatus.Running,
            LastEventAt = startedAt,
        };
        await _sessions.UpdateAsync(running, cancellationToken).ConfigureAwait(false);
        await AddEventAsync(
            running.Id,
            AiSessionEventType.Started,
            startedAt,
            $"Started PID {watched.Process.Id}.",
            cancellationToken).ConfigureAwait(false);
        await AddLogArtifactsAsync(running.Id, watched.OutputCapture, startedAt, cancellationToken)
            .ConfigureAwait(false);

        _ = MonitorProcessExitAsync(running.Id, watched.Process, watched.OutputCapture);

        return new CommandResult(
            true,
            $"Started watched command as AI session {running.Id} (PID {watched.Process.Id}).");
    }

    /// <summary>Attaches to an existing process from <c>octadock watch --pid</c>.</summary>
    public async Task<CommandResult> WatchPidAsync(
        OctadockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        int? pid = command.WatchedPid;
        if (pid is null or < 1)
        {
            return CommandResult.Fail("watch requires a positive '--pid' value.");
        }

        Process process;
        try
        {
            process = Process.GetProcessById(pid.Value);
            if (process.HasExited)
            {
                process.Dispose();
                return CommandResult.Fail($"PID {pid.Value} is not running.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return CommandResult.Fail($"PID {pid.Value} is not running.");
        }

        DateTimeOffset startedAt = _clock.UtcNow;
        string processName = TryGetProcessName(process);
        var record = new AiSessionRecord
        {
            Id = Guid.NewGuid(),
            Provider = AiSessionProvider.Generic,
            Title = ResolveTitle(command.Title, command.WatchedCommand ?? processName),
            Cwd = string.IsNullOrWhiteSpace(command.WorkingDirectory) ? null : command.WorkingDirectory,
            Command = command.WatchedCommand,
            Pid = pid.Value,
            Status = AiSessionStatus.Running,
            StartedAt = startedAt,
            LastEventAt = startedAt,
            NotificationMode = ResolveNotificationMode(command),
            MetadataJson = Metadata("watch-pid"),
        };

        await _sessions.AddAsync(record, cancellationToken).ConfigureAwait(false);
        await AddEventAsync(
            record.Id,
            AiSessionEventType.Created,
            startedAt,
            $"Watching PID {pid.Value}.",
            cancellationToken).ConfigureAwait(false);
        await AddEventAsync(
            record.Id,
            AiSessionEventType.Started,
            startedAt,
            $"Attached to {processName}.",
            cancellationToken).ConfigureAwait(false);

        _ = MonitorProcessExitAsync(record.Id, process);

        return new CommandResult(
            true,
            $"Watching PID {pid.Value} as AI session {record.Id}.");
    }

    /// <summary>Ingests a local hook event for an existing AI session.</summary>
    public async Task<CommandResult> AddHookEventAsync(
        OctadockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Guid.TryParse(command.AiSessionId, out Guid sessionId))
        {
            return CommandResult.Fail("ai-session-event requires a valid --session-id GUID.");
        }

        if (!TryResolveEventType(command.AiSessionEvent, out AiSessionEventType eventType))
        {
            return CommandResult.Fail($"Unsupported AI session event '{command.AiSessionEvent}'.");
        }

        if (!TryResolveOptionalStatus(command.Get("status"), out AiSessionStatus? status, out string? statusError))
        {
            return CommandResult.Fail(statusError!);
        }

        status ??= InferStatusFromEvent(eventType);
        if (eventType == AiSessionEventType.StatusChanged && status is null)
        {
            return CommandResult.Fail("ai-session-event with event 'status-changed' requires --status.");
        }

        if (!TryResolveExitCode(command, out int? exitCode, out string? exitCodeError))
        {
            return CommandResult.Fail(exitCodeError!);
        }

        if (!TryResolveMetadata(command, out string? metadataJson, out string? metadataError))
        {
            return CommandResult.Fail(metadataError!);
        }

        AiSessionRecord? existing = await _sessions.GetAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return CommandResult.Fail($"AI session '{sessionId}' was not found.");
        }

        DateTimeOffset now = _clock.UtcNow;
        if (status is { } requestedStatus)
        {
            if (!CanApplyHookStatus(existing, requestedStatus, out string? statusApplyError))
            {
                return CommandResult.Fail(statusApplyError!);
            }

            existing = ApplyHookStatus(existing, requestedStatus, exitCode, now);
            await _sessions.UpdateAsync(existing, cancellationToken).ConfigureAwait(false);
        }

        await AddEventAsync(
            sessionId,
            eventType,
            now,
            ResolveHookMessage(command.Get("message"), eventType, status),
            cancellationToken,
            metadataJson).ConfigureAwait(false);

        NotifyHookStatus(existing, status, exitCode);
        return new CommandResult(true, $"Recorded AI session event '{eventType}' for {sessionId}.");
    }

    private async Task MonitorProcessExitAsync(
        Guid sessionId,
        Process process,
        OutputCaptureState? outputCapture = null)
    {
        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            process.WaitForExit();
            outputCapture?.Dispose();
            int? exitCode = TryGetExitCode(process);
            DateTimeOffset endedAt = _clock.UtcNow;
            AiSessionStatus status = exitCode is null or 0
                ? AiSessionStatus.Completed
                : AiSessionStatus.Failed;

            AiSessionRecord? existing = await _sessions.GetAsync(sessionId).ConfigureAwait(false);
            if (existing is null)
            {
                return;
            }

            await _sessions.UpdateAsync(existing with
            {
                Status = status,
                EndedAt = endedAt,
                ExitCode = exitCode,
                LastEventAt = endedAt,
            }).ConfigureAwait(false);

            string message = exitCode is null
                ? "Process exited."
                : $"Process exited with code {exitCode.Value}.";
            await AddEventAsync(
                sessionId,
                AiSessionEventType.Completed,
                endedAt,
                message,
                CancellationToken.None).ConfigureAwait(false);

            NotifyExit(existing, status, exitCode);
        }
        catch (Exception ex)
        {
            LogProcessExitUpdateFailed(ex, sessionId);
        }
        finally
        {
            outputCapture?.Dispose();
            process.Dispose();
        }
    }

    private Task AddEventAsync(
        Guid sessionId,
        AiSessionEventType eventType,
        DateTimeOffset createdAt,
        string message,
        CancellationToken cancellationToken,
        string? metadataJson = null)
        => _sessions.AddEventAsync(new AiSessionEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            EventType = eventType,
            CreatedAt = createdAt,
            Message = message,
            MetadataJson = metadataJson,
        }, cancellationToken);

    private static ProcessStartInfo BuildStartInfo(OctadockCommand command, string commandLine, string cwd)
    {
        string[]? argv = TryReadArgv(command);
        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = cwd,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        if (argv is { Length: > 0 } && !string.IsNullOrWhiteSpace(argv[0]))
        {
            startInfo.FileName = argv[0];
            foreach (string argument in argv.Skip(1))
            {
                startInfo.ArgumentList.Add(argument);
            }

            return startInfo;
        }

        startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
        startInfo.ArgumentList.Add("/S");
        startInfo.ArgumentList.Add("/C");
        startInfo.ArgumentList.Add(commandLine);
        return startInfo;
    }

    private WatchedProcess StartProcessWithOutputCapture(
        Guid sessionId,
        ProcessStartInfo startInfo,
        DateTimeOffset createdAt)
    {
        var process = new Process
        {
            StartInfo = startInfo,
        };
        var outputCapture = CreateOutputCaptureState(sessionId, createdAt);

        process.OutputDataReceived += (_, e) =>
            QueueOutputEvent(sessionId, AiSessionEventType.Output, e.Data, outputCapture);
        process.ErrorDataReceived += (_, e) =>
            QueueOutputEvent(sessionId, AiSessionEventType.Error, e.Data, outputCapture);

        try
        {
            if (!process.Start())
            {
                process.Dispose();
                outputCapture.Dispose();
                throw new InvalidOperationException("The process could not be started.");
            }
        }
        catch
        {
            process.Dispose();
            outputCapture.Dispose();
            throw;
        }

        try
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (InvalidOperationException ex)
        {
            LogOutputCaptureFailed(ex, sessionId);
        }

        return new WatchedProcess(process, outputCapture);
    }

    private void QueueOutputEvent(
        Guid sessionId,
        AiSessionEventType eventType,
        string? line,
        OutputCaptureState state)
    {
        if (line is null)
        {
            return;
        }

        state.WriteLine(eventType, line);
        string prompt = line.Trim();
        if (prompt.Length == 0)
        {
            return;
        }

        if (LooksLikeInputPrompt(prompt) && state.TryMarkWaitingPromptReported())
        {
            _ = ReportWaitingForInputAsync(sessionId, prompt);
        }

        if (state.TryReserveLine(out bool reportTruncation))
        {
            _ = AddEventSafeAsync(
                sessionId,
                eventType,
                _clock.UtcNow,
                TruncateOutputLine(line));
            return;
        }

        if (reportTruncation)
        {
            _ = AddEventSafeAsync(
                sessionId,
                AiSessionEventType.Metadata,
                _clock.UtcNow,
                $"Output capture truncated after {MaxOutputEvents} lines.");
        }
    }

    private async Task ReportWaitingForInputAsync(Guid sessionId, string prompt)
    {
        try
        {
            DateTimeOffset now = _clock.UtcNow;
            AiSessionRecord? existing = await _sessions.GetAsync(sessionId, CancellationToken.None)
                .ConfigureAwait(false);
            if (existing is null || !existing.IsActive || existing.EndedAt is not null)
            {
                return;
            }

            if (existing.Status != AiSessionStatus.WaitingForInput)
            {
                await _sessions.UpdateAsync(existing with
                {
                    Status = AiSessionStatus.WaitingForInput,
                    LastEventAt = now,
                }, CancellationToken.None).ConfigureAwait(false);
            }

            await AddEventAsync(
                sessionId,
                AiSessionEventType.WaitingForInput,
                now,
                $"Output suggests the process is waiting for input: {TruncateOutputLine(prompt)}",
                CancellationToken.None).ConfigureAwait(false);

            NotifyWaiting(existing);
        }
        catch (Exception ex)
        {
            LogOutputCaptureFailed(ex, sessionId);
        }
    }

    private OutputCaptureState CreateOutputCaptureState(Guid sessionId, DateTimeOffset createdAt)
    {
        try
        {
            string datePrefix = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"{AiSessionLogsFolder}/{createdAt:yyyy}/{createdAt:MM}/{createdAt:dd}");
            string stdoutRelative = $"{datePrefix}/{sessionId:D}-stdout.log";
            string stderrRelative = $"{datePrefix}/{sessionId:D}-stderr.log";
            string stdoutPath = _paths.ToAbsolute(stdoutRelative);
            string stderrPath = _paths.ToAbsolute(stderrRelative);

            Directory.CreateDirectory(Path.GetDirectoryName(stdoutPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(stderrPath)!);

            return new OutputCaptureState(
                stdoutRelative,
                stderrRelative,
                CreateLogWriter(stdoutPath),
                CreateLogWriter(stderrPath));
        }
        catch (Exception ex)
        {
            LogOutputCaptureFailed(ex, sessionId);
            return new OutputCaptureState(null, null, null, null);
        }
    }

    private async Task AddLogArtifactsAsync(
        Guid sessionId,
        OutputCaptureState outputCapture,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        foreach (AiSessionArtifactRecord artifact in outputCapture.CreateArtifacts(sessionId, createdAt))
        {
            try
            {
                await _sessions.AddArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogOutputCaptureFailed(ex, sessionId);
                return;
            }
        }

        if (outputCapture.HasLogFiles)
        {
            try
            {
                await AddEventAsync(
                    sessionId,
                    AiSessionEventType.ArtifactLinked,
                    createdAt,
                    "Full stdout/stderr logs are being captured.",
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogOutputCaptureFailed(ex, sessionId);
            }
        }
    }

    private static StreamWriter CreateLogWriter(string path)
        => new(path, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };

    private async Task AddEventSafeAsync(
        Guid sessionId,
        AiSessionEventType eventType,
        DateTimeOffset createdAt,
        string message)
    {
        try
        {
            await AddEventAsync(sessionId, eventType, createdAt, message, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogOutputCaptureFailed(ex, sessionId);
        }
    }

    private static string TruncateOutputLine(string line)
        => line.Length <= MaxOutputLineLength
            ? line
            : line[..MaxOutputLineLength] + "...";

    private static bool LooksLikeInputPrompt(string line)
    {
        string normalized = line.ToLowerInvariant();
        if (normalized.Contains("waiting for input", StringComparison.Ordinal) ||
            normalized.Contains("needs input", StringComparison.Ordinal) ||
            normalized.Contains("requires input", StringComparison.Ordinal) ||
            normalized.Contains("press any key", StringComparison.Ordinal) ||
            normalized.Contains("press enter", StringComparison.Ordinal) ||
            normalized.Contains("press return", StringComparison.Ordinal) ||
            normalized.Contains("hit enter", StringComparison.Ordinal) ||
            normalized.Contains("[y/n]", StringComparison.Ordinal) ||
            normalized.Contains("[y/n/a]", StringComparison.Ordinal) ||
            normalized.Contains("[yes/no]", StringComparison.Ordinal) ||
            normalized.Contains("(y/n)", StringComparison.Ordinal) ||
            normalized.Contains("(yes/no)", StringComparison.Ordinal))
        {
            return true;
        }

        if (!normalized.EndsWith(':'))
        {
            return false;
        }

        return normalized.Contains("password", StringComparison.Ordinal) ||
            normalized.Contains("passphrase", StringComparison.Ordinal) ||
            normalized.Contains("username", StringComparison.Ordinal) ||
            normalized.Contains("login", StringComparison.Ordinal) ||
            normalized.Contains("api key", StringComparison.Ordinal) ||
            normalized.Contains("token", StringComparison.Ordinal) ||
            normalized.Contains("verification code", StringComparison.Ordinal) ||
            normalized.Contains("two-factor", StringComparison.Ordinal) ||
            normalized.Contains("2fa", StringComparison.Ordinal) ||
            normalized.Contains("otp", StringComparison.Ordinal) ||
            normalized.Contains("enter choice", StringComparison.Ordinal) ||
            normalized.Contains("select option", StringComparison.Ordinal);
    }

    private static bool TryResolveEventType(string? raw, out AiSessionEventType eventType)
    {
        string token = NormalizeHookToken(raw);
        eventType = token switch
        {
            "created" or "create" => AiSessionEventType.Created,
            "started" or "start" => AiSessionEventType.Started,
            "statuschanged" or "status" => AiSessionEventType.StatusChanged,
            "output" or "stdout" => AiSessionEventType.Output,
            "error" or "stderr" => AiSessionEventType.Error,
            "waitingforinput" or "waiting" or "needsinput" or "inputrequired" => AiSessionEventType.WaitingForInput,
            "completed" or "complete" or "finish" or "finished" => AiSessionEventType.Completed,
            "artifactlinked" or "artifact" => AiSessionEventType.ArtifactLinked,
            "notification" or "notify" => AiSessionEventType.Notification,
            "metadata" => AiSessionEventType.Metadata,
            "heartbeat" or "heartbeatreceived" => AiSessionEventType.Heartbeat,
            _ => AiSessionEventType.Unknown,
        };

        return eventType != AiSessionEventType.Unknown;
    }

    private static bool TryResolveOptionalStatus(
        string? raw,
        out AiSessionStatus? status,
        out string? error)
    {
        status = null;
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (TryResolveStatus(raw, out AiSessionStatus parsed))
        {
            status = parsed;
            return true;
        }

        error = $"Unsupported AI session status '{raw}'.";
        return false;
    }

    private static bool TryResolveStatus(string raw, out AiSessionStatus status)
    {
        string token = NormalizeHookToken(raw);
        status = token switch
        {
            "queued" or "queue" => AiSessionStatus.Queued,
            "running" or "run" or "started" => AiSessionStatus.Running,
            "waitingforinput" or "waiting" or "needsinput" or "inputrequired" => AiSessionStatus.WaitingForInput,
            "paused" or "pause" => AiSessionStatus.Paused,
            "completed" or "complete" or "succeeded" or "success" => AiSessionStatus.Completed,
            "failed" or "failure" or "error" => AiSessionStatus.Failed,
            "cancelled" or "canceled" or "cancel" => AiSessionStatus.Cancelled,
            _ => AiSessionStatus.Unknown,
        };

        return status != AiSessionStatus.Unknown;
    }

    private static AiSessionStatus? InferStatusFromEvent(AiSessionEventType eventType)
        => eventType switch
        {
            AiSessionEventType.Started => AiSessionStatus.Running,
            AiSessionEventType.WaitingForInput => AiSessionStatus.WaitingForInput,
            AiSessionEventType.Completed => AiSessionStatus.Completed,
            _ => null,
        };

    private static bool TryResolveExitCode(
        OctadockCommand command,
        out int? exitCode,
        out string? error)
    {
        exitCode = null;
        error = null;
        string? raw = command.Get("exit-code") ?? command.Get("exitcode");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            exitCode = parsed;
            return true;
        }

        error = $"Parameter 'exit-code' must be an integer but was '{raw}'.";
        return false;
    }

    private static bool TryResolveMetadata(
        OctadockCommand command,
        out string? metadataJson,
        out string? error)
    {
        metadataJson = null;
        error = null;
        string? raw = command.Get("metadata-json") ?? command.Get("metadatajson") ?? command.Get("metadata");
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                using JsonDocument _ = JsonDocument.Parse(raw);
                metadataJson = raw;
                return true;
            }
            catch (JsonException ex)
            {
                error = $"Parameter 'metadata-json' must contain JSON: {ex.Message}";
                return false;
            }
        }

        string? source = command.Get("source");
        if (!string.IsNullOrWhiteSpace(source))
        {
            metadataJson = Metadata(source.Trim());
        }

        return true;
    }

    private static bool CanApplyHookStatus(
        AiSessionRecord session,
        AiSessionStatus status,
        out string? error)
    {
        error = null;
        if (session.EndedAt is not null &&
            status is AiSessionStatus.Queued or AiSessionStatus.Running or AiSessionStatus.WaitingForInput or AiSessionStatus.Paused)
        {
            error = $"AI session '{session.Id}' has already ended and cannot be moved back to '{status}'.";
            return false;
        }

        return true;
    }

    private static AiSessionRecord ApplyHookStatus(
        AiSessionRecord session,
        AiSessionStatus status,
        int? exitCode,
        DateTimeOffset now)
    {
        bool terminal = status is AiSessionStatus.Completed or AiSessionStatus.Failed or AiSessionStatus.Cancelled;
        return session with
        {
            Status = status,
            LastEventAt = now,
            EndedAt = terminal ? session.EndedAt ?? now : session.EndedAt,
            ExitCode = exitCode ?? session.ExitCode,
        };
    }

    private static string ResolveHookMessage(
        string? requested,
        AiSessionEventType eventType,
        AiSessionStatus? status)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return TruncateOutputLine(requested.Trim());
        }

        return status is { } resolvedStatus
            ? $"Status changed to {resolvedStatus}."
            : $"{eventType} event received.";
    }

    private static string NormalizeHookToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(value.Length);
        foreach (char c in value)
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static string[]? TryReadArgv(OctadockCommand command)
    {
        string? raw = command.Get("argv");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(raw);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryResolveWorkingDirectory(
        string? requested,
        out string cwd,
        out string? error)
    {
        error = null;
        try
        {
            cwd = string.IsNullOrWhiteSpace(requested)
                ? Environment.CurrentDirectory
                : Path.GetFullPath(Environment.ExpandEnvironmentVariables(requested));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            cwd = Environment.CurrentDirectory;
            error = $"Working directory '{requested}' is invalid.";
            return false;
        }

        if (!Directory.Exists(cwd))
        {
            error = $"Working directory '{cwd}' does not exist.";
            return false;
        }

        return true;
    }

    private static AiSessionNotificationMode ResolveNotificationMode(OctadockCommand command)
        => command.GetEnum<AiSessionNotificationMode>("notify") ?? AiSessionNotificationMode.Default;

    private void NotifyExit(AiSessionRecord session, AiSessionStatus status, int? exitCode)
    {
        if (session.NotificationMode == AiSessionNotificationMode.Silent)
        {
            return;
        }

        string title = status == AiSessionStatus.Completed
            ? "AI session completed"
            : "AI session failed";
        string message = status == AiSessionStatus.Completed
            ? $"{session.Title} finished successfully."
            : exitCode is null
                ? $"{session.Title} exited without a status code."
                : $"{session.Title} failed with exit code {exitCode.Value}.";
        NotificationKind kind = status == AiSessionStatus.Completed
            ? NotificationKind.Success
            : NotificationKind.Error;

        _notifications.Notify(title, message, kind, _presenter.ShowAiSessions);
    }

    private void NotifyWaiting(AiSessionRecord session)
    {
        if (session.NotificationMode == AiSessionNotificationMode.Silent)
        {
            return;
        }

        _notifications.Notify(
            "AI session needs input",
            $"{session.Title} appears to be waiting for input.",
            NotificationKind.Warning,
            _presenter.ShowAiSessions);
    }

    private void NotifyHookStatus(AiSessionRecord session, AiSessionStatus? status, int? exitCode)
    {
        if (status is AiSessionStatus.WaitingForInput)
        {
            NotifyWaiting(session);
        }
        else if (status is AiSessionStatus.Completed or AiSessionStatus.Failed)
        {
            NotifyExit(session, status.Value, exitCode);
        }
    }

    private static string ResolveTitle(string? requested, string fallback)
    {
        string title = string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
        return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 3)] + "...";
    }

    private static string TryGetProcessName(Process process)
    {
        try
        {
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return $"PID {process.Id}";
        }
    }

    private static int? TryGetExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            return null;
        }
    }

    private static string Metadata(string source)
        => JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["source"] = source,
        });

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Failed to update AI session {SessionId} after process exit.")]
    private partial void LogProcessExitUpdateFailed(Exception exception, Guid sessionId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Debug,
        Message = "Failed to capture output for AI session {SessionId}.")]
    private partial void LogOutputCaptureFailed(Exception exception, Guid sessionId);

    private sealed record WatchedProcess(Process Process, OutputCaptureState OutputCapture);

    private sealed class OutputCaptureState : IDisposable
    {
        private readonly StreamWriter? _stdout;
        private readonly StreamWriter? _stderr;
        private readonly object _stdoutGate = new();
        private readonly object _stderrGate = new();
        private int _remaining = MaxOutputEvents;
        private int _truncationReported;
        private int _waitingPromptReported;
        private int _disposed;

        public OutputCaptureState(
            string? stdoutRelativePath,
            string? stderrRelativePath,
            StreamWriter? stdout,
            StreamWriter? stderr)
        {
            StdoutRelativePath = stdoutRelativePath;
            StderrRelativePath = stderrRelativePath;
            _stdout = stdout;
            _stderr = stderr;
        }

        public string? StdoutRelativePath { get; }

        public string? StderrRelativePath { get; }

        public bool HasLogFiles =>
            !string.IsNullOrWhiteSpace(StdoutRelativePath) ||
            !string.IsNullOrWhiteSpace(StderrRelativePath);

        public void WriteLine(AiSessionEventType eventType, string line)
        {
            StreamWriter? writer = eventType == AiSessionEventType.Error ? _stderr : _stdout;
            if (writer is null || Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            object gate = eventType == AiSessionEventType.Error ? _stderrGate : _stdoutGate;
            lock (gate)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    writer.WriteLine(line);
                }
            }
        }

        public IReadOnlyList<AiSessionArtifactRecord> CreateArtifacts(Guid sessionId, DateTimeOffset createdAt)
        {
            var artifacts = new List<AiSessionArtifactRecord>(capacity: 2);
            if (!string.IsNullOrWhiteSpace(StdoutRelativePath))
            {
                artifacts.Add(CreateLogArtifact(sessionId, createdAt, StdoutRelativePath, "stdout"));
            }

            if (!string.IsNullOrWhiteSpace(StderrRelativePath))
            {
                artifacts.Add(CreateLogArtifact(sessionId, createdAt, StderrRelativePath, "stderr"));
            }

            return artifacts;
        }

        public bool TryReserveLine(out bool reportTruncation)
        {
            int remaining = Interlocked.Decrement(ref _remaining);
            if (remaining >= 0)
            {
                reportTruncation = false;
                return true;
            }

            reportTruncation = Interlocked.Exchange(ref _truncationReported, 1) == 0;
            return false;
        }

        public bool TryMarkWaitingPromptReported()
            => Interlocked.Exchange(ref _waitingPromptReported, 1) == 0;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            lock (_stdoutGate)
            {
                _stdout?.Dispose();
            }

            lock (_stderrGate)
            {
                _stderr?.Dispose();
            }
        }

        private static AiSessionArtifactRecord CreateLogArtifact(
            Guid sessionId,
            DateTimeOffset createdAt,
            string path,
            string stream)
            => new()
            {
                Id = Guid.NewGuid(),
                SessionId = sessionId,
                ArtifactKind = AiSessionArtifactKind.Log,
                CreatedAt = createdAt,
                Path = path,
                Title = $"{stream} log",
                MetadataJson = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["stream"] = stream,
                }),
            };
    }
}
