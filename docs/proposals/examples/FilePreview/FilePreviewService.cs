// Example implementation for the file-preview proposal.
// Target location: src/Octadock.App/Preview/ — WPF layer: provider selection,
// card window lifecycle, and the open/close animation policy.

using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.App.Preview;

/// <summary>How the preview card animates open (Settings → Appearance).</summary>
public enum PreviewOpenAnimation
{
    /// <summary>Scale up from the source rect (the "Quick Look zoom"). Default.</summary>
    QuickLookZoom = 0,

    /// <summary>Bottom-anchored zoom with a vertical stretch.</summary>
    Genie,

    /// <summary>Fade in + rise ~46 px. Used automatically when there is no source rect.</summary>
    SlideUp,

    /// <summary>Cross-fade + 96%→100% scale in place. Reduced-motion fallback.</summary>
    Spotlight,
}

/// <summary>
/// Picks a provider by extension, loads the preview off the UI thread, and
/// shows a single reusable <c>PreviewCardWindow</c>. Entry points (shelf drop,
/// the <c>open</c> command verb, hotkey → picker) all funnel here.
/// </summary>
public sealed class FilePreviewService
{
    private readonly IReadOnlyList<IFilePreviewProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly INotificationService _notifications;
    private readonly ILogger<FilePreviewService> _logger;
    private PreviewCardWindow? _card;

    public FilePreviewService(
        IEnumerable<IFilePreviewProvider> providers,
        ISettingsService settings,
        INotificationService notifications,
        ILogger<FilePreviewService> logger)
    {
        _providers = providers.OrderByDescending(p => p.Priority).ToList();
        _settings = settings;
        _notifications = notifications;
        _logger = logger;
    }

    /// <summary>
    /// Previews <paramref name="path"/>. <paramref name="sourceRect"/> is the
    /// on-screen rect the card should zoom out of (shelf card bounds, picker row,
    /// or null when opened from the CLI — which falls back to slide-up).
    /// <paramref name="fromProtocol"/> marks octadock:// activations, which are
    /// confirmed with the user first (drive-by-open hardening).
    /// </summary>
    public async Task<bool> PreviewAsync(
        string path,
        PixelRect? sourceRect = null,
        bool fromProtocol = false,
        CancellationToken cancellationToken = default)
    {
        string full;
        try
        {
            full = Path.GetFullPath(path);
        }
        catch (Exception)
        {
            _notifications.Notify("Preview", "That path is not valid.", NotificationKind.Warning);
            return false;
        }

        // Security: UNC targets can leak credentials on connect; protocol
        // activations of any path get a confirmation prompt.
        if (new Uri(full).IsUnc)
        {
            _notifications.Notify("Preview", "Network paths are not previewed.", NotificationKind.Warning);
            return false;
        }

        if (!File.Exists(full))
        {
            _notifications.Notify("Preview", "The file could not be found.", NotificationKind.Warning);
            return false;
        }

        if (fromProtocol && !ConfirmProtocolOpen(full))
        {
            return false;
        }

        string extension = Path.GetExtension(full).ToLowerInvariant();
        IFilePreviewProvider? provider = _providers.FirstOrDefault(p => p.CanPreview(extension));
        if (provider is null)
        {
            _notifications.Notify("Preview", $"No preview available for {extension} files.", NotificationKind.Info);
            return false;
        }

        var options = new FilePreviewOptions(); // Later: from _settings.Current.Preview.
        FilePreviewResult result;
        try
        {
            result = await Task.Run(() => provider.LoadAsync(full, options, cancellationToken), cancellationToken)
                .ConfigureAwait(true); // Back on the UI thread to show the card.
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview load failed for {Path}.", full);
            result = FilePreviewResult.Fail(full, ex.Message);
        }

        ShowCard(result, sourceRect);
        return result.Kind != FilePreviewKind.Error;
    }

    private void ShowCard(FilePreviewResult result, PixelRect? sourceRect)
    {
        // One card at a time, Quick Look-style: a new preview replaces the old.
        _card ??= new PreviewCardWindow();
        _card.Closed += (_, _) => _card = null;

        PreviewOpenAnimation animation = ResolveAnimation(sourceRect);
        _card.Present(result, sourceRect, animation);
    }

    private PreviewOpenAnimation ResolveAnimation(PixelRect? sourceRect)
    {
        // Reduced motion always wins; no source rect degrades zoom → slide-up.
        if (!SystemParameters.ClientAreaAnimation)
        {
            return PreviewOpenAnimation.Spotlight;
        }

        var preferred = PreviewOpenAnimation.QuickLookZoom; // Later: from settings.
        if (sourceRect is null && preferred is PreviewOpenAnimation.QuickLookZoom or PreviewOpenAnimation.Genie)
        {
            return PreviewOpenAnimation.SlideUp;
        }

        return preferred;
    }

    private static bool ConfirmProtocolOpen(string path)
        => MessageBox.Show(
               $"A link asked Octadock to preview:\n\n{path}\n\nOpen it?",
               "Octadock preview",
               MessageBoxButton.YesNo,
               MessageBoxImage.Question) == MessageBoxResult.Yes;
}
