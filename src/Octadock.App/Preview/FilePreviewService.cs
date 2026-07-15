using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.Core.Abstractions;
using Octadock.Core.Io;
using Octadock.Core.Licensing;

namespace Octadock.App.Preview;

/// <summary>
/// Routes raster images to Octadock's clean always-on-top image viewer. Other
/// supported files are loaded off the UI thread and shown in a reusable
/// <see cref="PreviewCardWindow"/> Quick Look surface.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FilePreviewService
{
    private readonly IReadOnlyList<IFilePreviewProvider> _providers;
    private readonly IClipboardService _clipboard;
    private readonly ICaptureCoordinator _coordinator;
    private readonly IPinService _pins;
    private readonly INotificationService _notifications;
    private readonly ILicenseGate _licenseGate;
    private readonly ILogger<FilePreviewService> _logger;
    private PreviewCardWindow? _card;

    /// <summary>Creates the preview service over the registered providers.</summary>
    public FilePreviewService(
        IEnumerable<IFilePreviewProvider> providers,
        IClipboardService clipboard,
        ICaptureCoordinator coordinator,
        IPinService pins,
        INotificationService notifications,
        ILicenseGate licenseGate,
        ILogger<FilePreviewService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.OrderByDescending(p => p.Priority).ToList();
        _clipboard = clipboard;
        _coordinator = coordinator;
        _pins = pins;
        _notifications = notifications;
        _licenseGate = licenseGate;
        _logger = logger;
    }

    /// <summary>
    /// Previews <paramref name="path"/>: validates it, selects a provider, loads
    /// the model off the UI thread, and presents the reusable card. Returns
    /// false only when the path is rejected or no preview card can be shown.
    /// Provider-level errors are rendered inside the card so callers do not
    /// also surface a duplicate "could not preview" notification.
    /// </summary>
    public Task<bool> PreviewAsync(string path, CancellationToken cancellationToken = default)
        => PreviewCoreAsync(path, enforceExternalFileGate: true, cancellationToken);

    /// <summary>
    /// Views an item that is already part of Octadock's library or Context stack.
    /// Existing user data stays viewable after trial expiry; any mutating action in
    /// the card (pin/add) still passes through its own gated service seam.
    /// Internal visibility prevents automation/Explorer callers from using this as
    /// an external-file gate bypass.
    /// </summary>
    internal Task<bool> PreviewExistingAsync(string path, CancellationToken cancellationToken = default)
        => PreviewCoreAsync(path, enforceExternalFileGate: false, cancellationToken);

    private async Task<bool> PreviewCoreAsync(
        string path,
        bool enforceExternalFileGate,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Trial/license gate (WS5): only a NEW external preview is blocked. Existing
        // library/Context items use PreviewExistingAsync and remain viewable.
        if (enforceExternalFileGate && !_licenseGate.Allow(GatedFeature.FilePreview))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            _notifications.Notify("Preview", "That path is not valid.", NotificationKind.Warning);
            return false;
        }

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

        // Security: UNC targets can leak credentials on connect and are not
        // previewed in v1.
        bool isUnc;
        try
        {
            isUnc = new Uri(full).IsUnc;
        }
        catch (UriFormatException)
        {
            _notifications.Notify("Preview", "That path is not valid.", NotificationKind.Warning);
            return false;
        }

        if (isUnc)
        {
            _notifications.Notify("Preview", "Network paths are not previewed.", NotificationKind.Warning);
            return false;
        }

        if (!File.Exists(full))
        {
            _notifications.Notify("Preview", "The file could not be found.", NotificationKind.Warning);
            return false;
        }

        string extension = Path.GetExtension(full).ToLowerInvariant();
        if (ImageFileSupport.IsSupportedRasterExtension(extension))
        {
            // Keep the bounded validation provider, but never render raster images
            // in the legacy generic preview card. All image entry points converge on
            // the modern floating viewer with its hover toolbar and pin behavior.
            IFilePreviewProvider? imageProvider = _providers.FirstOrDefault(p => p.CanPreview(extension));
            if (imageProvider is not null)
            {
                FilePreviewResult validation = await Task.Run(
                    () => imageProvider.LoadAsync(full, new FilePreviewOptions(), cancellationToken),
                    cancellationToken).ConfigureAwait(true);
                cancellationToken.ThrowIfCancellationRequested();
                if (validation.Kind == FilePreviewKind.Error)
                {
                    _notifications.Notify(
                        "Open failed",
                        validation.Error ?? "The image could not be opened.",
                        NotificationKind.Warning);
                    return false;
                }
            }

            await _pins.ViewImageFileAsync(full, cancellationToken).ConfigureAwait(true);
            return true;
        }

        IFilePreviewProvider? provider = _providers.FirstOrDefault(p => p.CanPreview(extension));
        if (provider is null)
        {
            // No dedicated preview for this type: show a small file card with an
            // "open with the default app" action rather than erroring, so an
            // unsupported type never feels broken.
            ShowCard(BuildFileInfoResult(full));
            return true;
        }

        var options = new FilePreviewOptions(); // Later: from settings.
        FilePreviewResult result;
        try
        {
            // Parse off the UI thread; ContinueWith on it to show the card.
            result = await Task.Run(() => provider.LoadAsync(full, options, cancellationToken), cancellationToken)
                .ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview load failed for {Path}.", full);
            result = FilePreviewResult.Fail(full, ex.Message);
        }

        ShowCard(result);
        return true;
    }

    private void ShowCard(FilePreviewResult result)
    {
        // One card at a time, Quick Look-style: a new preview replaces the old.
        if (_card is null || _card.IsClosing)
        {
            var card = new PreviewCardWindow(new PreviewCardActions(
                CopyPath,
                CopyContent,
                RevealInExplorer,
                OpenWithDefaultApp,
                SaveCopyAs,
                PinImage,
                AddToShelf));
            card.Closed += (_, _) =>
            {
                if (ReferenceEquals(_card, card))
                {
                    _card = null;
                }
            };
            _card = card;
        }

        _card.Present(result);
    }

    private void CopyPath(string path)
    {
        try
        {
            _clipboard.SetText(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy preview path {Path}.", path);
            _notifications.Notify("Copy failed", "Could not copy the file path.", NotificationKind.Error);
        }
    }

    private void CopyContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return;
        }

        try
        {
            _clipboard.SetText(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy preview content.");
            _notifications.Notify("Copy failed", "Could not copy the preview content.", NotificationKind.Error);
        }
    }

    private void RevealInExplorer(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _notifications.Notify("Reveal failed", "The file could not be found.", NotificationKind.Warning);
                return;
            }

            using (Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true,
            }))
            {
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reveal preview file {Path}.", path);
            _notifications.Notify("Reveal failed", "Could not open the file location.", NotificationKind.Error);
        }
    }

    private void OpenWithDefaultApp(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _notifications.Notify("Open failed", "The file could not be found.", NotificationKind.Warning);
                return;
            }

            // Executable guard (WS9): a .exe/.bat/.ps1/… must never launch from a
            // single click — require an explicit, warned confirmation at this seam so
            // every caller is protected, not just the one UI path.
            if (PathSafety.IsExecutableExtension(path))
            {
                MessageBoxResult choice = MessageBox.Show(
                    $"\"{Path.GetFileName(path)}\" is an executable or script. Running it could harm your PC or run untrusted code.\n\nOpen it anyway?",
                    "Open executable file?",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);
                if (choice != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            using (Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }))
            {
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open preview file {Path}.", path);
            _notifications.Notify("Open failed", "Could not open the file.", NotificationKind.Error);
        }
    }

    private void SaveCopyAs(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _notifications.Notify("Save failed", "The file could not be found.", NotificationKind.Warning);
                return;
            }

            var info = new FileInfo(path);
            var dialog = new SaveFileDialog
            {
                Title = "Save a copy",
                FileName = info.Name,
                InitialDirectory = info.DirectoryName,
                Filter = BuildSaveFilter(info.Extension),
                OverwritePrompt = true,
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string destination = Path.GetFullPath(dialog.FileName);
            if (string.Equals(destination, info.FullName, StringComparison.OrdinalIgnoreCase))
            {
                _notifications.Notify("Save failed", "Choose a different destination for the copy.", NotificationKind.Warning);
                return;
            }

            App.Services.GetRequiredService<ISafeFileWriter>()
                .CopyAsync(info.FullName, destination).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save a copy of preview file {Path}.", path);
            _notifications.Notify("Save failed", "Could not export the previewed file.", NotificationKind.Error);
        }
    }

    private void PinImage(string path)
        => _ = PinImageAsync(path);

    private async Task PinImageAsync(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                _notifications.Notify("Open failed", "The file could not be found.", NotificationKind.Warning);
                return;
            }

            await _pins.PinImageFileAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to open preview image {Path}.", path);
            _notifications.Notify("Open failed", "Could not open the image.", NotificationKind.Error);
        }
    }

    private void AddToShelf(string path)
        => _ = AddImageToDockAsync(path);

    public async Task AddImageToDockAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            await _coordinator.AddExternalFileAsync(path, cancellationToken).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add preview file {Path} to the dock.", path);
            _notifications.Notify("Add to dock failed", "Could not add the previewed file to the dock.", NotificationKind.Error);
        }
    }

    /// <summary>
    /// Builds a <see cref="FilePreviewKind.FileInfo"/> fallback result: the
    /// "Name / Size / Created / Modified / Full path" lines carried in
    /// <see cref="FilePreviewResult.Text"/>. Metadata reads are best-effort.
    /// </summary>
    private static FilePreviewResult BuildFileInfoResult(string path)
    {
        string lines;
        try
        {
            var info = new FileInfo(path);
            lines = string.Join(
                Environment.NewLine,
                $"Name       {info.Name}",
                $"Size       {FormatSize(info.Length)}",
                $"Created    {info.CreationTime:yyyy-MM-dd HH:mm:ss}",
                $"Modified   {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
                $"Full path  {info.FullName}");
        }
        catch (Exception)
        {
            lines = $"Full path  {path}";
        }

        return new FilePreviewResult
        {
            Kind = FilePreviewKind.FileInfo,
            FilePath = path,
            Text = lines,
        };
    }

    /// <summary>Formats a byte count as a compact human-readable size.</summary>
    private static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0
            ? $"{bytes:N0} {units[unit]}"
            : $"{value:0.##} {units[unit]} ({bytes:N0} bytes)";
    }

    internal static string BuildSaveFilter(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "All files (*.*)|*.*";
        }

        string normalized = extension.StartsWith('.') ? extension : "." + extension;
        string upper = normalized.TrimStart('.').ToUpperInvariant();
        return $"{upper} files (*{normalized})|*{normalized}|All files (*.*)|*.*";
    }
}
