// Example implementation for the file-preview proposal.
// Target location: src/Octadock.App/Preview/ — the Quick Look card itself.
//
// The interesting part is the zoom-from-icon storyboard: the card is placed at
// its final centered position, then a ScaleTransform+TranslateTransform makes
// it *render* over the source rect at ~12% scale, and both animate to identity.
// Close plays the same board reversed. Pure WPF, no third-party libs.
//
// Companion XAML (PreviewCardWindow.xaml) sketch:
//
//   <w:ToolWindowBase x:Class="Octadock.App.Preview.PreviewCardWindow"
//                     xmlns:w="clr-namespace:Octadock.App.Windows"
//                     ShowActivated="True" Focusable="True">   <!-- diverges from base: card takes focus -->
//     <Border x:Name="CardRoot"
//             Background="{DynamicResource Octadock.Brush.Surface}"
//             CornerRadius="{StaticResource Octadock.Corner}"
//             RenderTransformOrigin="0.5,0.5">
//       <Border.RenderTransform>
//         <TransformGroup>
//           <ScaleTransform x:Name="CardScale" />
//           <TranslateTransform x:Name="CardTranslate" />
//         </TransformGroup>
//       </Border.RenderTransform>
//       <DockPanel>
//         <DockPanel DockPanel.Dock="Top"><!-- title, Copy / Export / Pin buttons --></DockPanel>
//         <ContentControl x:Name="BodyHost" /> <!-- CsvPreviewView / TextPreviewView by result kind -->
//       </DockPanel>
//     </Border>
//   </w:ToolWindowBase>

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Preview;

public partial class PreviewCardWindow : ToolWindowBase
{
    private static readonly Duration OpenDuration = new(TimeSpan.FromMilliseconds(340));
    private static readonly Duration CloseDuration = new(TimeSpan.FromMilliseconds(240));

    private PixelRect? _sourceRect;
    private PreviewOpenAnimation _animation;
    private bool _closingAnimated;

