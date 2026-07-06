using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.Services;
using Octadock.App.Tray;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The permanent Octadock dock: a small glass capsule that lives at the bottom
/// center of the primary monitor. Idle it is a breathing teal dot with the
/// wordmark; on hover it expands into the capture actions (area, window,
/// fullscreen, scrolling, OCR, record, history) plus developer utilities such
/// as dictation and active AI sessions. Real acrylic blur where the OS allows
/// it, translucent glass otherwise. Draggable; never takes keyboard focus;
/// capture-excluded.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class DockPill : ToolWindowBase
{
    private static readonly SolidColorBrush GlassBorder = new(Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
    private static readonly SolidColorBrush TextBrush = new(Color.FromArgb(0xFF, 0xF1, 0xF5, 0xF9));
    private static readonly SolidColorBrush AccentBrush = new(Color.FromArgb(0xFF, 0x2D, 0xD4, 0xBF));
    private static readonly SolidColorBrush RecordBrush = new(Color.FromArgb(0xFF, 0xF8, 0x71, 0x71));

    private readonly System.Windows.Shapes.Ellipse _logo;
    private readonly TextBlock _wordmark;
    private readonly TextBlock _licenseBadge;
    private readonly StackPanel _actions;
    private readonly Border _root;
    private readonly System.Windows.Threading.DispatcherTimer _collapseTimer;
    private readonly System.Windows.Threading.DispatcherTimer _followTimer;
    private System.Windows.Threading.DispatcherTimer? _licenseTimer;
    private Octadock.Core.Licensing.ILicenseGate? _licenseGate;
    private EventHandler<Octadock.Core.Licensing.LicenseState>? _licenseRefusedHandler;
    private PixelPoint _anchorCenter;
    private MonitorId _currentMonitor = MonitorId.Unknown;
    private bool _dragging;
    private bool _didDrag;
    private System.Windows.Point _dragStart;
    private PixelPoint _anchorAtDragStart;

    public DockPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _logo = new System.Windows.Shapes.Ellipse
        {
            Width = 11,
            Height = 11,
            Fill = AccentBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 6, 0),
        };

        _wordmark = new TextBlock
        {
            Text = "Octadock",
            Foreground = TextBrush,
            FontSize = 12,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
        };

        // Ambient trial/license badge (WS5, R31). Collapsed during an early trial or a
        // valid license; appears only as the trial nears its end / has ended / is revoked.
        _licenseBadge = new TextBlock
        {
            Foreground = AccentBrush,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0),
            Visibility = Visibility.Collapsed,
        };

        _actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Visibility = Visibility.Collapsed,
        };
        BuildActions();

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(_logo);
        row.Children.Add(_wordmark);
        row.Children.Add(_licenseBadge);
        row.Children.Add(_actions);

        _root = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xB8, 0x0C, 0x12, 0x20)),
            BorderBrush = GlassBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(19),
            Padding = new Thickness(12, 7, 10, 7),
            Child = row,
        };
        Content = _root;

        MouseEnter += (_, _) => Expand();
        MouseLeave += (_, _) => _collapseTimer!.Start();
        _collapseTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(420),
        };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver)
            {
                Collapse();
            }
        };

        // Follow the cursor's monitor (Wispr Flow-style): if the
        // cursor has moved to another monitor and the dock is idle, hop to that
        // monitor's bottom-center. Always on.
        _followTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(150),
        };
        _followTimer.Tick += (_, _) => FollowActiveMonitor();

        // Drag anywhere on the capsule chrome (not the buttons).
        MouseLeftButtonDown += (_, e) =>
        {
            _dragging = true;
            _didDrag = false;
            _dragStart = e.GetPosition(this);
            _anchorAtDragStart = _anchorCenter;
            CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (_dragging && e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                System.Windows.Point now = e.GetPosition(this);
                int dx = (int)Math.Round((now.X - _dragStart.X) * scale);
                int dy = (int)Math.Round((now.Y - _dragStart.Y) * scale);
                if (dx != 0 || dy != 0)
                {
                    _didDrag = true;
                }

                _anchorCenter = new PixelPoint(_anchorAtDragStart.X + dx, _anchorAtDragStart.Y + dy);
                Reanchor();
            }
        };
        MouseLeftButtonUp += (_, _) =>
        {
            bool moved = _dragging && _didDrag;
            _dragging = false;
            _didDrag = false;
            ReleaseMouseCapture();

            // Persist the new position only after a real drag so a plain click
            // never overwrites the saved anchor.
            if (moved)
            {
                PersistAnchor(_anchorCenter);
            }
        };
        SizeChanged += (_, _) => Reanchor();

        WireLicenseBadge();
    }

    private void WireLicenseBadge()
    {
        try
        {
            _licenseGate = App.Services.GetService(typeof(Octadock.Core.Licensing.ILicenseGate))
                as Octadock.Core.Licensing.ILicenseGate;
            if (_licenseGate is null)
            {
                return;
            }

            RefreshLicenseBadge();
            _licenseRefusedHandler = (_, _) => Dispatcher.BeginInvoke(RefreshLicenseBadge);
            _licenseGate.Refused += _licenseRefusedHandler;

            _licenseTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(1) };
            _licenseTimer.Tick += (_, _) => RefreshLicenseBadge();
            _licenseTimer.Start();
        }
        catch (Exception)
        {
            // Ambient chrome only: a licensing hiccup must never break the dock.
        }
    }

    private void RefreshLicenseBadge()
    {
        if (_licenseGate is null)
        {
            return;
        }

        try
        {
            SetLicenseStatus(_licenseGate.State, DateTimeOffset.UtcNow);
        }
        catch (Exception)
        {
            // best effort
        }
    }

    /// <summary>Shows the trial/license badge only as the trial nears its end / has ended / is revoked.</summary>
    public void SetLicenseStatus(Octadock.Core.Licensing.LicenseState state, DateTimeOffset nowUtc)
    {
        (string? text, Brush tint, string tip) = DescribeBadge(state, nowUtc);
        if (text is null)
        {
            _licenseBadge.Visibility = Visibility.Collapsed;
            _licenseBadge.ToolTip = null;
            return;
        }

        _licenseBadge.Text = text;
        _licenseBadge.Foreground = tint;
        _licenseBadge.ToolTip = tip;
        _licenseBadge.Visibility = Visibility.Visible;
    }

    private static (string? Text, Brush Tint, string Tip) DescribeBadge(
        Octadock.Core.Licensing.LicenseState state, DateTimeOffset nowUtc)
    {
        switch (state.Mode)
        {
            case Octadock.Core.Licensing.LicenseMode.Trial:
                int days = Octadock.Core.Licensing.LicenseStatusFormatter.DaysLeft(state.TrialEndsUtc, nowUtc);
                return days <= 7
                    ? ($"Trial {days}d", AccentBrush, $"Trial — {days} day(s) left. Enter a license key in Settings → Account.")
                    : (null, AccentBrush, string.Empty);
            case Octadock.Core.Licensing.LicenseMode.TrialExpired:
                return ("Trial ended", RecordBrush, "Your trial ended — enter a license key in Settings → Account.");
            case Octadock.Core.Licensing.LicenseMode.TrialFrozen:
                return ("Clock?", RecordBrush, "Trial paused — this PC's clock looks wrong.");
            case Octadock.Core.Licensing.LicenseMode.Revoked:
                return ("Revoked", RecordBrush, "License revoked — enter a valid key in Settings → Account.");
            default:
                return (null, AccentBrush, string.Empty);
        }
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.MakeNoActivateToolWindow(Hwnd);

        // NOTE: real acrylic blur (Acrylic.TryEnable) is deliberately NOT used
        // here. WPF AllowsTransparency windows are layered, and the accent
        // composition path renders layered content near-invisible on some
        // systems — the capsule became a ghost. The solid translucent glass
        // below matches the recording/scrolling pills and always composites.
        // Revisit acrylic with a non-layered host window in the dock v2 pass.
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        _collapseTimer.Stop();
        _followTimer.Stop();
        _licenseTimer?.Stop();
        if (_licenseGate is not null && _licenseRefusedHandler is not null)
        {
            _licenseGate.Refused -= _licenseRefusedHandler;
        }

        _logo.BeginAnimation(OpacityProperty, null);
        _dragging = false;
        _didDrag = false;
        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        base.OnClosed(e);
    }

    /// <summary>
    /// Shows the dock. A position the user previously dragged to is restored when
    /// it still lands on a connected monitor; otherwise the dock sits bottom-center
    /// on the given monitor's work area.
    /// </summary>
    public void ShowOn(DisplayInfo monitor)
    {
        _currentMonitor = monitor.Id;
        _anchorCenter = ResolveSavedAnchor()
            ?? new PixelPoint(
                monitor.WorkArea.X + (monitor.WorkArea.Width / 2),
                monitor.WorkArea.Bottom - 34);
        Show();
        StartBreathing();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reanchor);
        _followTimer.Start();
    }

    /// <summary>
    /// Returns the persisted anchor center when the user has one saved and it still
    /// lies within a connected monitor's bounds; otherwise null so the caller falls
    /// back to the default placement (a monitor may have been detached since).
    /// </summary>
    private static PixelPoint? ResolveSavedAnchor()
    {
        try
        {
            DockSettings dock = App.Services.GetRequiredService<ISettingsService>().Current.Dock;
            if (!dock.HasCustomAnchor)
            {
                return null;
            }

            var anchor = new PixelPoint(dock.AnchorX, dock.AnchorY);
            IReadOnlyList<DisplayInfo> monitors = App.Services.GetRequiredService<IMonitorService>().GetMonitors();
            foreach (DisplayInfo m in monitors)
            {
                if (m.Bounds.Contains(anchor))
                {
                    return anchor;
                }
            }

            return null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Fire-and-forget persistence of the dragged anchor; never throws to the caller.</summary>
    private static void PersistAnchor(PixelPoint anchor)
    {
        try
        {
            ISettingsService settings = App.Services.GetRequiredService<ISettingsService>();
            _ = settings.UpdateAsync(s => s with
            {
                Dock = s.Dock with
                {
                    HasCustomAnchor = true,
                    AnchorX = anchor.X,
                    AnchorY = anchor.Y,
                },
            });
        }
        catch (Exception)
        {
            // Persisting the dock position is best-effort; a failure must not
            // disrupt dragging.
        }
    }

    /// <summary>Dims the dock while the recording pill owns the screen.</summary>
    public void SetRecording(bool recording)
    {
        _logo.Fill = recording ? RecordBrush : AccentBrush;
        Opacity = recording ? 0.45 : 1.0;
        if (recording)
        {
            Collapse();
        }
    }

    private void Expand()
    {
        _collapseTimer.Stop();
        if (_actions.Visibility == Visibility.Visible)
        {
            return;
        }

        _wordmark.Visibility = Visibility.Collapsed;
        _actions.Visibility = Visibility.Visible;
        _actions.Opacity = 0;
        _actions.RenderTransform = new TranslateTransform(10, 0);
        _actions.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        ((TranslateTransform)_actions.RenderTransform).BeginAnimation(
            TranslateTransform.XProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
    }

    private void Collapse()
    {
        _actions.Visibility = Visibility.Collapsed;
        _wordmark.Visibility = Visibility.Visible;
    }

    /// <summary>Keeps the capsule centered on its anchor as its size changes.</summary>
    private void Reanchor()
    {
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int w = (int)Math.Ceiling(ActualWidth * scale);
        int h = (int)Math.Ceiling(ActualHeight * scale);
        NativeMethods.MovePhysical(Hwnd, _anchorCenter.X - (w / 2), _anchorCenter.Y - (h / 2));
    }

    /// <summary>
    /// Wispr Flow-style monitor following: if the cursor's monitor differs from
    /// the dock's and the dock is idle (not mid-drag, not expanded, not hovered),
    /// hop to that monitor's bottom-center. Following always overrides a saved
    /// custom position; the placement is deliberately just bottom-center.
    /// </summary>
    private void FollowActiveMonitor()
    {
        // Never move while the user is interacting with the dock.
        if (_dragging || IsMouseOver || _actions.Visibility == Visibility.Visible)
        {
            return;
        }

        try
        {
            DisplayInfo active = App.Services.GetRequiredService<IMonitorService>().GetActiveMonitor();
            if (active.Id == _currentMonitor)
            {
                return;
            }

            _currentMonitor = active.Id;
            _anchorCenter = new PixelPoint(
                active.WorkArea.X + (active.WorkArea.Width / 2),
                active.WorkArea.Bottom - 34);
            Reanchor();
        }
        catch (Exception)
        {
            // Monitor enumeration is best-effort; a failure must not stop the timer
            // from trying again next tick.
        }
    }

    private void StartBreathing()
    {
        var breathe = new DoubleAnimation(1.0, 0.45, TimeSpan.FromMilliseconds(1300))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        };
        _logo.BeginAnimation(OpacityProperty, breathe);
    }

    /// <summary>
    /// Command Deck grouping: capture family · record · text &amp; voice ·
    /// library · settings, separated so each cluster reads as one intent.
    /// </summary>
    private void BuildActions()
    {
        // Capture.
        AddAction("", "Capture area", () => Coordinator.CaptureAreaAsync(DefaultAction()), guardPaused: true);
        AddAction("", "Capture window", () => Coordinator.CaptureWindowAsync(DefaultAction()), guardPaused: true);
        AddAction("", "Capture fullscreen", () => Coordinator.CaptureFullscreenAsync(DefaultAction(), null, false), guardPaused: true);
        AddAction("", "Scrolling capture", () => Coordinator.CaptureScrollingAsync(DefaultAction()), guardPaused: true);
        AddSeparator();

        // Record.
        AddAction("", "Record the screen", () =>
        {
            RecordingController recorder = App.Services.GetRequiredService<RecordingController>();
            return !recorder.IsRecording && GuardPaused()
                ? Task.CompletedTask
                : recorder.ToggleAsync();
        }, RecordBrush);
        AddSeparator();

        // Text & voice.
        AddTextAction("OCR", "Grab text from a region", () =>
        {
            var settings = App.Services.GetRequiredService<ISettingsService>();
            return App.Services.GetRequiredService<IOcrService>()
                .CaptureRegionTextAsync(settings.Current.Ocr.OutputMode, null);
        }, guardPaused: true);
        AddAction("", "Read a region aloud — local voice, verbatim", () =>
            App.Services.GetRequiredService<ICommandDispatcher>()
                .DispatchAsync(OctadockCommand.Create(CommandType.ReadAloud)), guardPaused: true);
        AddAction("", "Dictate — local Parakeet engine (one-time model download, then on-device)", () =>
            App.Services.GetRequiredService<DictationController>().ToggleAsync(), AccentBrush);
        AddSeparator();

        // Library.
        AddAction("", "Open history", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowHistory();
            return Task.CompletedTask;
        });
        AddAction("", "Clipboard history — search and restore recent copies", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowClipboardHistory();
            return Task.CompletedTask;
        });
        AddAction("", "Open a file as a preview (CSV, code, text, images, and more)", OpenFileForPreviewAsync);
        AddSeparator();

        AddAction("", "Settings", () =>
        {
            App.Services.GetRequiredService<IWindowPresenter>().ShowSettings();
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Shared open-a-file flow used by both the "File" capsule button and the
    /// right-click "Open a file…" menu item: pick a file, then preview it.
    /// </summary>
    private static async Task OpenFileForPreviewAsync()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Open a file in Octadock",
            Filter =
                "Previewable files|*.csv;*.tsv;*.txt;*.log;*.md;*.json;*.xml;*.yaml;*.yml;*.toml;*.ini;*.cfg;*.cs;*.js;*.ts;*.jsx;*.tsx;*.py;*.rb;*.go;*.rs;*.java;*.c;*.cpp;*.h;*.css;*.html;*.htm;*.sql;*.sh;*.ps1;*.bat;*.csproj;*.sln;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp;*.ico" +
                "|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() == true)
        {
            await App.Services.GetRequiredService<Octadock.App.Preview.FilePreviewService>()
                .PreviewAsync(dialog.FileName).ConfigureAwait(true);
        }
    }

    private static ICaptureCoordinator Coordinator => App.Services.GetRequiredService<ICaptureCoordinator>();

    private static PostCaptureAction DefaultAction()
        => App.Services.GetRequiredService<ISettingsService>().Current.Capture.DefaultAction;

    private void AddSeparator()
        => _actions.Children.Add(new Border
        {
            Width = 1,
            Height = 18,
            Background = new SolidColorBrush(Color.FromArgb(0x2E, 0xFF, 0xFF, 0xFF)),
            Margin = new Thickness(5, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });

    private void AddAction(string glyph, string tooltip, Func<Task> action, Brush? tint = null, bool guardPaused = false)
    {
        Button button = MakeButton(new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = tint ?? TextBrush,
        }, tooltip);
        button.Click += async (_, _) =>
        {
            Collapse();
            if (guardPaused && GuardPaused())
            {
                return;
            }

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Coordinator paths surface their own notifications.
            }
        };
        _actions.Children.Add(button);
    }

    private void AddTextAction(string label, string tooltip, Func<Task> action, bool guardPaused = false)
    {
        Button button = MakeButton(new TextBlock
        {
            Text = label,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextBrush,
        }, tooltip);
        button.Click += async (_, _) =>
        {
            Collapse();
            if (guardPaused && GuardPaused())
            {
                return;
            }

            try
            {
                await action().ConfigureAwait(true);
            }
            catch (Exception)
            {
            }
        };
        _actions.Children.Add(button);
    }

    private static bool GuardPaused()
    {
        try
        {
            TrayIconController? tray = App.Services.GetService<TrayIconController>();
            if (tray?.IsPaused != true)
            {
                return false;
            }

            App.Services.GetService<INotificationService>()?.Notify(
                "Octadock is paused",
                "Resume capture from the tray menu.",
                NotificationKind.Info);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static Button MakeButton(UIElement content, string tooltip)
    {
        var button = new Button
        {
            Content = content,
            ToolTip = tooltip,
            Width = 32,
            Height = 28,
            Margin = new Thickness(1, 0, 1, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false,
        };

        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
        border.Name = "Bd";
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)),
            "Bd"));
        template.Triggers.Add(hover);
        button.Template = template;
        return button;
    }

    /// <summary>
    /// Undocumented-but-ubiquitous acrylic blur (the same composition API the
    /// OS shell and PowerToys use). Fails soft: callers keep their translucent
    /// fallback brush when the call is rejected.
    /// </summary>
    private static class Acrylic
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct AccentPolicy
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CompositionData
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref CompositionData data);

        public static bool TryEnable(IntPtr hwnd, uint tintAbgr)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                var accent = new AccentPolicy
                {
                    AccentState = 4, // ACCENT_ENABLE_ACRYLICBLURBEHIND
                    GradientColor = tintAbgr,
                };

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
                try
                {
                    Marshal.StructureToPtr(accent, buffer, false);
                    var data = new CompositionData
                    {
                        Attribute = 19, // WCA_ACCENT_POLICY
                        Data = buffer,
                        SizeOfData = Marshal.SizeOf<AccentPolicy>(),
                    };

                    return SetWindowCompositionAttribute(hwnd, ref data) != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
