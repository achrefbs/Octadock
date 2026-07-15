using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.CaptureUx;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Core.Recording;

namespace Octadock.App.Services;

/// <summary>
/// Minimal start/stop wiring over <see cref="IRecordingEngine"/>: one action
/// (hotkey, tray item, or the <c>record-screen</c> command) toggles recording of
/// the active monitor or a selected region to an MP4 in the configured save
/// folder (or Videos\Octadock). First functional slice of the recording
/// milestone; feedback is via notifications and the compact recording pill.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1001:Types that own disposable fields should be disposable",
    Justification = "This DI singleton and its operation gate share the process lifetime; disposing the gate during an active toggle would introduce a shutdown race.")]
public sealed class RecordingController
{
    private readonly IRecordingEngine _engine;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly IMonitorService _monitors;
    private readonly IRegionSelectionService _regionSelection;
    private readonly IStoragePaths _paths;
    private readonly ICaptureRepository _captureRepository;
    private readonly IActionRepository _actionRepository;
    private readonly IShelfService _shelf;
    private readonly ILicenseGate _licenseGate;
    private readonly ILogger<RecordingController> _logger;
    private readonly SemaphoreSlim _toggleGate = new(1, 1);
    private RecordingPill? _pill;
    private Guid? _activeRecordingId;
    private DateTimeOffset? _activeRecordingCreatedAt;
    private string? _activeRecordingOutputPath;
    private bool _activeRecordingManaged;
    private int _engineSessionStarted;
    private int _engineFailureRecoveryStarted;

    public RecordingController(
        IRecordingEngine engine,
        ISettingsService settings,
        INotificationService notifications,
        IMonitorService monitors,
        IRegionSelectionService regionSelection,
        IStoragePaths paths,
        ICaptureRepository captureRepository,
        IActionRepository actionRepository,
        IShelfService shelf,
        ILicenseGate licenseGate,
        ILogger<RecordingController> logger)
    {
        _engine = engine;
        _settings = settings;
        _notifications = notifications;
        _monitors = monitors;
        _regionSelection = regionSelection;
        _paths = paths;
        _captureRepository = captureRepository;
        _actionRepository = actionRepository;
        _shelf = shelf;
        _licenseGate = licenseGate;
        _logger = logger;

        // Per-frame progress from the pump thread drives the pill's timer.
        _engine.ProgressChanged += (_, progress) =>
        {
            Application.Current?.Dispatcher.BeginInvoke(() => _pill?.Update(progress));
            if (progress.State == RecordingState.Failed && Volatile.Read(ref _engineSessionStarted) == 1)
            {
                BeginEngineFailureRecovery();
            }
        };
    }

    /// <summary>True while a session is active (drives the tray item label).</summary>
    public bool IsRecording =>
        _engine.State is RecordingState.Countdown or RecordingState.Recording or RecordingState.Paused;

    /// <summary>Starts active-monitor recording if idle; stops and saves if one is running.</summary>
    public Task ToggleAsync(CancellationToken cancellationToken = default)
        => ToggleAsync(RecordingStartRequest.ActiveMonitor, cancellationToken);