    public PreviewCardWindow()
    {
        InitializeComponent();

        // Unlike passive overlays, the card owns keyboard focus (Esc, filter box).
        ShowActivated = true;
        Focusable = true;

        Deactivated += (_, _) => DismissAnimated();          // click-away
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key is Key.Escape or Key.Space && Keyboard.FocusedElement is not System.Windows.Controls.TextBox)
            {
                DismissAnimated();
                e.Handled = true;
            }
        };
    }

    /// <summary>Shows (or re-targets) the card for a new preview result.</summary>
    public void Present(FilePreviewResult result, PixelRect? sourceRect, PreviewOpenAnimation animation)
    {
        _sourceRect = sourceRect;
        _animation = animation;

        BodyHost.Content = BuildBody(result);
        Title = System.IO.Path.GetFileName(result.FilePath);

        SizeToOwningMonitor();
        if (!IsVisible)
        {
            Show();
            Activate();
            PlayOpenAnimation();
        }
    }

    private object BuildBody(FilePreviewResult result) => result.Kind switch
    {
        FilePreviewKind.Csv => new CsvPreviewView { Model = result.Csv! },     // virtualized DataGrid + stats bar
        FilePreviewKind.PlainText => new TextPreviewView { Text = result.Text! },
        _ => new ErrorPreviewView { Message = result.Error ?? "Preview failed." },
    };

    /// <summary>Centers the card on the monitor that owns the source rect (or the active one).</summary>
    private void SizeToOwningMonitor()
    {
        DisplayInfo monitor = GetOwningMonitor(); // From ToolWindowBase.
        DipRect work = monitor.WorkAreaDips;

        Width = Math.Min(900, work.Width * 0.72);
        Height = Math.Min(640, work.Height * 0.78);
        Left = work.X + ((work.Width - Width) / 2);
        Top = work.Y + ((work.Height - Height) / 2);
    }

    // ---- Animation ---------------------------------------------------------

    private void PlayOpenAnimation()
    {
        (double fromScaleX, double fromScaleY, double fromX, double fromY, Point origin) = _animation switch
        {
            PreviewOpenAnimation.QuickLookZoom => ZoomFrom(_sourceRect!.Value, stretch: false),
            PreviewOpenAnimation.Genie => ZoomFrom(_sourceRect!.Value, stretch: true),
            PreviewOpenAnimation.SlideUp => (1d, 1d, 0d, 46d, new Point(0.5, 0.5)),
            _ /* Spotlight */ => (0.96d, 0.96d, 0d, 0d, new Point(0.5, 0.5)),
        };

        CardRoot.RenderTransformOrigin = origin;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        Animate(CardScale, ScaleTransform.ScaleXProperty, fromScaleX, 1, OpenDuration, ease);
        Animate(CardScale, ScaleTransform.ScaleYProperty, fromScaleY, 1, OpenDuration, ease);
        Animate(CardTranslate, TranslateTransform.XProperty, fromX, 0, OpenDuration, ease);
        Animate(CardTranslate, TranslateTransform.YProperty, fromY, 0, OpenDuration, ease);
        Animate(CardRoot, OpacityProperty, 0, 1, OpenDuration, ease);
    }

    /// <summary>
    /// Computes the transform that makes the (already centered) card render over
    /// the physical source rect: scale = sourceSize / cardSize, translate =
    /// sourceCenter − cardCenter, both in DIPs of the card's monitor.
    /// </summary>
    private (double Sx, double Sy, double Tx, double Ty, Point Origin) ZoomFrom(PixelRect source, bool stretch)
    {
        DisplayInfo monitor = GetOwningMonitor();
        double dpi = monitor.DpiScale;

        double srcW = source.Width / dpi;
        double srcH = source.Height / dpi;
        double srcCx = (source.X / dpi) + (srcW / 2);
        double srcCy = (source.Y / dpi) + (srcH / 2);

        double scale = Math.Max(0.06, Math.Min(srcW / Width, srcH / Height));
        double sy = stretch ? scale * 1.25 : scale; // Genie: slight vertical pull.

        double tx = srcCx - (Left + (Width / 2));
        double ty = srcCy - (Top + (Height / 2));
        Point origin = stretch ? new Point(0.5, 1.0) : new Point(0.5, 0.5);
        return (scale, sy, tx, ty, origin);
    }

    private void DismissAnimated()
    {
        if (_closingAnimated)
        {
            return;
        }

        _closingAnimated = true;
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        // Reverse of open: shrink back toward the source rect (or fade down).
        (double toSx, double toSy, double toX, double toY, Point origin) = _animation switch
        {
            PreviewOpenAnimation.QuickLookZoom => ZoomFrom(_sourceRect!.Value, stretch: false),
            PreviewOpenAnimation.Genie => ZoomFrom(_sourceRect!.Value, stretch: true),
            PreviewOpenAnimation.SlideUp => (1d, 1d, 0d, 30d, new Point(0.5, 0.5)),
            _ => (0.96d, 0.96d, 0d, 0d, new Point(0.5, 0.5)),
        };

        CardRoot.RenderTransformOrigin = origin;
        var fade = new DoubleAnimation(1, 0, CloseDuration) { EasingFunction = ease };
        fade.Completed += (_, _) => Close();

        Animate(CardScale, ScaleTransform.ScaleXProperty, 1, toSx, CloseDuration, ease);
        Animate(CardScale, ScaleTransform.ScaleYProperty, 1, toSy, CloseDuration, ease);
        Animate(CardTranslate, TranslateTransform.XProperty, 0, toX, CloseDuration, ease);
        Animate(CardTranslate, TranslateTransform.YProperty, 0, toY, CloseDuration, ease);
        CardRoot.BeginAnimation(OpacityProperty, fade);
    }

    private static void Animate(
        IAnimatable target, DependencyProperty property,
        double from, double to, Duration duration, IEasingFunction ease)
        => target.BeginAnimation(property, new DoubleAnimation(from, to, duration) { EasingFunction = ease });
}
