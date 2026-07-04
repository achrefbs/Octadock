using System.Collections.Specialized;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

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

    /// <summary>Creates the shelf card view.</summary>
    public ShelfItemView()
    {
        InitializeComponent();
    }

    private ShelfItemViewModel? ViewModel => DataContext as ShelfItemViewModel;

    private void OnThumbMouseDown(object sender, MouseButtonEventArgs e)
    {
        _pressOrigin = e.GetPosition(this);
        _pressed = true;
    }

    private void OnThumbMouseUp(object sender, MouseButtonEventArgs e)
    {
        _pressed = false;
        _dragging = false;
    }

    private void OnThumbMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pressed || _dragging || e.LeftButton != MouseButtonState.Pressed || ViewModel is null)
        {
            return;
        }

        Point current = e.GetPosition(this);
        if (Math.Abs(current.X - _pressOrigin.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(current.Y - _pressOrigin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        StartDrag(ViewModel);
    }

    private void StartDrag(ShelfItemViewModel vm)
    {
        _dragging = true;
        try
        {
            string path = vm.AbsoluteOriginalPath;
            var data = new DataObject();

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
        }
    }
}
