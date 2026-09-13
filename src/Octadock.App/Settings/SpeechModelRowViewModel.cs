using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Settings;

/// <summary>One entry of the Settings → Dictation provider picker.</summary>
public sealed record SpeechProviderOption(string Id, string Label, string Detail);

/// <summary>
/// One local speech model in the Settings → Voice model manager: shows the
/// provider, whether it is on disk (and its model size when not), and
/// offers import with progress, cancel, retry, and delete without leaving
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

    /// <summary>True while an import is running (used to keep the row alive across rebuilds).</summary>
    public bool IsBusy { get; private set; }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (IsBusy)
        {
            return;
        }

        if (_provider is not ILocalSpeechModelImport importer) return;
        string source;
        if (importer.ImportUsesFolder)
        {
            var dialog = new OpenFolderDialog { Title = "Import local Parakeet model folder" };
            if (dialog.ShowDialog() != true) return;
            source = dialog.FolderName;
        }
        else
        {
            var dialog = new OpenFileDialog { Title = "Import local Whisper model", Filter = "Whisper GGML model|*.bin" };
            if (dialog.ShowDialog() != true) return;
            source = dialog.FileName;
        }
        await ImportFromAsync(source);
    }

    internal async Task ImportFromAsync(string source)
    {
        if (IsBusy || _provider is not ILocalSpeechModelImport importer) return;
        IsBusy = true;
        CanDownload = false;
        CanDelete = false;
        CanCancel = true;
        using var cancellation = new CancellationTokenSource();
        _downloadCts = cancellation;
        try
        {
            var progress = new Progress<double>(fraction =>
                StatusText = $"Importing… {fraction * 100:0}%");
            await Task.Run(() => importer.ImportModelAsync(_model, source, progress, cancellation.Token));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import cancelled — no model files are in use. Import again to retry.";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Model import failed for {Model}.", _model);
            StatusText = $"Import failed: {ex.Message} Use Import to retry.";
        }
        finally
        {
            _downloadCts = null;
            IsBusy = false;
            CanCancel = false;
            Refresh(keepFailureText: StatusText.StartsWith("Import failed", StringComparison.Ordinal)
                || StatusText.StartsWith("Import cancelled", StringComparison.Ordinal));
        }
    }

    /// <summary>Cancels the import; the installed model remains available.</summary>
    [RelayCommand]
    private void CancelDownload() => _downloadCts?.Cancel();

    /// <inheritdoc />
    public void Dispose()
    {
        _downloadCts?.Cancel();
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
            ? "Available locally"
            : $"Import from disk — {FormatBytes(_provider.ModelDownloadBytes(_model))}";
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
