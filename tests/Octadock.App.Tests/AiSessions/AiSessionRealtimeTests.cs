using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.App.Tests.AiSessions;

public sealed class AiSessionRealtimeTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 5, 12, 0, 0, TimeSpan.Zero);

    // ---- Change bus -------------------------------------------------------

    [Fact]
    public void ChangeBus_survives_a_throwing_subscriber()
    {
        var bus = new AiSessionChangeBus();
        var received = 0;
        bus.SessionsChanged += (_, _) => throw new InvalidOperationException("boom");
        bus.SessionsChanged += (_, _) => received++;

        bus.Publish();

        received.Should().Be(1);
    }

    [Fact]
    public async Task NotifyingRepository_publishes_on_every_mutation()
    {
        var bus = new AiSessionChangeBus();
        var published = 0;
        bus.SessionsChanged += (_, _) => published++;
        var repository = new NotifyingAiSessionRepository(new InMemorySessionRepository(), bus);
        AiSessionRecord record = Session(Guid.NewGuid(), AiSessionStatus.Running, pid: null);

        await repository.AddAsync(record);
        await repository.UpdateAsync(record with { Status = AiSessionStatus.Completed });
        await repository.AddEventAsync(new AiSessionEventRecord
        {
            Id = Guid.NewGuid(),
            SessionId = record.Id,
            EventType = AiSessionEventType.Completed,
            CreatedAt = T0,
        });

        published.Should().Be(3);

        // Reads must not publish.
        _ = await repository.GetAsync(record.Id);
        _ = await repository.ListAsync(new AiSessionFilter());
        published.Should().Be(3);
    }

    // ---- Exit watcher -----------------------------------------------------

    [Fact]
    public async Task ExitWatcher_completes_session_the_moment_its_process_exits()
    {
        var repository = new InMemorySessionRepository();
        var bus = new AiSessionChangeBus();
        using var watcher = new AiSessionProcessExitWatcher(
            repository, bus, SystemClock.Instance, NullLogger<AiSessionProcessExitWatcher>.Instance);

        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "ping",
            Arguments = "-n 60 127.0.0.1",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        try
        {
            AiSessionRecord session = Session(Guid.NewGuid(), AiSessionStatus.Running, process.Id);
            await repository.AddAsync(session);

            await watcher.SyncAsync(CancellationToken.None);
            watcher.TrackedCount.Should().Be(1);

            process.Kill(entireProcessTree: true);

            AiSessionRecord? completed = await WaitForStatusAsync(
                repository, session.Id, AiSessionStatus.Completed, TimeSpan.FromSeconds(5));
            completed.Should().NotBeNull("the watcher must complete the session as soon as the process dies");
            completed!.EndedAt.Should().NotBeNull();
            repository.Events.Should().Contain(e =>
                e.SessionId == session.Id && e.EventType == AiSessionEventType.Completed);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
    }

    [Fact]
    public async Task ExitWatcher_completes_sessions_whose_pid_is_already_gone()
    {
        var repository = new InMemorySessionRepository();
        var bus = new AiSessionChangeBus();
        using var watcher = new AiSessionProcessExitWatcher(
            repository, bus, SystemClock.Instance, NullLogger<AiSessionProcessExitWatcher>.Instance);

        // Spawn and let it exit so the PID is (almost certainly) dead.
        using Process shortLived = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c exit 0",
            CreateNoWindow = true,
            UseShellExecute = false,
        })!;
        await shortLived.WaitForExitAsync();
        int deadPid = shortLived.Id;

        AiSessionRecord session = Session(Guid.NewGuid(), AiSessionStatus.Running, deadPid);
        await repository.AddAsync(session);

        await watcher.SyncAsync(CancellationToken.None);

        AiSessionRecord? completed = await WaitForStatusAsync(
            repository, session.Id, AiSessionStatus.Completed, TimeSpan.FromSeconds(5));
        completed.Should().NotBeNull("a session pointing at a dead PID must be completed immediately");
    }

    [Fact]
    public async Task ExitWatcher_never_touches_sessions_completed_by_another_writer()
    {
        var repository = new InMemorySessionRepository();
        var bus = new AiSessionChangeBus();
        using var watcher = new AiSessionProcessExitWatcher(
            repository, bus, SystemClock.Instance, NullLogger<AiSessionProcessExitWatcher>.Instance);

        AiSessionRecord session = Session(Guid.NewGuid(), AiSessionStatus.Completed, pid: 1);
        await repository.AddAsync(session);

        await watcher.CompleteIfStillActiveAsync(session.Id, exitCode: 0);

        repository.Events.Should().BeEmpty("already-completed sessions must not get duplicate events");
    }

    // ---- Helpers ----------------------------------------------------------

    private static async Task<AiSessionRecord?> WaitForStatusAsync(
        InMemorySessionRepository repository, Guid id, AiSessionStatus status, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            AiSessionRecord? current = await repository.GetAsync(id);
            if (current is not null && current.Status == status)
            {
                return current;
            }

            await Task.Delay(50);
        }

        return null;
    }

    private static AiSessionRecord Session(Guid id, AiSessionStatus status, int? pid)
        => new()
        {
            Id = id,
            Provider = AiSessionProvider.ClaudeCode,
            Title = "test session",
            Status = status,
            StartedAt = T0,
            Pid = pid,
        };

    private sealed class InMemorySessionRepository : IAiSessionRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, AiSessionRecord> _sessions = [];

        public List<AiSessionEventRecord> Events { get; } = [];

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

            return Task.CompletedTask;
        }

        public Task<AiSessionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_sessions.TryGetValue(id, out AiSessionRecord? record) ? record : null);
            }
        }

        public Task<IReadOnlyList<AiSessionRecord>> ListAsync(
            AiSessionFilter filter, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                IEnumerable<AiSessionRecord> query = _sessions.Values;
                if (filter.Statuses is { Count: > 0 })
                {
                    query = query.Where(s => filter.Statuses.Contains(s.Status));
                }

                return Task.FromResult<IReadOnlyList<AiSessionRecord>>(query.Take(filter.Limit).ToList());
            }
        }

        public Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                Events.Add(record);
            }

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AiSessionEventRecord>> GetEventsAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult<IReadOnlyList<AiSessionEventRecord>>(
                    Events.Where(e => e.SessionId == sessionId).ToList());
            }
        }

        public Task AddArtifactAsync(AiSessionArtifactRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AiSessionArtifactRecord>> GetArtifactsAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiSessionArtifactRecord>>([]);
    }
}
