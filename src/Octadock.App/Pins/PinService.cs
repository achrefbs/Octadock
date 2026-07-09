using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Octadock.Core.Imaging;
using Octadock.Core.Licensing;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Pins;

/// <summary>
/// <see cref="IPinService"/>. Creates floating pins from captures, image files, or
/// the clipboard, restores pins persisted from a previous session, and tracks the
/// live pin windows so they can all be hidden or closed. Windows are created and
/// shown on the UI dispatcher; image decoding happens off the UI thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PinService : IPinService
{
    private readonly IImageLoadService _images;
    private readonly ICaptureRepository _captures;
    private readonly IPinRepository _pinRepository;
    private readonly IStoragePaths _paths;
    private readonly IClipboardService _clipboard;
    private readonly IMonitorService _monitors;
    private readonly ILicenseGate _licenseGate;
    private readonly ILogger<PinService> _logger;

    private readonly List<PinWindow> _pins = [];
    private readonly object _gate = new();
    private const string PinsFolder = "Pins";

    /// <summary>
    /// Minimum on-screen extent (physical pixels, per axis) a restored pin must retain
    /// before it is considered reachable; below this it is relocated on-screen.
    /// </summary>
    private const int MinVisiblePixels = 40;

    /// <summary>Creates the pin service.</summary>
    public PinService(
        IImageLoadService images,
        ICaptureRepository captures,
        IPinRepository pinRepository,
        IStoragePaths paths,
        IClipboardService clipboard,
        IMonitorService monitors,
        ILicenseGate licenseGate,
        ILogger<PinService> logger)
    {
        _images = images;
        _captures = captures;
        _pinRepository = pinRepository;
        _paths = paths;
        _clipboard = clipboard;
        _monitors = monitors;
        _licenseGate = licenseGate;
        _logger = logger;
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public async Task PinCaptureAsync(CaptureRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Trial/license gate (WS5): creating a new pin is new activity. Restoring
        // persisted pins on startup stays exempt (see RestorePersistedPinsAsync).
        if (!_licenseGate.Allow(GatedFeature.Pin))
        {
            return;
        }

        string path = _paths.ToAbsolute(record.OriginalPath);
        if (!File.Exists(path))
        {
            _logger.LogWarning("Cannot pin capture {Id}: file missing at {Path}.", record.Id, path);
            return;
        }

        BitmapSource image = await Task.Run(() => _images.LoadFromFile(path), cancellationToken).ConfigureAwait(false);
        await CreatePinAsync(
            image,
            record.Id,
            imagePath: path,
            pinId: null,
            bounds: null,
            opacity: 1.0,
            clickThrough: false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PinImageFileAsync(string filePath, CancellationToken cancellationToken = default)
    {
        // UNC guard FIRST (WS9, R32): reject network paths before any filesystem call
        // (File.Exists on \\host\share can leak NTLM credentials).
        if (Octadock.Core.Io.PathSafety.IsUncPath(filePath))
        {
            _logger.LogWarning("Refusing to pin a UNC/network path: {Path}.", filePath);
            return;
        }

        // Trial/license gate (WS5): creating a new pin is new activity.
        if (!_licenseGate.Allow(GatedFeature.Pin))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Cannot pin image: file missing at {Path}.", filePath);
            return;
        }

        string fullPath = Path.GetFullPath(filePath);
        BitmapSource image = await Task.Run(() => _images.LoadFromFile(fullPath), cancellationToken).ConfigureAwait(false);
        Guid pinId = Guid.NewGuid();
        await CreatePinAsync(image, null, fullPath, pinId, null, 1.0, false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PinFromClipboardAsync(CancellationToken cancellationToken = default)
    {
        // Trial/license gate (WS5): creating a new pin is new activity.
        if (!_licenseGate.Allow(GatedFeature.Pin))
        {
            return;
        }

        EncodedImage? clip = _clipboard.TryGetImage();
        if (clip is null)
        {
            _logger.LogInformation("Pin-from-clipboard requested but no image is on the clipboard.");
            return;
        }

        BitmapSource image = await Task.Run(() => Decode(clip.Bytes), cancellationToken).ConfigureAwait(false);
        Guid pinId = Guid.NewGuid();
        string imagePath = await SavePinImageAsync(pinId, image, cancellationToken).ConfigureAwait(false);
        await CreatePinAsync(image, null, imagePath, pinId, null, 1.0, false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RestorePersistedPinsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PinRecord> records;
        try
        {
            records = await _pinRepository.GetAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read persisted pins.");
            return;
        }

        foreach (PinRecord record in records)
        {
            try
            {
                BitmapSource? image = await LoadPinImageAsync(record, cancellationToken).ConfigureAwait(false);
                if (image is null)
                {
                    // Source is gone; drop the stale pin record.
                    await _pinRepository.DeleteAsync(record.Id, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                await CreatePinAsync(
                    image,
                    record.CaptureId,
                    record.ImagePath,
                    record.Id,
                    record.Bounds,
                    record.Opacity,
                    record.ClickThrough).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to restore pin {Id}.", record.Id);
            }
        }
    }

    /// <inheritdoc />
    public void HideAll()
    {
        OnUi(() =>
        {
            lock (_gate)
            {
                foreach (PinWindow pin in _pins)
                {
                    pin.Hide();
                }
            }
        });
    }

    /// <inheritdoc />
    public void CloseAll()
    {
        OnUi(() =>
        {
            PinWindow[] snapshot;
            lock (_gate)
            {
                snapshot = [.. _pins];
                _pins.Clear();
            }

            foreach (PinWindow pin in snapshot)
            {
                pin.Close();
            }
        });
    }

    /// <summary>Shows any hidden pins again (used by the tray "show pins" affordance).</summary>
    public void ShowAll()
    {
        OnUi(() =>
        {
            lock (_gate)
            {
                foreach (PinWindow pin in _pins)
                {
                    pin.Show();
                }
            }
        });
    }

    /// <summary>Unlocks every position-locked pin (a global unlock affordance for the tray).</summary>
    public void UnlockAll()
    {
        OnUi(() =>
        {
            lock (_gate)
            {
                foreach (PinWindow pin in _pins)
                {
                    pin.ForceUnlock();
                }
            }
        });
    }

    /// <summary>
    /// Brings every open pin fully back on-screen (used by the tray "Show All Pins"
    /// affordance). After a monitor layout change a pin can sit entirely outside the
    /// visible desktop; this shows each pin and clamps any that are stranded.
    /// </summary>
    public void GatherAllOnScreen()
    {
        OnUi(() =>
        {
            IReadOnlyList<DisplayInfo> monitors = SafeMonitors();
            DisplayInfo active = _monitors.GetActiveMonitor();

            lock (_gate)
            {
                foreach (PinWindow pin in _pins)
                {
                    pin.Show();
                    pin.EnsureOnScreen(monitors, active);
                }
            }
        });
    }

    /// <summary>
    /// Clamps a pin's saved <em>physical</em> bounds so it stays reachable. If the pin's
    /// saved rectangle still shows at least <see cref="MinVisiblePixels"/>×
    /// <see cref="MinVisiblePixels"/> on some monitor it is left untouched; otherwise it is
    /// relocated fully inside <paramref name="fallback"/>'s work area, preserving its size
    /// (capped to the work area) and clamping its position.
    /// </summary>
    public static PixelRect ClampToVisible(
        PixelRect bounds,
        IReadOnlyList<DisplayInfo> monitors,
        DisplayInfo fallback)
    {
        if (bounds.IsEmpty)
        {
            return bounds;
        }

        // If a meaningful slice of the pin is visible on any monitor, keep it as-is.
        foreach (DisplayInfo monitor in monitors)
        {
            PixelRect visible = bounds.Intersect(monitor.Bounds);
            if (visible.Width >= MinVisiblePixels && visible.Height >= MinVisiblePixels)
            {
                return bounds;
            }
        }

        // Otherwise the pin is (effectively) off-screen: pull it into the fallback
        // monitor's work area, preserving size where it fits.
        PixelRect work = fallback.WorkArea.IsEmpty ? fallback.Bounds : fallback.WorkArea;
        int width = Math.Min(bounds.Width, work.Width);
        int height = Math.Min(bounds.Height, work.Height);
        int x = Math.Clamp(bounds.X, work.Left, Math.Max(work.Left, work.Right - width));
        int y = Math.Clamp(bounds.Y, work.Top, Math.Max(work.Top, work.Bottom - height));
        return new PixelRect(x, y, width, height);
    }

    private async Task<BitmapSource?> LoadPinImageAsync(PinRecord record, CancellationToken cancellationToken)
    {
        if (record.CaptureId is not { } captureId)
        {
            if (string.IsNullOrWhiteSpace(record.ImagePath))
            {
                return null;
            }

            string managedPath = _paths.ToAbsolute(record.ImagePath);
            return File.Exists(managedPath)
                ? await Task.Run(() => _images.LoadFromFile(managedPath), cancellationToken).ConfigureAwait(false)
                : null;
        }

        CaptureRecord? capture = await _captures.GetAsync(captureId, cancellationToken).ConfigureAwait(false);
        if (capture is null)
        {
            return null;
        }

        string path = _paths.ToAbsolute(capture.OriginalPath);
        return File.Exists(path)
            ? await Task.Run(() => _images.LoadFromFile(path), cancellationToken).ConfigureAwait(false)
            : null;
    }

    private Task CreatePinAsync(
        BitmapSource image,
        Guid? captureId,
        string? imagePath,
        Guid? pinId,
        PixelRect? bounds,
        double opacity,
        bool clickThrough)
    {
        return Dispatcher.InvokeAsync(() =>
        {
            var window = ActivatorUtilities.CreateInstance<PinWindow>(App.Services);
            window.PinClosed += OnPinClosed;

            lock (_gate)
            {
                _pins.Add(window);
            }

            window.Initialize(image, captureId, imagePath, pinId, bounds, opacity, clickThrough);
            window.Activate();
        }).Task;
    }

    private async Task<string> SavePinImageAsync(Guid pinId, BitmapSource image, CancellationToken cancellationToken)
    {
        string relative = $"{PinsFolder}/{pinId:D}.png";
        string absolute = _paths.ToAbsolute(relative);
        Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);

        byte[] png = _images.EncodePng(image);
        await File.WriteAllBytesAsync(absolute, png, cancellationToken).ConfigureAwait(false);
        return relative;
    }

    private void OnPinClosed(object? sender, Guid pinId)
    {
        if (sender is not PinWindow window)
        {
            return;
        }

        window.PinClosed -= OnPinClosed;
        lock (_gate)
        {
            _pins.Remove(window);
        }
    }

    private IReadOnlyList<DisplayInfo> SafeMonitors()
    {
        try
        {
            return _monitors.GetMonitors();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate monitors while placing pins.");
            return [];
        }
    }

    private static BitmapSource Decode(ReadOnlyMemory<byte> bytes)
    {
        using var stream = new MemoryStream(bytes.ToArray());
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapSource frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private static void OnUi(Action action)
    {
        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
