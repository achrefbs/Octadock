using System.Globalization;
using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Octadock.App.Pins;

/// <summary>
/// View model for a single floating image. Holds the displayed image plus the
/// interactive state (opacity, pinned-on-top state) and exposes the toolbar
/// commands. The window supplies the actual copy/save/annotate/close behavior via
/// the <see cref="PinActions"/> callbacks so the view model stays UI-free.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PinViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NaturalWidth))]
    [NotifyPropertyChangedFor(nameof(NaturalHeight))]
    [NotifyPropertyChangedFor(nameof(DimensionsLabel))]
    private BitmapSource _image;

    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PinLabel))]
    [NotifyPropertyChangedFor(nameof(PinStatusLabel))]
    private bool _isLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PenLabel))]
    private bool _isPenActive;

    /// <summary>Creates a pin view model over the given image.</summary>
    public PinViewModel(BitmapSource image, PinActions actions)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The image's native pixel size (used for the initial window size).</summary>
    public double NaturalWidth => Image.PixelWidth;

    /// <summary>The image's native pixel size (used for the initial window size).</summary>
    public double NaturalHeight => Image.PixelHeight;

    /// <summary>Title shown in the image window's glass top bar.</summary>
    public string Title { get; init; } = "Image";

    /// <summary>"W × H" pixel dimensions, shown in the top bar and metadata strip.</summary>
    public string DimensionsLabel => $"{Image.PixelWidth} × {Image.PixelHeight}";

    /// <summary>The window-level operations this pin delegates to.</summary>
    public PinActions Actions { get; }

    /// <summary>The pin/unpin toggle tooltip.</summary>
    public string PinLabel => IsLocked
        ? "Unpin from top"
        : "Pin on top";

    /// <summary>The footer status text.</summary>
    public string PinStatusLabel => IsLocked ? "Pinned" : "Not pinned";

    /// <summary>The pen toggle tooltip.</summary>
    public string PenLabel => IsPenActive ? "Turn pen off" : "Pen";

    [RelayCommand]
    private async Task CopyAsync() => await Actions.CopyAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsync() => await Actions.SaveAsync().ConfigureAwait(true);

    [RelayCommand]
    private void Annotate() => IsPenActive = !IsPenActive;

    [RelayCommand]
    private void ClearInk() => Actions.ClearInk();

    [RelayCommand]
    private async Task AddToContextAsync() => await Actions.AddToContextAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task OpenSourceAsync() => await Actions.OpenSourceAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task RevealSourceAsync() => await Actions.RevealSourceAsync().ConfigureAwait(true);

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
        Actions.LockChanged(IsLocked);
    }

    [RelayCommand]
    private void SetOpacity(string value)
    {
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double opacity))
        {
            Opacity = Math.Clamp(opacity, 0.2, 1.0);
        }
    }

    [RelayCommand]
    private void Close() => Actions.Close();

    partial void OnOpacityChanged(double value) => Actions.OpacityChanged(value);

    partial void OnIsPenActiveChanged(bool value) => Actions.PenChanged(value);
}

/// <summary>
/// Window-level callbacks a <see cref="PinViewModel"/> invokes. Implemented by the
/// <c>PinWindow</c> code-behind (clipboard, file dialog, annotation hand-off,
/// close, and persistence of opacity / lock changes).
/// </summary>
public abstract class PinActions
{
    /// <summary>Copies the pinned image to the clipboard.</summary>
    public abstract Task CopyAsync();

    /// <summary>Prompts to save the pinned image to a file.</summary>
    public abstract Task SaveAsync();

    /// <summary>Clears quick pen marks drawn on the pinned image.</summary>
    public abstract void ClearInk();

    /// <summary>Adds the current image to Context.</summary>
    public abstract Task AddToContextAsync();

    /// <summary>Opens the source image file, when there is one.</summary>
    public abstract Task OpenSourceAsync();

    /// <summary>Shows the source image in File Explorer, when there is one.</summary>
    public abstract Task RevealSourceAsync();

    /// <summary>Closes (and forgets) the pin.</summary>
    public abstract void Close();

    /// <summary>Notifies that pinned-on-top state was toggled, so the window applies it and persists.</summary>
    public abstract void LockChanged(bool locked);

    /// <summary>Notifies that opacity changed, so the window persists it.</summary>
    public abstract void OpacityChanged(double opacity);

    /// <summary>Notifies that quick pen mode changed.</summary>
    public abstract void PenChanged(bool enabled);
}
