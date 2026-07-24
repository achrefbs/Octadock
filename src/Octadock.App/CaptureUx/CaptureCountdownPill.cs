using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using MahApps.Metro.IconPacks;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// A tiny capture-excluded countdown shown after a self-timer region is selected and
/// before the actual screenshot is taken.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class CaptureCountdownPill : ToolWindowBase
{
    private const string FontResource = "Octadock.Font";
    private const string DisplayFontSizeResource = "Octadock.FontSize.Display";
    private const string CaptionFontSizeResource = "Octadock.FontSize.Caption";
    private const string IconSizeResource = "Octadock.Icon.Size.20";
    private const string TextResource = "Octadock.Brush.Text";
    private const string MutedTextResource = "Octadock.Brush.TextMuted";
    private const string AccentResource = "Octadock.Brush.Accent";

    private readonly IMonitorService _monitors;
    private readonly TextBlock _number;
    private readonly TextBlock _label;
    private bool _closed;

    public CaptureCountdownPill(IMonitorService monitors)
    {
        _monitors = monitors ?? throw new ArgumentNullException(nameof(monitors));
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        var icon = new PackIconLucide
        {
            Kind = PackIconLucideKind.Timer,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 10, 0),
        };
        icon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        icon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, AccentResource);

        _number = new TextBlock
        {
            Text = "3",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right,
            MinWidth = 22,
            Margin = new Thickness(0, 0, 6, 0),
        };
        _number.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _number.SetResourceReference(TextBlock.FontSizeProperty, DisplayFontSizeResource);
        _number.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        _label = new TextBlock
        {
            Text = "seconds",
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _label.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _label.SetResourceReference(TextBlock.FontSizeProperty, CaptionFontSizeResource);
        _label.SetResourceReference(TextBlock.ForegroundProperty, MutedTextResource);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(icon);
        row.Children.Add(_number);
        row.Children.Add(_label);

        var shell = new Border
        {
            MinWidth = 132,
            Child = row,
        };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        AutomationProperties.SetName(shell, "Capture countdown");
        Content = shell;

        SizeChanged += (_, _) => Reposition();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }

    public void ShowOnActiveMonitor()
    {
        _closed = false;
        Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, Reposition);
    }

    public void Update(int remainingSeconds)
    {
        int remaining = Math.Max(0, remainingSeconds);
        _number.Text = remaining.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _label.Text = remaining == 1 ? "second" : "seconds";
        Reposition();
    }

    private void Reposition()
    {
        if (_closed || Hwnd == IntPtr.Zero)
        {
            return;
        }

        DisplayInfo monitor = _monitors.GetActiveMonitor();
        double scale = monitor.DpiScale <= 0 ? 1.0 : monitor.DpiScale;
        int widthPx = Math.Max(1, (int)Math.Ceiling(ActualWidth * scale));
        int heightPx = Math.Max(1, (int)Math.Ceiling(ActualHeight * scale));
        PixelRect work = monitor.WorkArea.IsEmpty ? monitor.Bounds : monitor.WorkArea;
        int x = work.X + ((work.Width - widthPx) / 2);
        int y = work.Y + (int)Math.Round(work.Height * 0.22) - (heightPx / 2);
        NativeMethods.MovePhysical(Hwnd, x, y);
    }
}
