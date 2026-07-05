using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Settings;

/// <summary>One entry of the Settings → Dictation provider picker.</summary>
public sealed record SpeechProviderOption(string Id, string Label, string Detail);

/// <summary>
/// One local speech model in the Settings → Dictation model manager: shows
/// whether it is on disk (and its download size when not), and offers
/// download-with-progress and delete without leaving Settings.
/// </summary>
public sealed partial class SpeechModelRowViewModel : ObservableObject
{
    private readonly IModelBackedSpeechProvider _provider;
    private readonly string _model;
    private readonly ILogger _logger;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool _canDownload;
    [ObservableProperty] private bool _canDelete;

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
        try
        {
            var progress = new Progress<double>(fraction =>
                StatusText = $"Downloading… {fraction * 100:0}%");
            await _provider.EnsureModelAsync(_model, progress, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model download failed for {Model}.", _model);
            StatusText = $"Download failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            Refresh(keepFailureText: StatusText.StartsWith("Download failed", StringComparison.Ordinal));
        }
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
