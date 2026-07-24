using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using Octadock.App.Windows;
using Octadock.Core.Geometry;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The scrolling-capture session pill: a small, glassy, NO-ACTIVATE status
/// window shown beside the selected viewport while frames are stitched. It
/// gives the session the visibility it previously lacked — live stitched
/// height, an explicit Finish, and a Cancel — without ever taking keyboard
/// focus away from the window the user is scrolling.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed class ScrollingSessionPill : ToolWindowBase
{
    private const string FontResource = "Octadock.Font";
    private const string BodyFontSizeResource = "Octadock.FontSize.Body";
    private const string IconSizeResource = "Octadock.Icon.Size.16";
    private const string TextResource = "Octadock.Brush.Text";
    private const string AccentResource = "Octadock.Brush.Accent";

    private readonly TextBlock _status;

    /// <summary>Raised when the user clicks the finish (check) button.</summary>
    public event EventHandler? FinishRequested;

    /// <summary>Raised when the user clicks the cancel (cross) button.</summary>
    public event EventHandler? CancelRequested;

    public ScrollingSessionPill()
    {
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;

        _status = new TextBlock
        {
            Text = "Manual vertical scroll — scroll the selected area",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        _status.SetResourceReference(TextBlock.FontFamilyProperty, FontResource);
        _status.SetResourceReference(TextBlock.FontSizeProperty, BodyFontSizeResource);
        _status.SetResourceReference(TextBlock.ForegroundProperty, TextResource);

        var stateIcon = new PackIconLucide
        {
            Kind = PackIconLucideKind.ScrollText,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 8, 0),
        };
        stateIcon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        stateIcon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        stateIcon.SetResourceReference(PackIconLucide.ForegroundProperty, AccentResource);

        Button finish = MakeIconButton(
            PackIconLucideKind.Check,
            "Finish and stitch what you scrolled");
        finish.Click += (_, _) => FinishRequested?.Invoke(this, EventArgs.Empty);

        Button cancel = MakeIconButton(
            PackIconLucideKind.X,
            "Cancel the scrolling capture");
        cancel.Click += (_, _) => CancelRequested?.Invoke(this, EventArgs.Empty);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(stateIcon);
        row.Children.Add(_status);
        row.Children.Add(finish);
        row.Children.Add(cancel);

        var shell = new Border { Child = row };
        shell.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.StatusPill");
        AutomationProperties.SetName(shell, "Manual vertical scrolling capture status");
        Content = shell;
    }

    /// <inheritdoc />
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // The user must keep OS focus on the window they are scrolling; the
        // pill's buttons still work without activation.
        NativeMethods.MakeNoActivateToolWindow(Hwnd);
    }

    /// <summary>Shows the pill just outside the capture region (never inside it).</summary>
    public void ShowNear(PixelRect region, DisplayInfo monitor)
    {
        Show();
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
            int pillW = (int)Math.Ceiling(ActualWidth * scale);
            int pillH = (int)Math.Ceiling(ActualHeight * scale);

            int x = region.X + ((region.Width - pillW) / 2);
            x = Math.Clamp(x, monitor.WorkArea.X + 8, Math.Max(monitor.WorkArea.X + 8, monitor.WorkArea.Right - pillW - 8));

            // Above the region when there is room; otherwise below; never inside.
            int y = region.Y - pillH - 12;
            if (y < monitor.WorkArea.Y + 8)
            {
                y = Math.Min(region.Bottom + 12, monitor.WorkArea.Bottom - pillH - 8);
            }

            NativeMethods.MovePhysical(Hwnd, x, y);
        });
    }

    /// <summary>Updates the live status line.</summary>
    public void Update(int stitchedPixels, bool hasGrown)
    {
        _status.Text = hasGrown
            ? $"Manual vertical scroll — {stitchedPixels:N0} px stitched"
            : "Manual vertical scroll — scroll the selected area";
    }

    private static Button MakeIconButton(PackIconLucideKind kind, string tooltip)
    {
        var icon = new PackIconLucide { Kind = kind };
        icon.SetResourceReference(FrameworkElement.WidthProperty, IconSizeResource);
        icon.SetResourceReference(FrameworkElement.HeightProperty, IconSizeResource);
        icon.SetResourceReference(PackIconLucide.ForegroundProperty, TextResource);

        var button = new Button
        {
            Content = icon,
            ToolTip = tooltip,
            Width = 34,
            Height = 34,
            Margin = new Thickness(1, 0, 0, 0),
            Focusable = false,
        };
        button.SetResourceReference(FrameworkElement.StyleProperty, "Octadock.Style.HoverActionButton");
        AutomationProperties.SetName(button, tooltip);
        return button;
    }
}
