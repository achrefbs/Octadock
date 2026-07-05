using System.Management;
using System.Runtime.Versioning;

namespace Octadock.App.Services.AiSessionDiscovery;

/// <summary>Kind of process lifetime transition observed.</summary>
public enum AiSessionProcessChangeKind
{
    Started,
    Stopped,
}

/// <summary>One process start/stop notification.</summary>
public sealed record AiSessionProcessChange(
    AiSessionProcessChangeKind Kind,
    int Pid,
    string ProcessName);

/// <summary>
/// Event-driven process watcher built on WMI intrinsic instance events
/// (no admin rights required, unlike Win32_ProcessStartTrace). When WMI
/// eventing is unavailable the watcher fails soft and discovery falls back
/// to pure polling.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProcessLifetimeWatcher : IDisposable
{
    private readonly object _gate = new();
    private ManagementEventWatcher? _creationWatcher;
    private ManagementEventWatcher? _deletionWatcher;
    private Action<AiSessionProcessChange>? _onChange;
    private bool _disposed;

    /// <summary>Starts both watchers; false when WMI eventing is unavailable.</summary>
    public bool TryStart(Action<AiSessionProcessChange> onChange)
    {
        ArgumentNullException.ThrowIfNull(onChange);

        lock (_gate)
        {
            if (_disposed || _creationWatcher is not null)
            {
                return _creationWatcher is not null;
            }

            try
            {
                _onChange = onChange;
                _creationWatcher = CreateWatcher(
                    "__InstanceCreationEvent",
                    AiSessionProcessChangeKind.Started);
                _deletionWatcher = CreateWatcher(
                    "__InstanceDeletionEvent",
                    AiSessionProcessChangeKind.Stopped);
                _creationWatcher.Start();
                _deletionWatcher.Start();
                return true;
            }
            catch (Exception ex) when (ex is ManagementException
                or UnauthorizedAccessException
                or TypeInitializationException
                or System.Runtime.InteropServices.COMException)
            {
                StopCore();
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            StopCore();
        }
    }

    private ManagementEventWatcher CreateWatcher(string eventClass, AiSessionProcessChangeKind kind)
    {
        var watcher = new ManagementEventWatcher(new WqlEventQuery(
            eventClass,
            TimeSpan.FromSeconds(2),
            "TargetInstance ISA 'Win32_Process'"));
        watcher.EventArrived += (_, e) => HandleEvent(e, kind);
        return watcher;
    }

    private void HandleEvent(EventArrivedEventArgs e, AiSessionProcessChangeKind kind)
    {
        try
        {
            if (e.NewEvent?["TargetInstance"] is not ManagementBaseObject target)
            {
                return;
            }

            using (target)
            {
                int pid = target["ProcessId"] switch
                {
                    uint value => checked((int)value),
                    int value => value,
                    _ => 0,
                };
                if (pid <= 0)
                {
                    return;
                }

                string name = AiSessionTextSanitizer.NormalizeProcessName(target["Name"] as string);
                _onChange?.Invoke(new AiSessionProcessChange(kind, pid, name));
            }
        }
        catch (Exception ex) when (ex is ManagementException or InvalidCastException or OverflowException)
        {
            // Malformed event payloads are ignored; polling remains the safety net.
        }
    }

    private void StopCore()
    {
        DisposeWatcher(ref _creationWatcher);
        DisposeWatcher(ref _deletionWatcher);
        _onChange = null;
    }

    private static void DisposeWatcher(ref ManagementEventWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.Stop();
        }
        catch (Exception ex) when (ex is ManagementException
            or InvalidOperationException
            or System.Runtime.InteropServices.COMException)
        {
            // Best-effort stop during shutdown.
        }

        watcher.Dispose();
        watcher = null;
    }
}
