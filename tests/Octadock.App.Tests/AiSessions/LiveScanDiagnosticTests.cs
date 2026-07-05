using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Common;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Xunit;
using Xunit.Abstractions;

namespace Octadock.App.Tests.AiSessions;

/// <summary>
/// TEMPORARY diagnostic: runs a real discovery scan against this machine and
/// prints what it finds. Not a behavioral assertion beyond "does not throw".
/// </summary>
public sealed class LiveScanDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public LiveScanDiagnosticTests(ITestOutputHelper output) => _output = output;

    [Fact(Skip = "Machine-dependent diagnostic: run manually with an AI CLI active to inspect live discovery.")]
    public async Task Live_scan_finds_running_ai_processes()
    {
        var repository = new CapturingRepository();
        using var loggerFactory = LoggerFactory.Create(b => { });
        var discovery = new AiSessionDiscoveryService(
            repository,
            SystemClock.Instance,
            loggerFactory.CreateLogger<AiSessionDiscoveryService>());

        var stopwatch = Stopwatch.StartNew();
        AiSessionDiscoveryResult result = await discovery.ScanOnceAsync();
        stopwatch.Stop();

        _output.WriteLine($"scan took {stopwatch.ElapsedMilliseconds} ms");
        _output.WriteLine($"candidates={result.DetectedCount} added={result.AddedCount} completed={result.CompletedCount}");
        foreach (AiSessionRecord row in repository.Added)
        {
            _output.WriteLine($"ADDED: {row.Provider} | {row.Title} | pid={row.Pid} | start={row.StartedAt:O}");
        }

        result.DetectedCount.Should().BeGreaterThan(0, "Claude Code is running on this machine right now");
    }

    private sealed class CapturingRepository : IAiSessionRepository
    {
        public List<AiSessionRecord> Added { get; } = [];

        public Task AddAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
        {
            Added.Add(record);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<AiSessionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<AiSessionRecord?>(null);

        public Task<IReadOnlyList<AiSessionRecord>> ListAsync(
            AiSessionFilter filter, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiSessionRecord>>([]);

        public Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AiSessionEventRecord>> GetEventsAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiSessionEventRecord>>([]);

        public Task AddArtifactAsync(AiSessionArtifactRecord record, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<AiSessionArtifactRecord>> GetArtifactsAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<AiSessionArtifactRecord>>([]);
    }
}
