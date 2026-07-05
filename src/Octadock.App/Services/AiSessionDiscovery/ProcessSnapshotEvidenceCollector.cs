using System.Diagnostics;
using System.Management;
using System.Runtime.Versioning;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>
/// Enumerates local processes (WMI when available, <see cref="Process"/>
/// fallback otherwise), classifies them through the provider registry, and
/// collapses parent/child chains so one CLI wrapper + worker pair yields one
/// evidence record.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessSnapshotEvidenceCollector : IAiSessionEvidenceCollector
{
    public const string SourceId = "process-snapshot";

    private readonly AiSessionProcessClassifierRegistry _registry;
    private readonly Func<IReadOnlyList<AiSessionProcessSnapshot>> _snapshotProvider;

    public ProcessSnapshotEvidenceCollector(
        AiSessionProcessClassifierRegistry? registry = null,
        Func<IReadOnlyList<AiSessionProcessSnapshot>>? snapshotProvider = null)
    {
        _registry = registry ?? new AiSessionProcessClassifierRegistry();
        _snapshotProvider = snapshotProvider ?? EnumerateProcessSnapshots;
    }

    public string Source => SourceId;

    public Task<AiSessionEvidenceBatch> CollectAsync(DateTimeOffset observedAt, CancellationToken cancellationToken)
        => Task.Run(
            () =>
            {
                try
                {
                    IReadOnlyList<AiSessionProcessSnapshot> snapshots = _snapshotProvider();
                    return new AiSessionEvidenceBatch(
                        SourceId,
                        Succeeded: true,
                        Collect(snapshots, observedAt));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    return AiSessionEvidenceBatch.Failed(SourceId, $"Process enumeration failed: {ex.Message}");
                }
            },
            cancellationToken);

    /// <summary>Classifies a snapshot set and collapses same-provider ancestor chains.</summary>
    public IReadOnlyList<AiSessionEvidence> Collect(
        IReadOnlyList<AiSessionProcessSnapshot> snapshots,
        DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(snapshots);

        var byPid = new Dictionary<int, AiSessionProcessSnapshot>();
        foreach (AiSessionProcessSnapshot snapshot in snapshots)
        {
            byPid[snapshot.Pid] = snapshot;
        }

        var accepted = new List<AiSessionEvidence>();
        foreach (AiSessionProcessSnapshot snapshot in snapshots)
        {
            AiSessionProcessClassification? classification =
                _registry.Classify(AiSessionProcessContext.Create(snapshot, observedAt));
            if (classification is { IsAccepted: true, Evidence: not null })
            {
                accepted.Add(classification.Evidence);
            }
        }

        return CollapseProcessTrees(accepted, byPid)
            .Select(e => AttachAncestorChain(e, byPid))
            .ToArray();
    }

    /// <summary>
    /// When a wrapper spawns its worker DIRECTLY and both classify for the same
    /// provider, keep the worker (it carries the richer command line) and drop
    /// the wrapper so one logical session yields one evidence record. Only the
    /// direct parent link counts: an indirect chain (session → shell → nested
    /// session, a real headless sub-invocation) is two distinct sessions. The
    /// parent must also have started no later than the child, which filters
    /// stale ParentPid links pointing at a reused PID.
    /// </summary>
    internal static IReadOnlyList<AiSessionEvidence> CollapseProcessTrees(
        IReadOnlyList<AiSessionEvidence> accepted,
        IReadOnlyDictionary<int, AiSessionProcessSnapshot> snapshotsByPid)
    {
        if (accepted.Count < 2)
        {
            return accepted;
        }

        var acceptedPids = new Dictionary<int, AiSessionEvidence>();
        foreach (AiSessionEvidence evidence in accepted)
        {
            if (evidence.Pid is int pid)
            {
                acceptedPids[pid] = evidence;
            }
        }

        var wrappersToDrop = new HashSet<int>();
        foreach (AiSessionEvidence evidence in accepted)
        {
            if (evidence.Pid is not int pid ||
                !snapshotsByPid.TryGetValue(pid, out AiSessionProcessSnapshot? self) ||
                self.ParentPid is not int parentPid ||
                parentPid <= 0 ||
                !acceptedPids.TryGetValue(parentPid, out AiSessionEvidence? parent) ||
                parent.Provider != evidence.Provider)
            {
                continue;
            }

            bool startOrderPlausible =
                parent.StartedAt is null ||
                evidence.StartedAt is null ||
                parent.StartedAt.Value <= evidence.StartedAt.Value.AddSeconds(1);
            if (startOrderPlausible)
            {
                wrappersToDrop.Add(parentPid);
            }
        }

        if (wrappersToDrop.Count == 0)
        {
            return accepted;
        }

        return accepted
            .Where(e => e.Pid is not int pid || !wrappersToDrop.Contains(pid))
            .ToArray();
    }

    /// <summary>
    /// Records the pid ancestor chain on the evidence so the coordinator can
    /// tell that a classified worker descends from an explicitly watched
    /// wrapper (octadock run/watch) and skip creating a duplicate row.
    /// </summary>
    internal static AiSessionEvidence AttachAncestorChain(
        AiSessionEvidence evidence,
        IReadOnlyDictionary<int, AiSessionProcessSnapshot> snapshotsByPid)
    {
        if (evidence.Pid is not int pid)
        {
            return evidence;
        }

        var chain = new List<int>(8);
        var visited = new HashSet<int> { pid };
        int? parent = snapshotsByPid.TryGetValue(pid, out AiSessionProcessSnapshot? self)
            ? self.ParentPid
            : null;
        while (parent is int parentPid && parentPid > 0 && chain.Count < 8 && visited.Add(parentPid))
        {
            chain.Add(parentPid);
            parent = snapshotsByPid.TryGetValue(parentPid, out AiSessionProcessSnapshot? node)
                ? node.ParentPid
                : null;
        }

        if (chain.Count == 0)
        {
            return evidence;
        }

        var metadata = evidence.Metadata is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(evidence.Metadata, StringComparer.OrdinalIgnoreCase);
        metadata["ancestorPids"] = string.Join(",", chain);
        return evidence with { Metadata = metadata };
    }

    /// <summary>WMI process table with command lines; falls back to Process.GetProcesses.</summary>
    public static IReadOnlyList<AiSessionProcessSnapshot> EnumerateProcessSnapshots()
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
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or TypeInitializationException)
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

    internal static DateTimeOffset? TryGetStartTime(Process process)
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

    private static uint? TryReadUInt(ManagementBaseObject process, string propertyName)
        => process.Properties[propertyName]?.Value switch
        {
            uint value => value,
            int value and >= 0 => checked((uint)value),
            _ => null,
        };

    private static string? TryReadString(ManagementBaseObject process, string propertyName)
        => AiSessionTextSanitizer.Clean(process.Properties[propertyName]?.Value as string);

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
}
