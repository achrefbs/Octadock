using System.Globalization;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Ai;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Hotkeys;
using Octadock.Core.Recording;
using CaptureMode = Octadock.Core.Commands.CaptureMode;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The compact all-in-one HUD: a floating, capture-excluded panel exposing Area,
/// Window, Fullscreen, Scrolling, OCR and Record, plus optional fixed-size / locked-
/// aspect fields. Buttons route through <see cref="ICaptureCoordinator"/>,
/// <see cref="ICommandDispatcher"/> and <see cref="IOcrService"/> (resolved from the
/// service provider to avoid a construction cycle). The HUD closes itself before every
/// capture so it never appears in the grab, and it remembers the last selection via
/// the shared <see cref="HudState"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class HudWindow : ToolWindowBase
{
    private readonly IServiceProvider _services;
    private readonly HudState _state;
    private bool _captureStarted;

    /// <summary>Creates the HUD bound to the shared HUD state.</summary>
    public HudWindow(IServiceProvider services, HudState state)
    {
        _services = services;
        _state = state;
        InitializeComponent();
        UpdateHotkeyChip();

        Loaded += OnLoaded;
        DragBar.MouseLeftButtonDown += OnDragBarMouseDown;
        KeyDown += OnKeyDown;
    }

    private void UpdateHotkeyChip()
    {
        HotkeyGesture gesture = _services.GetRequiredService<ISettingsService>().Current.Shortcuts.AllInOne;
        HotkeyChip.Visibility = gesture.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        HotkeyChipText.Text = gesture.ToString();
    }

    /// <summary>Applies a preselected mode by immediately triggering that capture.</summary>
    public void ApplyMode(CaptureMode? mode)
    {
        if (mode is null)
        {
            return;
        }

        // Defer so the window is shown/positioned before we launch the flow.
        Dispatcher.BeginInvoke(() => TriggerMode(mode.Value));
    }

    /// <summary>Applies automation-supplied coordinates to the shared HUD state and visible fields.</summary>
    public void ApplyPreload(PixelRect? region, int? width, int? height)
    {
        _state.ApplyPreload(region, width, height);
        RestoreStateToUi();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Note: the HUD is interactive (text fields, Escape), so it is NOT marked
        // WS_EX_NOACTIVATE — it needs to take keyboard focus.
        PositionOnActiveMonitor();
        RestoreStateToUi();
    }

    private void RestoreStateToUi()
    {
        FixedSizeCheck.IsChecked = _state.FixedSizeEnabled;
        LockAspectCheck.IsChecked = _state.LockAspectEnabled;
        if (_state.FixedWidth > 0)
        {
            WidthBox.Text = _state.FixedWidth.ToString(CultureInfo.InvariantCulture);
        }

        if (_state.FixedHeight > 0)
        {
            HeightBox.Text = _state.FixedHeight.ToString(CultureInfo.InvariantCulture);
        }

        AspectText.Text = _state.LockAspectEnabled && _state.LastRegion is not null
            ? _state.DescribeLast()
            : "Aspect";
    }

    private void CaptureStateFromUi()
    {
        _state.FixedSizeEnabled = FixedSizeCheck.IsChecked == true;
        _state.LockAspectEnabled = LockAspectCheck.IsChecked == true;
        _state.FixedWidth = ParseInt(WidthBox.Text);
        _state.FixedHeight = ParseInt(HeightBox.Text);
    }

    private void PositionOnActiveMonitor()
    {
        var monitors = _services.GetRequiredService<IMonitorService>();
        DisplayInfo active = monitors.GetActiveMonitor();

        // Center horizontally near the top of the active monitor's work area. The
        // window uses SizeToContent, so move only (WPF owns the size).
        double scale = active.DpiScale <= 0 ? 1.0 : active.DpiScale;
        int hudWidthPx = (int)Math.Round(ActualWidth * scale);
        int x = active.WorkArea.X + ((active.WorkArea.Width - hudWidthPx) / 2);
        int y = active.WorkArea.Y + (int)Math.Round(40 * scale);
        NativeMethods.MovePhysical(Hwnd, x, y);
    }

    private void OnDragBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch
            {
                // DragMove throws if the button was already released; ignore.
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnHandoffClick(object sender, RoutedEventArgs e)
    {
        Close();
        _services.GetRequiredService<IWindowPresenter>()
            .ShowAiActions(AgentReviewLaunch.FromHud());
    }

    private void OnAreaClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Area);

    private void OnWindowClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Window);

    private void OnFullscreenClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Fullscreen);

    private void OnScrollingClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Scrolling);

    private void OnOcrClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Ocr);

    private void OnRecordClick(object sender, RoutedEventArgs e) => TriggerMode(CaptureMode.Record);

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        CaptureStateFromUi();
        Close();
        _services.GetRequiredService<IWindowPresenter>().ShowSettings("capture");
    }

    private void OnDelayClick(object sender, RoutedEventArgs e)
    {
        if (_captureStarted)
        {
            return;
        }

        CaptureStateFromUi();
        PixelRect? region = null;
        if (_state.FixedSizeEnabled && _state.FixedWidth > 0 && _state.FixedHeight > 0)
        {
            PixelPoint origin = _state.LastRegion?.Location ?? ActiveMonitorOrigin();
            region = new PixelRect(origin.X, origin.Y, _state.FixedWidth, _state.FixedHeight);
            _state.LastRegion = region;
        }

        PostCaptureAction action = _services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;
        _captureStarted = true;
        Close();
        _ = RunDelayedCaptureAsync(action, region);
    }

    private async Task RunDelayedCaptureAsync(PostCaptureAction action, PixelRect? region)
    {
        try
        {
            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action.ToString().ToLowerInvariant(),
            };
            if (region is { } fixedRegion)
            {
                parameters["x"] = fixedRegion.X.ToString(CultureInfo.InvariantCulture);
                parameters["y"] = fixedRegion.Y.ToString(CultureInfo.InvariantCulture);
                parameters["width"] = fixedRegion.Width.ToString(CultureInfo.InvariantCulture);
                parameters["height"] = fixedRegion.Height.ToString(CultureInfo.InvariantCulture);
                parameters["units"] = "pixels";
            }

            OctadockCommand command = OctadockCommand.Create(CommandType.SelfTimer, parameters);
            CommandResult result = await _services.GetRequiredService<ICommandDispatcher>()
                .DispatchAsync(command).ConfigureAwait(true);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message ?? "The delayed capture could not start.");
            }
        }
        catch (Exception ex)
        {
            _services.GetService<INotificationService>()?.Notify(
                "Delayed capture failed", ex.Message, NotificationKind.Error);
        }
    }

    private void TriggerMode(CaptureMode mode)
    {
        if (_captureStarted)
        {
            return;
        }

        try
        {
            CaptureStateFromUi();
            PostCaptureAction action = _services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;
            _captureStarted = true;
            IsEnabled = false;

            // The HUD is single-shot per action. Close it before launching the async
            // capture flow so HudService never has a hidden window that can race a
            // replacement HUD.
            Close();
            _ = RunAsync(mode, action);
        }
        catch (Exception ex)
        {
            _captureStarted = false;
            IsEnabled = true;
            _services.GetService<INotificationService>()?.Notify(
                "Capture failed", ex.Message, NotificationKind.Error);
        }
    }

    private async Task RunAsync(CaptureMode mode, PostCaptureAction action)
    {
        try
        {
            switch (mode)
            {
                case CaptureMode.Area:
                    await CaptureAreaAsync(action).ConfigureAwait(true);
                    break;

                case CaptureMode.Window:
                    await Coordinator.CaptureWindowAsync(action).ConfigureAwait(true);
                    break;

                case CaptureMode.Fullscreen:
                    await Coordinator.CaptureFullscreenAsync(action, monitorToken: null, allMonitors: false).ConfigureAwait(true);
                    break;

                case CaptureMode.Ocr:
                    OcrTextMode ocrMode = _services.GetRequiredService<ISettingsService>().Current.Ocr.OutputMode;
                    await _services.GetRequiredService<IOcrService>()
                        .CaptureRegionTextAsync(ocrMode, language: null).ConfigureAwait(true);
                    break;

                case CaptureMode.Scrolling:
                    await Coordinator.CaptureScrollingAsync(action).ConfigureAwait(true);
                    break;

                case CaptureMode.Record:
                    await _services.GetRequiredService<Octadock.App.Services.RecordingController>()
                        .ToggleAsync(new RecordingStartRequest { PromptForRegion = true }).ConfigureAwait(true);
                    break;

                default:
                    await DispatchAsync(mode).ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            _services.GetService<INotificationService>()?.Notify(
                "Capture failed", ex.Message, NotificationKind.Error);
        }
    }

    private async Task CaptureAreaAsync(PostCaptureAction action)
    {
        // Fixed size: capture a rectangle of exactly W×H at the remembered origin (or
        // the active-monitor top-left) without prompting, via the dispatcher so the
        // full persistence pipeline runs.
        if (_state.FixedSizeEnabled && _state.FixedWidth > 0 && _state.FixedHeight > 0)
        {
            PixelPoint origin = _state.LastRegion?.Location ?? ActiveMonitorOrigin();
            var region = new PixelRect(origin.X, origin.Y, _state.FixedWidth, _state.FixedHeight);
            _state.LastRegion = region;

            var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["x"] = region.X.ToString(CultureInfo.InvariantCulture),
                ["y"] = region.Y.ToString(CultureInfo.InvariantCulture),
                ["width"] = region.Width.ToString(CultureInfo.InvariantCulture),
                ["height"] = region.Height.ToString(CultureInfo.InvariantCulture),
                ["units"] = "pixels",
                ["action"] = action.ToString().ToLowerInvariant(),
            };

            OctadockCommand command = OctadockCommand.Create(CommandType.CaptureArea, parameters);
            await _services.GetRequiredService<ICommandDispatcher>().DispatchAsync(command).ConfigureAwait(true);
            return;
        }

        // Interactive area selection: remember the resulting rectangle for next time.
        var selection = _services.GetService<IRegionSelectionService>();
        if (selection is not null)
        {
            RegionSelection result = await selection.SelectAreaAsync().ConfigureAwait(true);
            if (result.Confirmed && !result.Region.IsEmpty)
            {
                _state.LastRegion = result.Region;
                selection.HideAll();
                await Task.Delay(30).ConfigureAwait(true);
                var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["x"] = result.Region.X.ToString(CultureInfo.InvariantCulture),
                    ["y"] = result.Region.Y.ToString(CultureInfo.InvariantCulture),
                    ["width"] = result.Region.Width.ToString(CultureInfo.InvariantCulture),
                    ["height"] = result.Region.Height.ToString(CultureInfo.InvariantCulture),
                    ["units"] = "pixels",
                    ["action"] = action.ToString().ToLowerInvariant(),
                };
                OctadockCommand command = OctadockCommand.Create(CommandType.CaptureArea, parameters);
                await _services.GetRequiredService<ICommandDispatcher>().DispatchAsync(command).ConfigureAwait(true);
            }

            return;
        }

        // No selection service: fall back to the coordinator's own prompt/handling.
        await Coordinator.CaptureAreaAsync(action).ConfigureAwait(true);
    }

    private async Task DispatchAsync(CaptureMode mode)
    {
        CommandType type = mode switch
        {
            CaptureMode.Scrolling => CommandType.ScrollingCapture,
            CaptureMode.Record => CommandType.RecordScreen,
            _ => CommandType.AllInOne,
        };

        OctadockCommand command = OctadockCommand.Create(type);
        await _services.GetRequiredService<ICommandDispatcher>().DispatchAsync(command).ConfigureAwait(true);
    }

    private PixelPoint ActiveMonitorOrigin()
    {
        DisplayInfo active = _services.GetRequiredService<IMonitorService>().GetActiveMonitor();
        return active.WorkArea.Location;
    }

    private ICaptureCoordinator Coordinator => _services.GetRequiredService<ICaptureCoordinator>();

    private static int ParseInt(string? text)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) && v > 0 ? v : 0;
}
