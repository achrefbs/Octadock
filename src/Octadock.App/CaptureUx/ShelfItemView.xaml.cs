using System.Collections.Specialized;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// The Capture Shelf card view. Beyond binding to <see cref="ShelfItemViewModel"/>, it
/// implements drag-out: pressing and dragging the thumbnail past the system drag
/// threshold starts a <see cref="DragDrop.DoDragDrop"/> whose <see cref="DataObject"/>
/// carries BOTH the absolute PNG path (<see cref="DataFormats.FileDrop"/>) and a
/// <see cref="BitmapSource"/> (<see cref="DataFormats.Bitmap"/>). That dual payload lets
/// the same drag drop into Explorer (as a file), a browser upload field, Teams, Slack,
/// or an image editor (as a bitmap). The file is the persisted capture, so it stays
/// valid for the lifetime of the drop.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
public partial class ShelfItemView : UserControl
{
    private Point _pressOrigin;
    private bool _pressed;
    private bool _dragging;
    private bool _suppressClick;
    private ShelfPointerGesture _gesture;

    private const double SwipeReleaseCommitDistance = 54;
    private const double HorizontalDragOutDistance = 118;
    private const double SwipeVisualLimit = 86;

    /// <summary>Creates the shelf card view.</summary>
    public ShelfItemView()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyRoundedClip();
        SizeChanged += (_, _) => ApplyRoundedClip();
    }

    private ShelfItemViewModel? ViewModel => DataContext as ShelfItemViewModel;

    private void ApplyRoundedClip()
    {
        ApplyRoundedClip(TileRoot, 9);
        ApplyRoundedClip(ThumbHost, 9);
    }

    private static void ApplyRoundedClip(FrameworkElement element, double radius)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius);
    }

    private void OnThumbMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (IsInsideButton(e.OriginalSource as DependencyObject))
        {
            return;
        }

        TileRoot.Focus();
        ResetSwipe(animate: false);
        _pressOrigin = e.GetPosition(this);
        _pressed = true;
        _suppressClick = false;
        _gesture = ShelfPointerGesture.Pending;
        ThumbHost.CaptureMouse();
    }

    private void OnRowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && ViewModel is { } discardVm && discardVm.DiscardCommand.CanExecute(null))
        {
            discardVm.DiscardCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            ViewModel is { } copyVm && copyVm.CopyCommand.CanExecute(null))
        {
            copyVm.CopyCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Enter or Key.Space) || Keyboard.FocusedElement is ButtonBase)
        {
            return;
        }

        if (ViewModel is { } vm && vm.OpenCommand.CanExecute(null))
        {
            vm.OpenCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void OnMoreClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button)
        {
            return;
        }

        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OnThumbMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
        {
            return;
        }

        ShelfPointerGesture gesture = _gesture;
        double swipeOffset = SwipeTranslate.X;
        ReleaseThumbCapture();
        if (gesture == ShelfPointerGesture.Swipe)
        {
            _suppressClick = true;
            if (Math.Abs(swipeOffset) >= SwipeReleaseCommitDistance && ViewModel is { } swipeVm)
            {
                CompleteSwipe(swipeVm, swipeOffset);
            }
            else
            {
                ResetSwipe(animate: true);
            }

            e.Handled = true;
        }
        else if (!_suppressClick && ViewModel is { } vm)
        {
            if (vm.OpenCommand.CanExecute(null))
            {
                vm.OpenCommand.Execute(null);
                e.Handled = true;
            }
        }

        _pressed = false;
        _dragging = false;
        _suppressClick = false;
        _gesture = ShelfPointerGesture.None;
    }

    private void OnThumbMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragging || e.LeftButton != MouseButtonState.Pressed || ViewModel is null)
        {
            return;
        }

        Point current = e.GetPosition(this);
        double dx = current.X - _pressOrigin.X;
        double dy = current.Y - _pressOrigin.Y;
        if (_gesture == ShelfPointerGesture.Pending &&
            Math.Abs(dx) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(dy) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _suppressClick = true;
        if (_gesture == ShelfPointerGesture.Pending)
        {
            _gesture = Math.Abs(dx) > Math.Abs(dy) * 1.15
                ? ShelfPointerGesture.Swipe
                : ShelfPointerGesture.Drag;
        }

        if (_gesture == ShelfPointerGesture.Swipe)
        {
            UpdateSwipe(dx);

            // Continuing either direction far enough still becomes the existing
            // OS drag-out, so export workflows remain intact. Removing a card is
            // deliberately reserved for the explicit × / menu / Delete key.
            if (Math.Abs(dx) >= HorizontalDragOutDistance)
            {
                _gesture = ShelfPointerGesture.Drag;
                StartDrag(ViewModel);
                return;
            }

            e.Handled = true;
            return;
        }

        StartDrag(ViewModel);
    }

    private void OnThumbLostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_dragging || !_pressed)
        {
            return;
        }

        _pressed = false;
        _gesture = ShelfPointerGesture.None;
        ResetSwipe(animate: true);
    }

    private void UpdateSwipe(double rawOffset)
    {
        double offset = Math.Clamp(rawOffset, -SwipeVisualLimit, SwipeVisualLimit);
        ShelfSwipeAction action = ResolveSwipeAction(CurrentAnchor(), offset);
        double visualOffset = action == ShelfSwipeAction.Copy ? offset : offset * 0.16;
        SwipeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SwipeTranslate.X = visualOffset;

        SwipeActionIcon.Kind = PackIconLucideKind.Copy;
        SwipeActionIcon.HorizontalAlignment = offset >= 0
            ? HorizontalAlignment.Left
            : HorizontalAlignment.Right;
        SwipeActionBackground.SetResourceReference(Border.BackgroundProperty, "Octadock.Brush.AccentSoft");
        SwipeActionBackground.Opacity = action == ShelfSwipeAction.Copy
            ? Math.Clamp(Math.Abs(offset) / SwipeReleaseCommitDistance, 0, 1)
            : 0;
    }

    private void CompleteSwipe(ShelfItemViewModel vm, double offset)
    {
        ShelfSwipeAction action = ResolveSwipeAction(CurrentAnchor(), offset);
        if (action != ShelfSwipeAction.Copy)
        {
            ResetSwipe(animate: true);
            return;
        }

        if (vm.CopyCommand.CanExecute(null))
        {
            vm.CopyCommand.Execute(null);
        }

        ResetSwipe(animate: true);
    }

    private void ResetSwipe(bool animate)
    {
        SwipeActionBackground.BeginAnimation(OpacityProperty, null);
        if (animate && SystemParameters.ClientAreaAnimation && Math.Abs(SwipeTranslate.X) > 0.1)
        {
            var reset = new DoubleAnimation(
                SwipeTranslate.X,
                0,
                TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new BackEase
                {
                    Amplitude = 0.18,
                    EasingMode = EasingMode.EaseOut,
                },
            };
            SwipeTranslate.BeginAnimation(TranslateTransform.XProperty, reset);
            SwipeActionBackground.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(SwipeActionBackground.Opacity, 0, TimeSpan.FromMilliseconds(120)));
            return;
        }

        SwipeTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SwipeTranslate.X = 0;
        SwipeActionBackground.Opacity = 0;
    }

    private ShelfAnchor CurrentAnchor()
    {
        if (Window.GetWindow(this) is ShelfWindow shelfWindow)
        {
            return shelfWindow.EffectiveAnchor;
        }

        try
        {
            return App.Services.GetRequiredService<ISettingsService>().Current.Shelf.Anchor;
        }
        catch (Exception)
        {
            return ShelfAnchor.BottomLeft;
        }
    }

    internal static ShelfSwipeAction ResolveSwipeAction(ShelfAnchor anchor, double horizontalOffset)
    {
        bool shelfOnLeft = anchor is ShelfAnchor.BottomLeft or ShelfAnchor.TopLeft;
        bool towardScreenEdge = shelfOnLeft ? horizontalOffset < 0 : horizontalOffset > 0;
        return towardScreenEdge ? ShelfSwipeAction.None : ShelfSwipeAction.Copy;
    }

    private static bool IsInsideButton(DependencyObject? source)
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is ButtonBase)
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ReleaseThumbCapture()
    {
        if (ThumbHost.IsMouseCaptured)
        {
            ThumbHost.ReleaseMouseCapture();
        }
    }

    private void StartDrag(ShelfItemViewModel vm)
    {
        _dragging = true;
        ReleaseThumbCapture();
        ResetSwipe(animate: false);
        try
        {
            string path = vm.AbsoluteOriginalPath;
            var data = new DataObject();

            // Octadock targets can return this exact card without importing a
            // second managed file. Other applications safely ignore the format.
            ShelfDragPayload.SetCaptureId(data, vm.Record.Id);

            // File drop (Explorer, upload fields, Teams/Slack file attach).
            var files = new StringCollection { path };
            data.SetFileDropList(files);

            // Bitmap (image editors, chat inline images).
            BitmapSource? bitmap = vm.GetDragBitmap();
            if (bitmap is not null)
            {
                data.SetImage(bitmap);
            }

            // A plain path helps some targets that read text.
            data.SetData(DataFormats.Text, path);

            DragDropEffects effect = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy);
            if (effect != DragDropEffects.None)
            {
                vm.NotifyDraggedOut();
            }
        }
        catch (Exception)
        {
            // A failed drag must never crash the shelf; swallow and reset.
        }
        finally
        {
            _dragging = false;
            _pressed = false;
            _gesture = ShelfPointerGesture.None;
        }
    }

    private enum ShelfPointerGesture
    {
        None = 0,
        Pending,
        Swipe,
        Drag,
    }
}

internal enum ShelfSwipeAction
{
    None = 0,
    Copy,
}
