using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Common;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionCommandServiceTests
{
    [Fact]
    public async Task RunAsync_notifies_when_watched_command_completes()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var presenter = new FakeWindowPresenter();
        var service = CreateService(sessions, notifications, presenter: presenter);
        OctadockCommand command = RunCommand("exit 0", "Quick success");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        AiSessionRecord completed = await WithTimeout(sessions.TerminalUpdate.Task);
        completed.Status.Should().Be(AiSessionStatus.Completed);

        FakeNotification notification = await WithTimeout(notifications.NextNotification.Task);
        notification.Title.Should().Be("AI session completed");
        notification.Message.Should().Contain("Quick success");
        notification.Kind.Should().Be(NotificationKind.Success);
        notification.ClickAction.Should().NotBeNull();

        notification.ClickAction!();
        presenter.AiSessionsShown.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_notifies_when_watched_command_fails()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        OctadockCommand command = RunCommand("exit 7", "Quick failure");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        AiSessionRecord completed = await WithTimeout(sessions.TerminalUpdate.Task);
        completed.Status.Should().Be(AiSessionStatus.Failed);
        completed.ExitCode.Should().Be(7);

        FakeNotification notification = await WithTimeout(notifications.NextNotification.Task);
        notification.Title.Should().Be("AI session failed");
        notification.Message.Should().Contain("exit code 7");
        notification.Kind.Should().Be(NotificationKind.Error);
    }

    [Fact]
    public async Task RunAsync_honors_silent_notification_mode()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        OctadockCommand command = RunCommand("exit 0", "Quiet success", "silent");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        await WithTimeout(sessions.TerminalEvent.Task);
        await Task.Delay(100);
        notifications.Notifications.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_captures_stdout_and_stderr_events()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        OctadockCommand command = RunCommand("echo stdout-line & echo stderr-line 1>&2", "Output smoke", "silent");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        AiSessionEventRecord output = await WithTimeout(sessions.OutputEvent.Task);
        AiSessionEventRecord error = await WithTimeout(sessions.ErrorEvent.Task);

        output.Message.Should().NotBeNull();
        error.Message.Should().NotBeNull();
        output.Message!.TrimEnd().Should().Be("stdout-line");
        error.Message!.TrimEnd().Should().Be("stderr-line");
        output.SessionId.Should().Be(error.SessionId);
    }

    [Fact]
    public async Task RunAsync_writes_full_stdout_and_stderr_log_artifacts()
    {
        using var temp = new TempRoot();
        var paths = new StoragePaths(temp.Path);
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications, paths);
        OctadockCommand command = RunCommand("echo stdout-log & echo stderr-log 1>&2", "Output logs", "silent");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        await WithTimeout(sessions.TerminalEvent.Task);

        IReadOnlyList<AiSessionArtifactRecord> artifacts = sessions.Artifacts;
        artifacts.Should().HaveCount(2);
        artifacts.Should().OnlyContain(a => a.ArtifactKind == AiSessionArtifactKind.Log);

        AiSessionArtifactRecord stdout = artifacts.Single(a => a.Title == "stdout log");
        AiSessionArtifactRecord stderr = artifacts.Single(a => a.Title == "stderr log");
        stdout.Path.Should().StartWith($"{AiSessionCommandService.AiSessionLogsFolder}/");
        stderr.Path.Should().StartWith($"{AiSessionCommandService.AiSessionLogsFolder}/");
        File.ReadAllText(paths.ToAbsolute(stdout.Path!)).Should().Contain("stdout-log");
        File.ReadAllText(paths.ToAbsolute(stderr.Path!)).Should().Contain("stderr-log");
    }

    [Fact]
    public async Task RunAsync_marks_session_waiting_when_output_looks_like_a_prompt()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        OctadockCommand command = RunCommand(
            "echo Press any key to continue... & ping -n 2 127.0.0.1 >nul",
            "Prompted run");

        CommandResult result = await service.RunAsync(command);

        result.Success.Should().BeTrue();
        AiSessionRecord waiting = await WithTimeout(sessions.WaitingUpdate.Task);
        waiting.Status.Should().Be(AiSessionStatus.WaitingForInput);

        AiSessionEventRecord waitingEvent = await WithTimeout(sessions.WaitingEvent.Task);
        waitingEvent.Message.Should().Contain("Press any key to continue");

        FakeNotification notification = await WithTimeout(notifications.NextNotification.Task);
        notification.Title.Should().Be("AI session needs input");
        notification.Message.Should().Contain("Prompted run");
        notification.Kind.Should().Be(NotificationKind.Warning);

        AiSessionRecord completed = await WithTimeout(sessions.TerminalUpdate.Task);
        completed.Status.Should().Be(AiSessionStatus.Completed);
    }

    [Fact]
    public async Task AddHookEventAsync_marks_existing_session_waiting_and_notifies()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        Guid sessionId = Guid.NewGuid();
        await sessions.AddAsync(Session(sessionId));
        OctadockCommand command = OctadockCommand.Create(CommandType.AiSessionEvent, new Dictionary<string, string>
        {
            ["session-id"] = sessionId.ToString("D"),
            ["event"] = "needs-input",
            ["message"] = "Approval required before continuing.",
            ["source"] = "claude-hook",
        });

        CommandResult result = await service.AddHookEventAsync(command);

        result.Success.Should().BeTrue(result.Message);
        AiSessionRecord waiting = await WithTimeout(sessions.WaitingUpdate.Task);
        waiting.Status.Should().Be(AiSessionStatus.WaitingForInput);

        AiSessionEventRecord waitingEvent = await WithTimeout(sessions.WaitingEvent.Task);
        waitingEvent.SessionId.Should().Be(sessionId);
        waitingEvent.EventType.Should().Be(AiSessionEventType.WaitingForInput);
        waitingEvent.Message.Should().Be("Approval required before continuing.");
        waitingEvent.MetadataJson.Should().Contain("claude-hook");

        AiSessionRecord? stored = await sessions.GetAsync(sessionId);
        stored!.Status.Should().Be(AiSessionStatus.WaitingForInput);

        FakeNotification notification = await WithTimeout(notifications.NextNotification.Task);
        notification.Title.Should().Be("AI session needs input");
        notification.Message.Should().Contain("Hooked run");
        notification.Kind.Should().Be(NotificationKind.Warning);
    }

    [Fact]
    public async Task AddHookEventAsync_records_failed_status_and_exit_code()
    {
        var sessions = new FakeAiSessionRepository();
        var notifications = new FakeNotificationService();
        var service = CreateService(sessions, notifications);
        Guid sessionId = Guid.NewGuid();
        await sessions.AddAsync(Session(sessionId));
        OctadockCommand command = OctadockCommand.Create(CommandType.AiSessionEvent, new Dictionary<string, string>
        {
            ["session-id"] = sessionId.ToString("D"),
            ["event"] = "status-changed",
            ["status"] = "failed",
            ["exit-code"] = "7",
            ["message"] = "Hook reported failure.",
        });

        CommandResult result = await service.AddHookEventAsync(command);

        result.Success.Should().BeTrue(result.Message);
        AiSessionRecord failed = await WithTimeout(sessions.TerminalUpdate.Task);
        failed.Status.Should().Be(AiSessionStatus.Failed);
        failed.ExitCode.Should().Be(7);
        failed.EndedAt.Should().NotBeNull();

        FakeNotification notification = await WithTimeout(notifications.NextNotification.Task);
        notification.Title.Should().Be("AI session failed");
        notification.Message.Should().Contain("exit code 7");
        notification.Kind.Should().Be(NotificationKind.Error);
    }

    private static AiSessionCommandService CreateService(
        FakeAiSessionRepository sessions,
        FakeNotificationService notifications,
        IStoragePaths? paths = null,
        IWindowPresenter? presenter = null)
        => new(
            sessions,
            notifications,
            presenter ?? new FakeWindowPresenter(),
            paths ?? new StoragePaths(Path.Combine(
                Path.GetTempPath(),
                "OctadockAiSessionCommandTests",
                Guid.NewGuid().ToString("N"))),
            new TestClock(new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.Zero)),
            NullLogger<AiSessionCommandService>.Instance);

    private static OctadockCommand RunCommand(string commandLine, string title, string notify = "toast")
        => OctadockCommand.Create(CommandType.Run, new Dictionary<string, string>
        {
            ["command"] = commandLine,
            ["title"] = title,
            ["notify"] = notify,
        });

    private static AiSessionRecord Session(Guid id)
        => new()
        {
            Id = id,
            Provider = AiSessionProvider.Generic,
            Title = "Hooked run",
            Cwd = null,
            Command = "external hook",
            Pid = null,
            Status = AiSessionStatus.Running,
            StartedAt = new DateTimeOffset(2026, 7, 3, 11, 0, 0, TimeSpan.Zero),
            LastEventAt = new DateTimeOffset(2026, 7, 3, 11, 0, 0, TimeSpan.Zero),
            NotificationMode = AiSessionNotificationMode.Default,
            MetadataJson = null,
        };

    private static async Task<T> WithTimeout<T>(Task<T> task)
    {
        Task timeout = Task.Delay(TimeSpan.FromSeconds(5));
        Task completed = await Task.WhenAny(task, timeout);
        completed.Should().BeSameAs(task);
        return await task;
    }

    private sealed class FakeAiSessionRepository : IAiSessionRepository
    {
        private readonly Dictionary<Guid, AiSessionRecord> _sessions = new();
        private readonly List<AiSessionEventRecord> _events = new();
        private readonly List<AiSessionArtifactRecord> _artifacts = new();
        private readonly object _gate = new();

        public TaskCompletionSource<AiSessionRecord> TerminalUpdate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiSessionRecord> WaitingUpdate { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiSessionEventRecord> TerminalEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiSessionEventRecord> WaitingEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiSessionEventRecord> OutputEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<AiSessionEventRecord> ErrorEvent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<AiSessionArtifactRecord> Artifacts
        {
            get
            {
                lock (_gate)
                {
                    return _artifacts.ToArray();
                }
            }
        }

        public Task AddAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _sessions[record.Id] = record;
            }

            return Task.CompletedTask;
        }

        public Task UpdateAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _sessions[record.Id] = record;
            }

            if (record.Status is AiSessionStatus.Completed or AiSessionStatus.Failed)
            {
                TerminalUpdate.TrySetResult(record);
            }
            else if (record.Status == AiSessionStatus.WaitingForInput)
            {
                WaitingUpdate.TrySetResult(record);
            }

            return Task.CompletedTask;
        }

        public Task<AiSessionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_sessions.GetValueOrDefault(id));
            }
        }

        public Task<IReadOnlyList<AiSessionRecord>> ListAsync(
            AiSessionFilter filter,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<AiSessionRecord>>(_sessions.Values.ToArray());
            }
        }

        public Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _events.Add(record);
            }

            if (record.EventType == AiSessionEventType.Completed)
            {
                TerminalEvent.TrySetResult(record);
            }
            else if (record.EventType == AiSessionEventType.WaitingForInput)
            {
                WaitingEvent.TrySetResult(record);
            }
            else if (record.EventType == AiSessionEventType.Output)
            {
                OutputEvent.TrySetResult(record);
            }
            else if (record.EventType == AiSessionEventType.Error)
            {
                ErrorEvent.TrySetResult(record);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiSessionEventRecord>> GetEventsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<AiSessionEventRecord>>(
                    _events.Where(e => e.SessionId == sessionId).ToArray());
            }
        }

        public Task AddArtifactAsync(AiSessionArtifactRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _artifacts.Add(record);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiSessionArtifactRecord>> GetArtifactsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<AiSessionArtifactRecord>>(
                    _artifacts.Where(a => a.SessionId == sessionId).ToArray());
            }
        }
    }

    private sealed class FakeNotificationService : INotificationService
    {
        private readonly object _gate = new();

        public TaskCompletionSource<FakeNotification> NextNotification { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyList<FakeNotification> Notifications
        {
            get
            {
                lock (_gate)
                {
                    return _notifications.ToArray();
                }
            }
        }

        private readonly List<FakeNotification> _notifications = new();

        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
        {
            var notification = new FakeNotification(title, message, kind, clickAction);
            lock (_gate)
            {
                _notifications.Add(notification);
            }

            NextNotification.TrySetResult(notification);
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;

        public DateTimeOffset LocalNow => UtcNow;
    }

    private sealed class TempRoot : IDisposable
    {
        public TempRoot()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "OctadockAiSessionCommandTests",
                Guid.NewGuid().ToString("N"));
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private sealed class FakeWindowPresenter : IWindowPresenter
    {
        public int AiSessionsShown { get; private set; }

        public void ShowHistory()
        {
        }

        public void ShowClipboardHistory()
        {
        }

        public void ShowTextTools()
        {
        }

        public void ShowAiSessions()
            => AiSessionsShown++;

        public void ShowSettings(string? tab = null)
        {
        }

        public void ShowAllInOneHud(
            CaptureMode? mode = null,
            PixelRect? preloadedRegion = null,
            int? preloadedWidth = null,
            int? preloadedHeight = null)
        {
        }

        public Task<bool> ShowFirstRunIfNeededAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private sealed record FakeNotification(
        string Title,
        string Message,
        NotificationKind Kind,
        Action? ClickAction);
}
