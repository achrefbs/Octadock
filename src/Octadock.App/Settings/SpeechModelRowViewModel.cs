using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Settings;

/// <summary>One entry of the Settings → Dictation provider picker.</summary>
public sealed record SpeechProviderOption(string Id, string Label, string Detail);

/// <summary>
/// One local speech model in the Settings → Voice model manager: shows the
/// provider, whether it is on disk (and its download size when not), and
/// offers download-with-progress, cancel, retry, and delete without leaving
/// Settings.
/// </summary>
public sealed partial class SpeechModelRowViewModel : ObservableObject, IDisposable
{
    private readonly IModelBackedSpeechProvider _provider;
    private readonly string _model;
    private readonly ILogger _logger;
    private CancellationTokenSource? _downloadCts;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _canDelete;
    [ObservableProperty] private bool _canCancel;

    public SpeechModelRowViewModel(
        IModelBackedSpeechProvider provider, string model, string displayName, ILogger logger)
    {
        _provider = provider;
        _model = model;
        _logger = logger;
        DisplayName = displayName;
        Refresh();
    }

    /// <summary>Human-readable model name, e.g. "Parakeet TDT 0.6B v3 (default engine)".</summary>
    public string DisplayName { get; }

    /// <summary>True while a download is running (used to keep the row alive across rebuilds).</summary>
    public bool IsBusy { get; private set; }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        CanDownload = false;
        CanDelete = false;
        CanCancel = true;
        _downloadCts = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(fraction =>
                StatusText = $"Downloading… {fraction * 100:0}%");
            await _provider.EnsureModelAsync(_model, progress, _downloadCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Download cancelled — no model files are in use. Download again to retry.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model download failed for {Model}.", _model);
            StatusText = $"Download failed: {ex.Message} Use Download to retry.";
        }
        finally
        {
            _downloadCts.Dispose();
            _downloadCts = null;
            IsBusy = false;
            CanCancel = false;
            Refresh(keepFailureText: StatusText.StartsWith("Download failed", StringComparison.Ordinal)
                || StatusText.StartsWith("Download cancelled", StringComparison.Ordinal));
        }
    }

    /// <summary>Cancels the in-flight download; partial files stay resumable.</summary>
    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
        _downloadCts?.Cancel();
        _downloadCts?.Dispose();
        _downloadCts = null;
    }

    [RelayCommand]
    private void Delete()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            _provider.DeleteModel(_model);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model delete failed for {Model}.", _model);
            StatusText = $"Delete failed: {ex.Message}";
            return;
        }

        Refresh();
    }

    private void Refresh(bool keepFailureText = false)
    {
        bool downloaded = _provider.IsModelAvailable(_model);
        CanDownload = !downloaded;
        CanDelete = downloaded;
        if (keepFailureText)
        {
            return;
        }

        StatusText = downloaded
            ? "Downloaded"
            : $"Not downloaded — {FormatBytes(_provider.ModelDownloadBytes(_model))}";
    }

    private static string FormatBytes(long bytes)
        => bytes switch
        {
            <= 0 => "size unknown",
            < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
            < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024.0:0} MB",
            _ => $"{bytes / 1024.0 / 1024.0 / 1024.0:0.0} GB",
        };
}
