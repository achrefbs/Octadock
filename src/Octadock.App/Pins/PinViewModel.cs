using System.Runtime.Versioning;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Octadock.App.Pins;

/// <summary>
/// View model for a single floating pin. Holds the displayed image plus the
/// interactive state (opacity, click-through lock) and exposes the pin's toolbar
/// commands. The window supplies the actual copy/save/annotate/close behavior via
/// the <see cref="PinActions"/> callbacks so the view model stays UI-free.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class PinViewModel : ObservableObject
{
    [ObservableProperty]
    private double _opacity = 1.0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockGlyph))]
    [NotifyPropertyChangedFor(nameof(LockLabel))]
    private bool _isLocked;

    /// <summary>Creates a pin view model over the given image.</summary>
    public PinViewModel(BitmapSource image, PinActions actions)
    {
        Image = image ?? throw new ArgumentNullException(nameof(image));
        Actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    /// <summary>The pinned image.</summary>
    public BitmapSource Image { get; }

    /// <summary>The image's native pixel size (used for the initial window size).</summary>
    public double NaturalWidth => Image.PixelWidth;

    /// <summary>The image's native pixel size (used for the initial window size).</summary>
    public double NaturalHeight => Image.PixelHeight;

    /// <summary>The window-level operations this pin delegates to.</summary>
    public PinActions Actions { get; }

    /// <summary>The lock/unlock toggle glyph (Segoe MDL2 lock/unlock) shown on the pin toolbar.</summary>
    public string LockGlyph => IsLocked ? "\uE72E" : "\uE785";

    /// <summary>The lock toggle tooltip.</summary>
    public string LockLabel => IsLocked
        ? "Locked (click-through) — unlock to interact"
        : "Lock (let clicks pass through to apps below)";

    [RelayCommand]
    private async Task CopyAsync() => await Actions.CopyAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task SaveAsync() => await Actions.SaveAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task AnnotateAsync() => await Actions.AnnotateAsync().ConfigureAwait(true);

    [RelayCommand]
    private void ToggleLock()
    {
        IsLocked = !IsLocked;
        Actions.LockChanged(IsLocked);
    }

    [RelayCommand]
    private void Close() => Actions.Close();

    partial void OnOpacityChanged(double value) => Actions.OpacityChanged(value);
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

    /// <summary>Opens the pinned image in the annotation editor.</summary>
    public abstract Task AnnotateAsync();

    /// <summary>Closes (and forgets) the pin.</summary>
    public abstract void Close();

    /// <summary>Notifies that click-through was toggled, so the window applies it and persists.</summary>
    public abstract void LockChanged(bool locked);

    /// <summary>Notifies that opacity changed, so the window persists it.</summary>
    public abstract void OpacityChanged(double opacity);
}
