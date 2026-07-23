using System.Windows;
using Octadock.Core.Abstractions;

namespace Octadock.App.Services;

/// <summary>
/// WPF implementation of the model-download consent prompt (WS7, R6): a modal
/// Yes/No dialog that states the provider, the download size, and where the
/// files will be stored before any bytes are fetched. "No" (the default)
/// aborts the download. Shown on the UI thread; if there is no
/// application/dispatcher (headless), it declines rather than fetch silently.
/// </summary>
public sealed class MessageBoxModelDownloadConsentPrompt : IModelDownloadConsentPrompt
{
    /// <inheritdoc />
    public Task<bool> RequestAsync(
        ModelDownloadConsentRequest request, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(false);
        }

        string provider = string.IsNullOrWhiteSpace(request.ProviderName)
            ? "Speech engine"
            : request.ProviderName;
        string message =
            $"{request.ModelName} needs a one-time download of about {FormatSize(request.TotalBytes)} " +
            "before dictation can run on your PC.\n\n" +
            $"Provider: {provider}\n" +
            $"Stored on this PC under: {StorageText(request)}\n\n" +
            "The files download directly from the model host; nothing you say or type is sent to Octadock. " +
            "Download it now?";

        Application? app = Application.Current;
        if (app?.Dispatcher is null)
        {
            // No UI thread available — never fetch without a real confirmation.
            return Task.FromResult(false);
        }

        bool allowed = app.Dispatcher.Invoke(() =>
            MessageBox.Show(
                message,
                "Download speech model?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) == MessageBoxResult.Yes);

        return Task.FromResult(allowed);
    }

    private static string StorageText(ModelDownloadConsentRequest request)
        => string.IsNullOrWhiteSpace(request.StorageLocation)
            ? "the Octadock data folder"
            : request.StorageLocation;

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "several hundred MB";
        }

        double mb = bytes / 1024.0 / 1024.0;
        return mb >= 1024
            ? $"{mb / 1024.0:0.0} GB"
            : $"{mb:0} MB";
    }
}
