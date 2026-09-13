using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using Octadock.App.Ai;

namespace Octadock.App.Editing;

[SupportedOSPlatform("windows")]
public partial class ImageMockupWindow : Window
{
    private readonly IImageMockupService _service;
    private readonly byte[] _sourcePng;
    private readonly ImageEditRegion _region;
    private bool _generating;

    public ImageMockupWindow(
        IImageMockupService service,
        IImageEditProvider provider,
        byte[] sourcePng,
        ImageEditRegion region)
    {
        _service = service;
        _sourcePng = sourcePng;
        _region = region;
        InitializeComponent();
        Octadock.App.Windows.ScreenFit.Attach(this);

        BeforeImage.Source = Load(sourcePng);
        ProviderText.Text = provider.ProviderDisplayName;
        RegionText.Text = $"Selected region · {region.Width:N0} × {region.Height:N0} px";
        ConsentText.Text =
            $"Send only this region plus 32 px of visual context ({region.PixelCount:N0} selected pixels) to " +
            $"{provider.ProviderDisplayName}. The crop may contain visible text, code, or secrets.";
        InstructionBox.TextChanged += OnInputChanged;
        Loaded += (_, _) => InstructionBox.Focus();
        UpdateGenerateState();
    }

    public ImageMockupResult? Result { get; private set; }

    public string TextDelta => InstructionBox.Text.Trim();

    private async void OnGenerate(object sender, RoutedEventArgs e)
    {
        if (_generating || !ConsentBox.IsChecked.GetValueOrDefault() || string.IsNullOrWhiteSpace(TextDelta))
        {
            return;
        }

        _generating = true;
        ErrorText.Text = string.Empty;
        ProgressText.Text = "Generating a bounded variant…";
        GenerateButton.IsEnabled = false;
        ApproveButton.IsEnabled = false;
        try
        {
            Result = await _service.GenerateAsync(_sourcePng, _region, TextDelta).ConfigureAwait(true);
            AfterImage.Source = Load(Result.CompositePng);
            EmptyPreviewText.Visibility = Visibility.Collapsed;
            ResultText.Text =
                $"Verified: 0 pixels changed outside the selection · " +
                $"{Result.ChangedPixelsInsideSelection:N0} changed inside";
            ResultBadge.Visibility = Visibility.Visible;
            ProgressText.Text = $"Generated with {Result.Provider} · {Result.Model}";
            ApproveButton.IsEnabled = true;
            GenerateButton.Content = "Regenerate";
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ProgressText.Text = string.Empty;
        }
        finally
        {
            _generating = false;
            UpdateGenerateState();
        }
    }

    private void OnApprove(object sender, RoutedEventArgs e)
    {
        if (Result is null) return;
        DialogResult = true;
    }

    private void OnInputChanged(object? sender, RoutedEventArgs e) => UpdateGenerateState();

    private void UpdateGenerateState()
    {
        if (GenerateButton is null) return;
        GenerateButton.IsEnabled = !_generating && ConsentBox.IsChecked.GetValueOrDefault() &&
                                   !string.IsNullOrWhiteSpace(InstructionBox.Text);
    }

    private static BitmapSource Load(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}
