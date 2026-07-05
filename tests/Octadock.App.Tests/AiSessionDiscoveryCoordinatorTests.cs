using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.App.Services.AiSessionDiscovery;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Xunit;

namespace Octadock.App.Tests;

public sealed class AiSessionDiscoveryCoordinatorTests
{
    private static readonly DateTimeOffset ObservedAt =
        new(2026, 7, 3, 12, 5, 0, TimeSpan.Zero);

    private static readonly string[] AllSources =
    [
        ProcessSnapshotEvidenceCollector.SourceId,
        CodexStateEvidenceCollector.SourceId,
        ClaudeCodeStateEvidenceCollector.SourceId,
    ];

    private readonly FakeAiSessionRepository _repository = new();
    private readonly HashSet<int> _alivePids = [];
    private readonly AiSessionDiscoveryCoordinator _coordinator;

    public AiSessionDiscoveryCoordinatorTests()
    {
        _coordinator = new AiSessionDiscoveryCoordinator(
            _repository,
            (pid, _) => _alivePids.Contains(pid));
    }

    [Fact]
    public async Task Creates_row_with_lifecycle_events_and_diagnostic_metadata()
    {
        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            pid: 100,
            sessionId: "t1",
            workspace: @"C:\Repo");

        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt);

        result.AddedCount.Should().Be(1);
        result.ActivePids.Should().Contain(100);
        AiSessionRecord row = _repository.Rows.Should().ContainSingle().Subject;
        row.Provider.Should().Be(AiSessionProvider.Codex);
        row.Status.Should().Be(AiSessionStatus.Running);
        row.Pid.Should().Be(100);
        row.Cwd.Should().Be(@"C:\Repo");
        row.MetadataJson.Should().Contain("Codex:session:t1");
        row.MetadataJson.Should().Contain("\"source\":\"process-discovery\"");
        row.MetadataJson.Should().Contain("confidence");
        row.MetadataJson.Should().Contain("Test detection reason.");
        _repository.EventsFor(row.Id).Select(e => e.EventType).Should().Equal(
            AiSessionEventType.Created,
            AiSessionEventType.Started);
    }

    [Fact]
    public async Task Repeated_sync_with_same_observation_is_idempotent()
    {
        AiSessionObservation observation = Observation("Codex:session:t1", pid: 100, sessionId: "t1");

        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);
        AiSessionDiscoverySyncResult second = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt.AddSeconds(10));

        second.AddedCount.Should().Be(0);
        second.CompletedCount.Should().Be(0);
        _repository.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Process_exit_completes_a_process_backed_row()
    {
        AiSessionObservation observation = Observation("ClaudeCode:pid:100:5", pid: 100);
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([]),
            ObservedAt.AddSeconds(30));

        result.CompletedCount.Should().Be(1);
        AiSessionRecord row = _repository.Rows.Single();
        row.Status.Should().Be(AiSessionStatus.Completed);
        row.EndedAt.Should().Be(ObservedAt.AddSeconds(30));
        _repository.EventsFor(row.Id).Should().Contain(e =>
            e.EventType == AiSessionEventType.Completed &&
            e.Message!.Contains("no longer running"));
    }

    [Fact]
    public async Task Live_but_declassified_process_survives_one_scan_then_completes()
    {
        _alivePids.Add(100);
        AiSessionObservation observation = Observation("ClaudeCode:pid:100:5", pid: 100);
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        AiSessionDiscoverySyncResult firstMiss = await _coordinator.SyncAsync(
            Resolution([]),
            ObservedAt.AddSeconds(30));
        firstMiss.CompletedCount.Should().Be(0);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Running);

        AiSessionDiscoverySyncResult secondMiss = await _coordinator.SyncAsync(
            Resolution([]),
            ObservedAt.AddSeconds(60));
        secondMiss.CompletedCount.Should().Be(1);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Completed);
    }

    [Fact]
    public async Task Failed_evidence_source_never_completes_its_rows()
    {
        AiSessionObservation observation = Observation("ClaudeCode:pid:100:5", pid: 100);
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        // Process snapshot collection failed this pass: even though the pid is
        // dead, a missing session proves nothing.
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            new AiSessionResolution(
                [],
                [],
                new HashSet<string>(
                    [CodexStateEvidenceCollector.SourceId],
                    StringComparer.OrdinalIgnoreCase)),
            ObservedAt.AddSeconds(30));

        result.CompletedCount.Should().Be(0);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Running);
    }

    [Fact]
    public async Task Stale_codex_thread_row_completes_when_state_stops_reporting_it()
    {
        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            sessionId: "t1",
            detector: "codex-state-thread",
            extraMetadata: new Dictionary<string, string> { ["codexThreadId"] = "t1" });
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([]),
            ObservedAt.AddMinutes(1));

        result.CompletedCount.Should().Be(1);
        AiSessionRecord row = _repository.Rows.Single();
        row.Status.Should().Be(AiSessionStatus.Completed);
        _repository.EventsFor(row.Id).Should().Contain(e =>
            e.EventType == AiSessionEventType.Completed &&
            e.Message!.Contains("Provider state"));
    }

    [Fact]
    public async Task Completed_observation_completes_the_matching_row_with_its_reason()
    {
        AiSessionObservation running = Observation("Codex:session:t1", sessionId: "t1");
        await _coordinator.SyncAsync(Resolution([running]), ObservedAt);

        AiSessionObservation completed = running with
        {
            Status = AiSessionStatus.Completed,
            Reason = "Codex thread rollout recorded task completion.",
        };
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([completed]),
            ObservedAt.AddMinutes(1));

        result.CompletedCount.Should().Be(1);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Completed);
        _repository.EventsFor(_repository.Rows.Single().Id).Should().Contain(e =>
            e.Message!.Contains("task completion"));
    }

    [Fact]
    public async Task Completed_observation_without_a_row_creates_nothing()
    {
        AiSessionObservation completed = Observation("Codex:session:gone", sessionId: "gone") with
        {
            Status = AiSessionStatus.Completed,
        };

        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([completed]),
            ObservedAt);

        result.AddedCount.Should().Be(0);
        _repository.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_rows_for_one_session_collapse_into_the_canonical_row()
    {
        await SeedRowAsync(
            "Codex:session:t1",
            provider: AiSessionProvider.Codex,
            pid: null,
            sessionId: "t1");
        await SeedRowAsync(
            "Codex:pid:100:5",
            provider: AiSessionProvider.Codex,
            pid: 100,
            startTicks: 5);

        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            pid: 100,
            startTicks: 5,
            sessionId: "t1");
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt.AddSeconds(30));

        result.AddedCount.Should().Be(0);
        result.CompletedCount.Should().Be(1);
        _repository.Rows.Count(r => r.IsActive).Should().Be(1);
        AiSessionRecord merged = _repository.Rows.Single(r => !r.IsActive);
        _repository.EventsFor(merged.Id).Should().Contain(e =>
            e.Message!.Contains("Merged into"));
    }

    [Fact]
    public async Task Legacy_processKey_row_is_adopted_instead_of_duplicated()
    {
        long ticks = ObservedAt.AddMinutes(-5).UtcTicks;
        var metadata = new Dictionary<string, string>
        {
            ["source"] = "process-discovery",
            ["processKey"] = string.Create(CultureInfo.InvariantCulture, $"Codex:100:{ticks}"),
            ["detector"] = "codex-runtime-session",
        };
        await SeedRowAsync(metadataOverride: metadata, provider: AiSessionProvider.Codex, pid: 100);

        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            pid: 100,
            startTicks: ticks,
            sessionId: "t1");
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt);

        result.AddedCount.Should().Be(0);
        AiSessionRecord row = _repository.Rows.Should().ContainSingle().Subject;
        row.IsActive.Should().BeTrue();
        row.MetadataJson.Should().Contain("Codex:session:t1");
    }

    [Fact]
    public async Task Workspace_match_adopts_a_pid_row_into_a_thread_observation()
    {
        AiSessionObservation pidObservation = Observation(
            "Codex:pid:100:5",
            pid: 100,
            startTicks: 5,
            workspace: @"C:\Repo");
        await _coordinator.SyncAsync(Resolution([pidObservation]), ObservedAt);

        AiSessionObservation threadObservation = Observation(
            "Codex:session:t1",
            sessionId: "t1",
            workspace: @"C:\Repo");
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([threadObservation]),
            ObservedAt.AddSeconds(30));

        result.AddedCount.Should().Be(0);
        AiSessionRecord row = _repository.Rows.Should().ContainSingle().Subject;
        row.IsActive.Should().BeTrue();
        row.MetadataJson.Should().Contain("Codex:session:t1");
    }

    [Fact]
    public async Task Never_duplicates_an_explicit_run_or_watch_session()
    {
        var watched = new AiSessionRecord
        {
            Id = Guid.NewGuid(),
            Provider = AiSessionProvider.Generic,
            Title = "octadock watch",
            Pid = 100,
            Status = AiSessionStatus.Running,
            StartedAt = ObservedAt.AddMinutes(-10),
            MetadataJson = """{"source":"watch-pid"}""",
        };
        await _repository.AddAsync(watched);

        AiSessionObservation observation = Observation("ClaudeCode:pid:100:5", pid: 100);
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt);

        result.AddedCount.Should().Be(0);
        _repository.Rows.Should().ContainSingle().Which.Id.Should().Be(watched.Id);
    }

    [Fact]
    public async Task Never_duplicates_a_run_session_that_watches_the_wrapper_process()
    {
        // octadock run stores the cmd.exe wrapper pid (50); discovery
        // classifies the descendant worker (pid 100) whose ancestor chain
        // includes the wrapper.
        var watched = new AiSessionRecord
        {
            Id = Guid.NewGuid(),
            Provider = AiSessionProvider.Generic,
            Title = "octadock run",
            Pid = 50,
            Status = AiSessionStatus.Running,
            StartedAt = ObservedAt.AddMinutes(-10),
            MetadataJson = """{"source":"run"}""",
        };
        await _repository.AddAsync(watched);

        AiSessionObservation observation = Observation(
            "ClaudeCode:pid:100:5",
            pid: 100,
            extraMetadata: new Dictionary<string, string> { ["ancestorPids"] = "50,10" });
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            Resolution([observation]),
            ObservedAt);

        result.AddedCount.Should().Be(0);
        _repository.Rows.Should().ContainSingle().Which.Id.Should().Be(watched.Id);
    }

    [Fact]
    public async Task Weaker_observation_update_preserves_session_identity_metadata()
    {
        AiSessionObservation withId = Observation("ClaudeCode:session:sA", pid: 100, sessionId: "sA");
        await _coordinator.SyncAsync(Resolution([withId]), ObservedAt);

        // The transcript went quiet: the next observation is process-only.
        AiSessionObservation pidOnly = Observation("ClaudeCode:pid:100:5", pid: 100, startTicks: 5);
        await _coordinator.SyncAsync(Resolution([pidOnly]), ObservedAt.AddMinutes(2));

        AiSessionRecord row = _repository.Rows.Should().ContainSingle().Subject;
        row.IsActive.Should().BeTrue();
        row.MetadataJson.Should().Contain("\"sessionId\":\"sA\"");

        // When the transcript wakes up again, tier-3 finds the same row.
        AiSessionDiscoverySyncResult third = await _coordinator.SyncAsync(
            Resolution([withId]),
            ObservedAt.AddMinutes(4));
        third.AddedCount.Should().Be(0);
        _repository.Rows.Should().ContainSingle();
    }

    [Fact]
    public async Task Workspace_comatch_is_not_completed_as_a_merge_duplicate()
    {
        // Two state-only rows with different session ids in the same folder
        // (seeded exactly as the pipeline writes transcript-backed rows);
        // a pid-only observation may adopt one but must not merge away the other.
        await SeedTranscriptRowAsync("sA", @"C:\Repo");
        await SeedTranscriptRowAsync("sB", @"C:\Repo");

        AiSessionObservation observation = Observation(
            "ClaudeCode:pid:100:5",
            pid: 100,
            startTicks: 5,
            workspace: @"C:\Repo");
        AiSessionDiscoverySyncResult result = await _coordinator.SyncAsync(
            new AiSessionResolution(
                [observation],
                [],
                new HashSet<string>([ProcessSnapshotEvidenceCollector.SourceId], StringComparer.OrdinalIgnoreCase)),
            ObservedAt);

        result.AddedCount.Should().Be(0);
        result.CompletedCount.Should().Be(0);
        _repository.Rows.Should().HaveCount(2);
        _repository.Rows.Should().OnlyContain(r => r.IsActive);
        _repository.EventsFor(_repository.Rows[0].Id)
            .Concat(_repository.EventsFor(_repository.Rows[1].Id))
            .Should().NotContain(e => e.Message != null && e.Message.Contains("Merged into"));
    }

    [Fact]
    public async Task Stale_row_completes_even_when_its_backing_source_keeps_failing()
    {
        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            sessionId: "t1",
            detector: "codex-state-thread",
            extraMetadata: new Dictionary<string, string> { ["codexThreadId"] = "t1" });
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        // codex-state fails on every later pass (e.g. state db deleted), but
        // after the stale cutoff the row completes anyway.
        var withoutCodex = new AiSessionResolution(
            [],
            [],
            new HashSet<string>([ProcessSnapshotEvidenceCollector.SourceId], StringComparer.OrdinalIgnoreCase));
        AiSessionDiscoverySyncResult early = await _coordinator.SyncAsync(withoutCodex, ObservedAt.AddMinutes(10));
        early.CompletedCount.Should().Be(0);

        AiSessionDiscoverySyncResult late = await _coordinator.SyncAsync(withoutCodex, ObservedAt.AddHours(7));
        late.CompletedCount.Should().Be(1);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Completed);
    }

    [Fact]
    public async Task Declassified_process_grace_is_time_based_not_scan_counted()
    {
        _alivePids.Add(100);
        AiSessionObservation observation = Observation("ClaudeCode:pid:100:5", pid: 100);
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        // Rapid overlay-driven scans within the grace window never complete.
        (await _coordinator.SyncAsync(Resolution([]), ObservedAt.AddSeconds(2))).CompletedCount.Should().Be(0);
        (await _coordinator.SyncAsync(Resolution([]), ObservedAt.AddSeconds(4))).CompletedCount.Should().Be(0);
        (await _coordinator.SyncAsync(Resolution([]), ObservedAt.AddSeconds(6))).CompletedCount.Should().Be(0);

        // Once the grace window elapses, the row completes.
        (await _coordinator.SyncAsync(Resolution([]), ObservedAt.AddSeconds(30))).CompletedCount.Should().Be(1);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Completed);
    }

    [Fact]
    public async Task Provider_state_activity_advances_LastEventAt()
    {
        AiSessionObservation observation = Observation(
            "Codex:session:t1",
            sessionId: "t1",
            lastActivityAt: ObservedAt.AddMinutes(-1));
        await _coordinator.SyncAsync(Resolution([observation]), ObservedAt);

        DateTimeOffset newer = ObservedAt.AddMinutes(2);
        await _coordinator.SyncAsync(
            Resolution([observation with { LastActivityAt = newer }]),
            ObservedAt.AddMinutes(2));

        _repository.Rows.Single().LastEventAt.Should().Be(newer);
    }

    [Fact]
    public async Task Facade_scan_uses_injected_collectors_and_syncs_rows_end_to_end()
    {
        var collector = new FakeCollector(CodexStateEvidenceCollector.SourceId);
        collector.Evidence.Add(new AiSessionEvidence
        {
            Source = CodexStateEvidenceCollector.SourceId,
            Detector = "codex-state-thread",
            Provider = AiSessionProvider.Codex,
            Confidence = AiSessionConfidence.Certain,
            Reason = "Codex thread with fresh rollout activity.",
            Title = "Codex - Repo",
            WorkspacePath = @"C:\Repo",
            ProviderSessionId = "t1",
            StartedAt = ObservedAt.AddMinutes(-3),
            LastActivityAt = ObservedAt.AddSeconds(-5),
            StatusHint = AiSessionStatus.Running,
            Metadata = new Dictionary<string, string> { ["codexThreadId"] = "t1" },
        });
        var clock = new TestClock(ObservedAt);
        using var service = new AiSessionDiscoveryService(
            _repository,
            clock,
            NullLogger<AiSessionDiscoveryService>.Instance,
            [collector]);

        AiSessionDiscoveryResult first = await service.ScanOnceAsync();
        first.AddedCount.Should().Be(1);
        _repository.Rows.Should().ContainSingle().Which.IsActive.Should().BeTrue();

        // The provider state stops reporting the thread: the row completes
        // without any UI-driven refresh logic involved.
        collector.Evidence.Clear();
        clock.UtcNow = ObservedAt.AddMinutes(1);
        AiSessionDiscoveryResult second = await service.ScanOnceAsync();
        second.CompletedCount.Should().Be(1);
        _repository.Rows.Single().Status.Should().Be(AiSessionStatus.Completed);
    }

    private static AiSessionResolution Resolution(
        IReadOnlyList<AiSessionObservation> observations)
        => new(
            observations,
            [],
            new HashSet<string>(AllSources, StringComparer.OrdinalIgnoreCase));

    private static AiSessionObservation Observation(
        string discoveryKey,
        int? pid = null,
        long? startTicks = null,
        string? sessionId = null,
        string? workspace = null,
        string detector = "test-detector",
        DateTimeOffset? lastActivityAt = null,
        IReadOnlyDictionary<string, string>? extraMetadata = null)
    {
        AiSessionProvider provider = discoveryKey.StartsWith("Codex", StringComparison.Ordinal)
            ? AiSessionProvider.Codex
            : AiSessionProvider.ClaudeCode;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (sessionId is not null)
        {
            metadata["sessionId"] = sessionId;
        }

        if (startTicks is not null)
        {
            metadata["processStartTicks"] = startTicks.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (workspace is not null)
        {
            metadata["workingDirectory"] = workspace;
        }

        if (extraMetadata is not null)
        {
            foreach (KeyValuePair<string, string> entry in extraMetadata)
            {
                metadata[entry.Key] = entry.Value;
            }
        }

        return new AiSessionObservation
        {
            DiscoveryKey = discoveryKey,
            Provider = provider,
            Title = $"{provider} session",
            Confidence = 0.9,
            Status = AiSessionStatus.Running,
            Detector = detector,
            Reason = "Test detection reason.",
            Pid = pid,
            ProcessStartTicks = startTicks,
            WorkspacePath = workspace,
            ProviderSessionId = sessionId,
            StartedAt = ObservedAt.AddMinutes(-5),
            LastActivityAt = lastActivityAt,
            Sources = [ProcessSnapshotEvidenceCollector.SourceId],
            Metadata = metadata,
        };
    }

    private Task SeedTranscriptRowAsync(string sessionId, string workspace)
        => SeedRowAsync(
            $"ClaudeCode:session:{sessionId}",
            provider: AiSessionProvider.ClaudeCode,
            sessionId: sessionId,
            workspace: workspace,
            metadataOverride: new Dictionary<string, string>
            {
                ["source"] = "process-discovery",
                ["discoveryKey"] = $"ClaudeCode:session:{sessionId}",
                ["detector"] = "claude-project-transcript",
                ["sessionId"] = sessionId,
                ["claudeSessionId"] = sessionId,
                ["workingDirectory"] = workspace,
            });

    private async Task SeedRowAsync(
        string? discoveryKey = null,
        AiSessionProvider provider = AiSessionProvider.Codex,
        int? pid = null,
        long? startTicks = null,
        string? sessionId = null,
        string? workspace = null,
        Dictionary<string, string>? metadataOverride = null)
    {
        Dictionary<string, string> metadata = metadataOverride ?? new Dictionary<string, string>
        {
            ["source"] = "process-discovery",
        };
        if (metadataOverride is null)
        {
            if (discoveryKey is not null)
            {
                metadata["discoveryKey"] = discoveryKey;
            }

            if (sessionId is not null)
            {
                metadata["sessionId"] = sessionId;
            }

            if (startTicks is not null)
            {
                metadata["processStartTicks"] = startTicks.Value.ToString(CultureInfo.InvariantCulture);
            }

            if (workspace is not null)
            {
                metadata["workingDirectory"] = workspace;
            }
        }

        await _repository.AddAsync(new AiSessionRecord
        {
            Id = Guid.NewGuid(),
            Provider = provider,
            Title = "Seeded session",
            Cwd = workspace,
            Pid = pid,
            Status = AiSessionStatus.Running,
            StartedAt = ObservedAt.AddMinutes(-10),
            LastEventAt = ObservedAt.AddMinutes(-10),
            MetadataJson = System.Text.Json.JsonSerializer.Serialize(metadata),
        });
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public DateTimeOffset LocalNow => UtcNow;
    }

    private sealed class FakeCollector(string source) : IAiSessionEvidenceCollector
    {
        public List<AiSessionEvidence> Evidence { get; } = [];

        public string Source { get; } = source;

        public Task<AiSessionEvidenceBatch> CollectAsync(
            DateTimeOffset observedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(new AiSessionEvidenceBatch(Source, Succeeded: true, Evidence.ToArray()));
    }

    private sealed class FakeAiSessionRepository : IAiSessionRepository
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, AiSessionRecord> _sessions = [];
        private readonly List<AiSessionEventRecord> _events = [];
        private readonly List<AiSessionArtifactRecord> _artifacts = [];

        public IReadOnlyList<AiSessionRecord> Rows
        {
            get
            {
                lock (_gate)
                {
                    return _sessions.Values.ToArray();
                }
            }
        }

        public IReadOnlyList<AiSessionEventRecord> EventsFor(Guid sessionId)
        {
            lock (_gate)
            {
                return _events.Where(e => e.SessionId == sessionId).ToArray();
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
                IEnumerable<AiSessionRecord> query = _sessions.Values;
                if (filter.Statuses is { Count: > 0 })
                {
                    query = query.Where(r => filter.Statuses.Contains(r.Status));
                }

                return Task.FromResult<IReadOnlyList<AiSessionRecord>>(query
                    .OrderByDescending(r => r.LastEventAt ?? r.StartedAt)
                    .Take(filter.Limit)
                    .ToArray());
            }
        }

        public Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _events.Add(record);
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
}
