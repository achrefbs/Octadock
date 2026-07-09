using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Octadock.Core.Settings;

namespace Octadock.App.Pins;

/// <summary>Compact save-choice prompt for quick edits made on the pinned image surface.</summary>
[SupportedOSPlatform("windows")]
internal sealed class ImageSaveChoiceDialog : Window
{
    private readonly CheckBox _rememberBox;
    private ImageEditSaveBehavior? _choice;

    private ImageSaveChoiceDialog(bool canOverwriteOriginal)
    {
        Title = "Save image";
        Width = 390;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        WindowStyle = WindowStyle.None;

        var text = new SolidColorBrush(Color.FromRgb(241, 245, 249));
        var muted = new SolidColorBrush(Color.FromArgb(0xBA, 0xCB, 0xD5, 0xE1));
        var accent = new SolidColorBrush(Color.FromRgb(45, 212, 191));
        var surface = new SolidColorBrush(Color.FromArgb(0xF4, 0x09, 0x0F, 0x19));
        var borderBrush = new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));

        _rememberBox = new CheckBox
        {
            Content = "Save this option and never ask again",
            Foreground = muted,
            FontSize = 12,
            Margin = new Thickness(0, 14, 0, 0),
        };

        Button sameButton = MakeButton("Same image", accent, text, borderBrush);
        sameButton.IsEnabled = canOverwriteOriginal;
        sameButton.Click += (_, _) => Choose(ImageEditSaveBehavior.OverwriteOriginal);

        Button copyButton = MakeButton("New image", accent, text, borderBrush);
        copyButton.Click += (_, _) => Choose(ImageEditSaveBehavior.CreateCopy);

        Button cancelButton = MakeButton("Cancel", Brushes.Transparent, muted, borderBrush);
        cancelButton.Click += (_, _) =>
        {
            _choice = null;
            DialogResult = false;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            Children = { sameButton, copyButton, cancelButton },
        };

        var body = new StackPanel
        {
            Children =
            {
                new TextBlock
                {
                    Text = "Choose to save to the same image or create a new image",
                    Foreground = text,
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock
                {
                    Text = canOverwriteOriginal
                        ? "Same image overwrites the file you opened. New image creates a separate PNG or JPEG copy."
                        : "This source format cannot be safely overwritten yet. Create a new PNG or JPEG copy.",
                    Foreground = muted,
                    FontSize = 12,
                    Margin = new Thickness(0, 8, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                },
                _rememberBox,
                buttons,
            },
        };

        Content = new Border
        {
            Background = surface,
            BorderBrush = borderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Child = body,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 26,
                ShadowDepth = 8,
                Direction = 270,
                Opacity = 0.45,
                Color = Color.FromRgb(5, 7, 12),
            },
        };
    }

    public static ImageSaveChoice? Prompt(Window owner, bool canOverwriteOriginal)
    {
        var dialog = new ImageSaveChoiceDialog(canOverwriteOriginal)
        {
            Owner = owner,
        };

        bool? result = dialog.ShowDialog();
        return result == true && dialog._choice is { } choice
            ? new ImageSaveChoice(choice, dialog._rememberBox.IsChecked == true)
            : null;
    }

    private static Button MakeButton(
        string label,
        Brush background,
        Brush foreground,
        Brush border)
        => new()
        {
            Content = label,
            MinWidth = 92,
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            Background = background,
            Foreground = foreground,
            BorderBrush = border,
            BorderThickness = new Thickness(1),
        };

    private void Choose(ImageEditSaveBehavior behavior)
    {
        _choice = behavior;
        DialogResult = true;
    }
}

internal readonly record struct ImageSaveChoice(ImageEditSaveBehavior Behavior, bool Remember);