    /// <summary>Starts a requested recording target if idle; stops and saves if one is running.</summary>
    public async Task ToggleAsync(RecordingStartRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // One toggle at a time: a double-press must not race start against stop.
        if (!await _toggleGate.WaitAsync(TimeSpan.Zero, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            switch (_engine.State)
            {
                case RecordingState.Recording:
                case RecordingState.Paused:
                    await StopAsync(cancellationToken).ConfigureAwait(false);
                    break;

                case RecordingState.Countdown:
                case RecordingState.Finalizing:
                    _notifications.Notify(
                        "Recording", "The recorder is busy — try again in a moment.", NotificationKind.Info);
                    break;

                case RecordingState.Failed:
                    Interlocked.Exchange(ref _engineFailureRecoveryStarted, 1);
                    await RecoverFailedRecordingCoreAsync(cancellationToken).ConfigureAwait(false);
                    break;

                default:
                    // Trial/license gate (WS5): starting a new recording is blocked
                    // post-expiry; stopping an in-progress one always completes.
                    if (_licenseGate.Allow(GatedFeature.Recording))
                    {
                        await StartAsync(request, cancellationToken).ConfigureAwait(false);
                    }

                    break;
            }
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task StartAsync(RecordingStartRequest request, CancellationToken cancellationToken)
    {
        if (!_engine.IsSupported)
        {
            _notifications.Notify(
                "Recording unavailable",
                "Screen recording needs Windows 10 2004 or later.",
                NotificationKind.Warning);
            return;
        }

        ResolvedRecordingTarget? target = await ResolveTargetAsync(request, cancellationToken).ConfigureAwait(false);
        if (target is null)
        {
            return;
        }

        var settings = _settings.Current;
        var capture = settings.Capture;
        var recording = settings.Recording;
        DateTimeOffset createdAt = DateTimeOffset.Now;
        Guid recordingId = Guid.NewGuid();
        bool managedOutput = string.IsNullOrWhiteSpace(capture.SaveDirectory);

        string output;
        if (managedOutput)
        {
            string relative = _paths.BuildRecordingRelativePath(recordingId, createdAt, ".mp4");
            output = _paths.ToAbsolute(relative);
        }
        else
        {
            output = Path.Combine(
                capture.SaveDirectory, $"Recording {createdAt:yyyy-MM-dd 'at' HH.mm.ss}.mp4");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        // Pin the target monitor explicitly so the engine and the pill agree.
        DisplayInfo monitor = target.Value.Monitor;
        var options = new RecordingOptions
        {
            OutputPath = output,
            Monitor = monitor.Id,
            Region = target.Value.Region,
            Fps = recording.Fps,
            Quality = recording.Quality,
            IncludeCursor = recording.IncludeCursor,
            IncludeMicrophone = false,
            IncludeSystemAudio = false,
            CountdownSeconds = 3,
        };

        try
        {
            Volatile.Write(ref _engineSessionStarted, 0);
            Interlocked.Exchange(ref _engineFailureRecoveryStarted, 0);
            _activeRecordingId = recordingId;
            _activeRecordingCreatedAt = createdAt;
            _activeRecordingOutputPath = output;
            _activeRecordingManaged = managedOutput;

            if (recording.IncludeMicrophone || recording.IncludeSystemAudio)
            {
                _notifications.Notify(
                    "Recording audio unavailable",
                    "This build records video only; audio tracks are not encoded yet.",
                    NotificationKind.Warning);
            }

            await ShowPillAsync(monitor).ConfigureAwait(false);
            await _engine.StartAsync(options, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _engineSessionStarted, 1);
            if (_engine.State == RecordingState.Failed)
            {
                BeginEngineFailureRecovery();
            }
        }
        catch (OperationCanceledException)
        {
            Volatile.Write(ref _engineSessionStarted, 0);
            TryDeleteActiveRecordingOutput();
            ClearActiveRecordingMetadata();
            await ClosePillAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _engineSessionStarted, 0);
            _logger.LogError(ex, "Failed to start recording.");
            _notifications.Notify("Recording failed", ex.Message, NotificationKind.Error);
            TryDeleteActiveRecordingOutput();
            ClearActiveRecordingMetadata();
            await ClosePillAsync().ConfigureAwait(false);
        }
    }

    private async Task<ResolvedRecordingTarget?> ResolveTargetAsync(
        RecordingStartRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Region is { } explicitRegion)
        {
            PixelRect region = explicitRegion.Normalized();
            if (region.IsEmpty)
            {
                _notifications.Notify(
                    "Recording unavailable",
                    "The selected recording area is empty.",
                    NotificationKind.Warning);
                return null;
            }

            return new ResolvedRecordingTarget(_monitors.GetMonitorFromPoint(region.Center), region);
        }

        if (request.PromptForRegion)
        {
            _notifications.Notify("Recording", "Select the area to record.", NotificationKind.Info);
            RegionSelection selection = await _regionSelection.SelectAreaAsync(cancellationToken).ConfigureAwait(false);
            if (!selection.Confirmed || selection.Region.IsEmpty)
            {
                _logger.LogDebug("Area recording selection cancelled.");
                return null;
            }

            PixelRect region = selection.Region.Normalized();
            _regionSelection.HideAll();
            await Task.Delay(30, cancellationToken).ConfigureAwait(false);
            return new ResolvedRecordingTarget(_monitors.GetMonitorFromPoint(region.Center), region);
        }

        if (request.Monitor is { } monitorId)
        {
            DisplayInfo? monitor = _monitors.FindById(monitorId);
            if (monitor is null)
            {
                _notifications.Notify(
                    "Recording unavailable",
                    "The requested monitor is no longer available.",
                    NotificationKind.Warning);
                return null;
            }

            return new ResolvedRecordingTarget(monitor, Region: null);
        }

        return new ResolvedRecordingTarget(_monitors.GetActiveMonitor(), Region: null);
    }

    private async Task ShowPillAsync(DisplayInfo monitor)
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            _pill?.Close();
            _pill = new RecordingPill();
            _pill.StopRequested += (_, _) => _ = ToggleAsync();
            _pill.PauseToggleRequested += (_, _) => TogglePause();
            _pill.ShowOn(monitor);
            App.Dock?.SetRecording(true);
        });
    }

    private async Task ClosePillAsync()
    {
        Dispatcher? dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        await dispatcher.InvokeAsync(() =>
        {
            _pill?.Close();
            _pill = null;
            App.Dock?.SetRecording(false);
        });
    }

    private void TogglePause()
    {
        try
        {
            if (_engine.State == RecordingState.Paused)
            {
                _engine.Resume();
            }
            else if (_engine.State == RecordingState.Recording)
            {
                _engine.Pause();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pause/resume failed.");
        }
    }

    private async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            RecordingResult result = await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            double seconds = Math.Round(result.DurationMs / 1000.0, 1);
            await AddToHistoryAndShelfAsync(result, cancellationToken).ConfigureAwait(false);

            _notifications.Notify(
                "Video saved",
                $"{Path.GetFileName(result.OutputPath)} ({seconds}s)",
                NotificationKind.Success,
                () => RevealInExplorer(result.OutputPath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop the recording.");
            _notifications.Notify("Recording failed", ex.Message, NotificationKind.Error);
            TryDeleteActiveRecordingOutput();
        }
        finally
        {
            Volatile.Write(ref _engineSessionStarted, 0);
            await ClosePillAsync().ConfigureAwait(false);
            ClearActiveRecordingMetadata();
        }
    }

    private void BeginEngineFailureRecovery()
    {
        if (Interlocked.CompareExchange(ref _engineFailureRecoveryStarted, 1, 0) != 0)
        {
            return;
        }

        _ = RecoverFailedRecordingAsync();
    }

    private async Task RecoverFailedRecordingAsync()
    {
        await _toggleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_engine.State == RecordingState.Failed && _activeRecordingOutputPath is not null)
            {
                await RecoverFailedRecordingCoreAsync(CancellationToken.None).ConfigureAwait(false);
            }
        }
        finally
        {
            _toggleGate.Release();
        }
    }

    private async Task RecoverFailedRecordingCoreAsync(CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await _engine.StopAsync(cancellationToken).ConfigureAwait(false);
            failure = new InvalidOperationException("The recording frame pump stopped unexpectedly.");
        }
        catch (Exception ex)
        {
            failure = ex;
            try
            {
                await _engine.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                _logger.LogDebug(cleanupException, "Failed to abandon the broken recording session.");
            }
        }
        finally
        {
            Volatile.Write(ref _engineSessionStarted, 0);
            string message = failure?.GetBaseException().Message
                ?? "The recording stopped before a valid MP4 could be created.";
            _logger.LogError(failure, "The recording frame pump stopped unexpectedly.");
            _notifications.Notify("Recording failed", message, NotificationKind.Error);
            TryDeleteActiveRecordingOutput();
            await ClosePillAsync().ConfigureAwait(false);
            ClearActiveRecordingMetadata();
        }
    }

    /// <summary>
    /// Records the finished MP4 in capture history and shows it on the shelf.
    /// Best-effort: a failure here must never break the save notification.
    /// </summary>
    private async Task AddToHistoryAndShelfAsync(RecordingResult result, CancellationToken cancellationToken)
    {
        bool managedPath = _activeRecordingManaged;
        var record = new CaptureRecord
        {
            Id = _activeRecordingId ?? Guid.NewGuid(),
            Type = CaptureType.Recording,
            CreatedAt = _activeRecordingCreatedAt ?? DateTimeOffset.Now,
            Source = CaptureSource.Empty,
            MonitorId = MonitorId.Unknown,
            PixelWidth = result.FrameSize.Width,
            PixelHeight = result.FrameSize.Height,
            OriginalPath = managedPath ? _paths.ToRelative(result.OutputPath) : result.OutputPath,
            DurationMs = result.DurationMs,
        };

        bool addedToHistory = false;
        try
        {
            await _captureRepository.AddAsync(record, cancellationToken).ConfigureAwait(false);
            addedToHistory = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add the recording to history.");
        }

        try
        {
            bool shown = await _shelf.ShowAsync(record, cancellationToken).ConfigureAwait(false);
            if (shown && addedToHistory)
            {
                await _actionRepository.AddAsync(
                    new ActionRecord
                    {
                        Id = Guid.NewGuid(),
                        CaptureId = record.Id,
                        ActionType = ActionType.Shelved,
                        CreatedAt = DateTimeOffset.Now,
                        Destination = "shelf",
                    },
                    cancellationToken).ConfigureAwait(false);
            }
            else
            {
                _notifications.Notify(
                    "Video saved",
                    "The recording was saved, but the shelf could not be shown.",
                    NotificationKind.Warning,
                    () => RevealInExplorer(result.OutputPath));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show the recording on the shelf.");
        }
    }

    private static void RevealInExplorer(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        using (System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true,
        }))
        {
        }
    }

    private void ClearActiveRecordingMetadata()
    {
        _activeRecordingId = null;
        _activeRecordingCreatedAt = null;
        _activeRecordingOutputPath = null;
        _activeRecordingManaged = false;
    }

    private void TryDeleteActiveRecordingOutput()
    {
        string? output = _activeRecordingOutputPath;
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        try
        {
            if (File.Exists(output))
            {
                File.Delete(output);
                _logger.LogInformation("Deleted incomplete recording output {Path}.", output);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to delete incomplete recording output {Path}.", output);
        }
    }

    private readonly record struct ResolvedRecordingTarget(DisplayInfo Monitor, PixelRect? Region);
}
