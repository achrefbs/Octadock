using System.Diagnostics;
using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.App.Context;
using Octadock.App.Imaging;
using Octadock.App.Services;
using Octadock.App.Windows;
using Octadock.Core.Abstractions;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Licensing;

namespace Octadock.App.Preview;

/// <summary>
/// Owns the one immediate Preview shell, enforces latest-request-wins, and keeps
/// all external/context actions explicit. Successful raster files continue to
/// use Octadock's Pin/image-viewer route.
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
    private readonly ContextService _context;
    private readonly ActiveContextState _activeContext;
    private readonly IWindowPresenter _windowPresenter;
    private readonly IPreviewCardHost _cardHost;
    private readonly ILogger<FilePreviewService> _logger;
    private readonly PreviewCardActions _cardActions;
    private readonly object _requestGate = new();

    private CancellationTokenSource? _currentRequest;
    private long _currentGeneration;
    private string? _lastPath;
    private bool _lastEnforceExternalFileGate = true;

    public FilePreviewService(
        IEnumerable<IFilePreviewProvider> providers,
        IClipboardService clipboard,
        ICaptureCoordinator coordinator,
        IPinService pins,
        INotificationService notifications,
        ILicenseGate licenseGate,
        ContextService context,
        ActiveContextState activeContext,
        IWindowPresenter windowPresenter,
        IPreviewCardHost cardHost,
        ILogger<FilePreviewService> logger)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.OrderByDescending(provider => provider.Priority).ToList();
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _pins = pins ?? throw new ArgumentNullException(nameof(pins));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        _licenseGate = licenseGate ?? throw new ArgumentNullException(nameof(licenseGate));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _activeContext = activeContext ?? throw new ArgumentNullException(nameof(activeContext));
        _windowPresenter = windowPresenter ?? throw new ArgumentNullException(nameof(windowPresenter));
        _cardHost = cardHost ?? throw new ArgumentNullException(nameof(cardHost));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _cardActions = new PreviewCardActions(
            CopyPath,
            CopyContent,
            RevealInExplorer,
            OpenWithDefaultApp,
            SaveCopyAs,
            PinImage,
            AddToShelf,
            Retry,
            Locate,
            CancelCurrentRequest,
            AddToContext,
            _activeContext);
        _cardHost.Closed += (_, _) => CancelAndRetireCurrentRequest();
    }

    public Task<bool> PreviewAsync(string path, CancellationToken cancellationToken = default)
        => PreviewCoreAsync(path, enforceExternalFileGate: true, cancellationToken);

    internal Task<bool> PreviewExistingAsync(string path, CancellationToken cancellationToken = default)
        => PreviewCoreAsync(path, enforceExternalFileGate: false, cancellationToken);

    private async Task<bool> PreviewCoreAsync(
        string path,
        bool enforceExternalFileGate,
        CancellationToken cancellationToken)
    {
        // Preserve the security/test invariant: pre-cancel happens before license
        // checks or any path/file access and does not disturb an existing preview.
        cancellationToken.ThrowIfCancellationRequested();
        if (enforceExternalFileGate && !_licenseGate.Allow(GatedFeature.FilePreview))
        {
            return false;
        }

        (long generation, CancellationTokenSource request) = BeginRequest(cancellationToken);
        string displayPath = path ?? string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                PresentIfCurrent(
                    generation,
                    FilePreviewResult.Fail(displayPath, FilePreviewFailureKind.InvalidPath));
                return true;
            }

            string full;
            try
            {
                full = Path.GetFullPath(path);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                PresentIfCurrent(
                    generation,
                    FilePreviewResult.Fail(displayPath, FilePreviewFailureKind.InvalidPath));
                return true;
            }

            displayPath = full;
            if (PathSafety.IsUncPath(full))
            {
                PresentIfCurrent(
                    generation,
                    FilePreviewResult.Fail(
                        full,
                        FilePreviewFailureKind.Unsupported,
                        productMessage: "Network paths are not previewed. Copy the file locally first."));
                return true;
            }

            lock (_requestGate)
            {
                if (generation == _currentGeneration)
                {
                    _lastPath = full;
                    _lastEnforceExternalFileGate = enforceExternalFileGate;
                }
            }

            // The shell is visible before provider selection, metadata reads, or decoding.
            PresentIfCurrent(generation, Loading(full));
            if (Environment.GetEnvironmentVariable(ToolWindowBase.UiAuditEnvVar) == "1")
            {
                // Give out-of-process UIA/screenshot tooling a deterministic
                // opportunity to record the real loading shell. Production
                // launches never set the audit flag and take no delay.
                await Task.Delay(TimeSpan.FromSeconds(1), request.Token).ConfigureAwait(true);
            }

            string extension = Path.GetExtension(full).ToLowerInvariant();
            IFilePreviewProvider? provider = _providers.FirstOrDefault(candidate => candidate.CanPreview(extension));

            if (ImageFileSupport.IsSupportedRasterExtension(extension))
            {
                FilePreviewResult validation = provider is null
                    ? FilePreviewResult.Fail(full, FilePreviewFailureKind.CodecUnavailable)
                    : await LoadProviderAsync(provider, full, request.Token).ConfigureAwait(true);
                request.Token.ThrowIfCancellationRequested();
                if (!IsCurrent(generation))
                {
                    return false;
                }

                if (validation.Kind == FilePreviewKind.Error)
                {
                    PresentIfCurrent(generation, validation);
                    return true;
                }

                try
                {
                    Task viewTask;
                    lock (_requestGate)
                    {
                        if (generation != _currentGeneration)
                        {
                            return false;
                        }

                        viewTask = _pins.ViewImageFileAsync(full, request.Token);
                    }

                    await viewTask.ConfigureAwait(true);
                    DismissIfCurrent(generation);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Image preview failed after validation for {Path}.", full);
                    PresentIfCurrent(generation, FailureFromException(full, ex));
                }

                return true;
            }

            FilePreviewResult result = provider is null
                ? await Task.Run(() => BuildFileInfoResult(full), request.Token).ConfigureAwait(true)
                : await LoadProviderAsync(provider, full, request.Token).ConfigureAwait(true);
            request.Token.ThrowIfCancellationRequested();
            PresentIfCurrent(generation, result);
            return IsCurrent(generation);
        }
        catch (OperationCanceledException) when (!IsCurrent(generation))
        {
            // Supersession/card close is intentionally silent: an old request can
            // never replace or reopen the current card.
            return false;
        }
        catch (OperationCanceledException)
        {
            PresentIfCurrent(
                generation,
                FilePreviewResult.Fail(displayPath, FilePreviewFailureKind.Cancelled));
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Preview load failed for {Path}.", displayPath);
            PresentIfCurrent(generation, FailureFromException(displayPath, ex));
            return true;
        }
        finally
        {
            CompleteRequest(generation, request);
        }
    }

    private static Task<FilePreviewResult> LoadProviderAsync(
        IFilePreviewProvider provider,
        string path,
        CancellationToken cancellationToken)
        => Task.Run(
            async () => await provider
                .LoadAsync(path, new FilePreviewOptions(), cancellationToken)
                .ConfigureAwait(false),
            cancellationToken);

    private (long Generation, CancellationTokenSource Request) BeginRequest(CancellationToken callerToken)
    {
        var request = CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        long generation;
        lock (_requestGate)
        {
            CancellationTokenSource? previous = _currentRequest;
            _currentRequest = request;
            generation = ++_currentGeneration;
            // Cancellation happens while the old request still owns disposal,
            // so BeginRequest can never race a disposed CTS.
            previous?.Cancel();
        }

        return (generation, request);
    }

    private void CompleteRequest(long generation, CancellationTokenSource request)
    {
        lock (_requestGate)
        {
            if (generation == _currentGeneration && ReferenceEquals(_currentRequest, request))
            {
                _currentRequest = null;
            }
        }

        request.Dispose();
    }

    private void CancelAndRetireCurrentRequest()
    {
        CancellationTokenSource? request;
        lock (_requestGate)
        {
            request = _currentRequest;
            _currentRequest = null;
            _currentGeneration++;
            request?.Cancel();
        }
        // The request that created the CTS remains its sole disposer in finally.
    }

    private bool IsCurrent(long generation)
    {
        lock (_requestGate)
        {
            return generation == _currentGeneration;
        }
    }

    private void PresentIfCurrent(long generation, FilePreviewResult result)
    {
        lock (_requestGate)
        {
            if (generation == _currentGeneration)
            {
                // The generation check and presentation are one serialized
                // operation: a stale completion cannot pass the guard, pause,
                // then overwrite a newer loading/result state.
                _cardHost.Present(result, _cardActions);
            }
        }
    }

    private void DismissIfCurrent(long generation)
    {
        lock (_requestGate)
        {
            if (generation == _currentGeneration)
            {
                _cardHost.Dismiss();
            }
        }
    }

    private static FilePreviewResult Loading(string path)
        => new()
        {
            Kind = FilePreviewKind.Loading,
            FilePath = path,
            Scope = new PreviewContentScope { Label = "Loading preview" },
        };

    private static FilePreviewResult FailureFromException(string path, Exception exception)
    {
        FilePreviewFailureKind kind = exception switch
        {
            ImagePreviewException image => image.FailureKind,
            FileNotFoundException or DirectoryNotFoundException => FilePreviewFailureKind.NotFound,
            UnauthorizedAccessException => FilePreviewFailureKind.AccessDenied,
            IOException io when (io.HResult & 0xFFFF) is 32 or 33 => FilePreviewFailureKind.Busy,
            IOException => FilePreviewFailureKind.Unknown,
            _ => FilePreviewFailureKind.Unknown,
        };
        return FilePreviewResult.Fail(path, kind);
    }

    private void Retry(string path)
    {
        bool enforce;
        lock (_requestGate)
        {
            enforce = _lastPath is not null &&
                string.Equals(_lastPath, path, StringComparison.OrdinalIgnoreCase)
                    ? _lastEnforceExternalFileGate
                    : true;
        }

        _ = PreviewCoreAsync(path, enforce, CancellationToken.None);
    }

    private void CancelCurrentRequest()
    {
        lock (_requestGate)
        {
            _currentRequest?.Cancel();
        }
    }

    private void Locate(string path)
    {
        try
        {
            var dialog = new OpenFileDialog
            {
                Title = $"Locate {Path.GetFileName(path)}",
                FileName = Path.GetFileName(path),
                CheckFileExists = true,
                Multiselect = false,
            };
            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            if (dialog.ShowDialog() == true)
            {
                Retry(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not locate replacement for preview path {Path}.", path);
            _notifications.Notify("Locate failed", "Could not open the file picker.", NotificationKind.Error);
        }
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
            string? directory = Path.GetDirectoryName(path);
            ProcessStartInfo start = File.Exists(path)
                ? new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{path}\"",
                    UseShellExecute = true,
                }
                : !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory)
                    ? new ProcessStartInfo(directory) { UseShellExecute = true }
                    : throw new FileNotFoundException();
            using (Process.Start(start))
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

            if (RequiresExternalOpenWarning(path))
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

    internal static bool RequiresExternalOpenWarning(string path)
        => PathSafety.IsExecutableExtension(path);

    private void SaveCopyAs(string path) => _ = SaveCopyAsAsync(path);

    private async Task SaveCopyAsAsync(string path)
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

            await App.Services.GetRequiredService<ISafeFileWriter>()
                .CopyAsync(info.FullName, destination).ConfigureAwait(true);
            _notifications.Notify("Saved a copy", Path.GetFileName(destination), NotificationKind.Success);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save a copy of preview file {Path}.", path);
            _notifications.Notify("Save failed", "Could not export the previewed file.", NotificationKind.Error);
        }
    }

    private void PinImage(string path) => _ = PinImageAsync(path);

    private async Task PinImageAsync(string path)
    {
        try
        {
            await _pins.PinImageFileAsync(path).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to pin preview image {Path}.", path);
            _notifications.Notify("Open failed", "Could not open the image.", NotificationKind.Error);
        }
    }

    private void AddToShelf(string path) => _ = AddImageToDockAsync(path);

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

    private void AddToContext(string path) => _ = AddToContextAsync(path);

    internal async Task AddToContextAsync(string path)
    {
        try
        {
            ContextPackage? package = await _activeContext.ResolveAsync(_context).ConfigureAwait(true);
            if (package is null)
            {
                _windowPresenter.ShowContext();
                _notifications.Notify(
                    "Choose a Context",
                    "Select or create the Context that should receive this file.",
                    NotificationKind.Info);
                return;
            }

            if (await _context.AddFileAsync(package.Id, path).ConfigureAwait(true))
            {
                _notifications.Notify("Added to Context", package.Name, NotificationKind.Success);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to add preview file {Path} to Context.", path);
            _notifications.Notify("Context failed", "Could not add this file to Context.", NotificationKind.Error);
        }
    }

    private static FilePreviewResult BuildFileInfoResult(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1,
            FileOptions.SequentialScan);
        long size = stream.Length;
        var info = new FileInfo(path);
        string lines = string.Join(
            Environment.NewLine,
            $"Name       {info.Name}",
            $"Size       {FormatSize(size)}",
            $"Created    {info.CreationTime:yyyy-MM-dd HH:mm:ss}",
            $"Modified   {info.LastWriteTime:yyyy-MM-dd HH:mm:ss}",
            $"Full path  {info.FullName}");
        return new FilePreviewResult
        {
            Kind = FilePreviewKind.FileInfo,
            FilePath = path,
            Text = lines,
            SourceByteLength = size,
            Scope = new PreviewContentScope
            {
                StartByte = 0,
                EndByteExclusive = size,
                Label = "File metadata only",
            },
        };
    }

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
