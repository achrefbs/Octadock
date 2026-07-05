using System.Diagnostics;
using System.IO;
using System.Text;
using Octadock.App.Services.AiSessionDiscovery;
using Xunit;

namespace Octadock.App.Tests;

/// <summary>
/// On-demand diagnostic that runs the real discovery pipeline against the
/// live machine and dumps every stage to %TEMP%\octadock-live-scan.txt.
/// Opt-in via OCTADOCK_LIVE_DIAGNOSTIC=1 (it probes WMI, ~/.codex, and
/// ~/.claude, which belongs to interactive debugging, not CI):
/// set OCTADOCK_LIVE_DIAGNOSTIC=1 &amp;&amp; dotnet test --filter "Category=LiveDiagnostic"
/// </summary>
public sealed class LivePipelineDiagnosticTests
{
    [Fact]
    [Trait("Category", "LiveDiagnostic")]
    public async Task Dump_live_scan_pipeline()
    {
        if (Environment.GetEnvironmentVariable("OCTADOCK_LIVE_DIAGNOSTIC") != "1")
        {
            return;
        }

        string reportPath = Path.Combine(Path.GetTempPath(), "octadock-live-scan.txt");
        var sb = new StringBuilder();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        sb.AppendLine($"observedAt={observedAt:o}");

        IAiSessionEvidenceCollector[] collectors =
        [
            new ProcessSnapshotEvidenceCollector(),
            new CodexStateEvidenceCollector(),
            new ClaudeCodeStateEvidenceCollector(),
        ];

        var batches = new List<AiSessionEvidenceBatch>();
        foreach (IAiSessionEvidenceCollector collector in collectors)
        {
            var sw = Stopwatch.StartNew();
            Task<AiSessionEvidenceBatch> work = collector.CollectAsync(observedAt, CancellationToken.None);
            Task winner = await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20)));
            if (winner != work)
            {
                sb.AppendLine($"[{collector.Source}] TIMED OUT after 20s");
                continue;
            }

            AiSessionEvidenceBatch batch = await work;
            batches.Add(batch);
            sb.AppendLine($"[{batch.Source}] succeeded={batch.Succeeded} evidence={batch.Evidence.Count} elapsed={sw.ElapsedMilliseconds}ms failure={batch.FailureReason}");
            foreach (AiSessionEvidence e in batch.Evidence)
            {
                sb.AppendLine($"    {e.Detector} conf={e.Confidence:0.00} pid={e.Pid} hint={e.StatusHint} sid={e.ProviderSessionId} ws={e.WorkspacePath} lastAct={e.LastActivityAt:HH:mm:ss} title={e.Title}");
            }
        }

        AiSessionResolution resolution = new AiSessionIdentityResolver().Resolve(batches, observedAt);
        sb.AppendLine($"observations={resolution.Observations.Count} dropped={resolution.Dropped.Count} succeededSources={string.Join("+", resolution.SucceededSources)}");
        foreach (AiSessionObservation o in resolution.Observations)
        {
            sb.AppendLine($"  OBS {o.DiscoveryKey} status={o.Status} conf={o.Confidence:0.00} pid={o.Pid} title={o.Title} ws={o.WorkspacePath}");
        }

        foreach (AiSessionResolutionDrop d in resolution.Dropped)
        {
            sb.AppendLine($"  DROP {d.Evidence.Detector} pid={d.Evidence.Pid} title={d.Evidence.Title}: {d.Reason}");
        }

        File.WriteAllText(reportPath, sb.ToString());
    }
}
