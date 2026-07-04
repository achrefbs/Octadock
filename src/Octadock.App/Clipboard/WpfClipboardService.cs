using System.IO;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.App.Imaging;
using Octadock.Core.Abstractions;
using Octadock.Core.Imaging;

namespace Octadock.App.Clipboard;

/// <summary>
/// <see cref="IClipboardService"/> over <see cref="System.Windows.Clipboard"/> and
/// <see cref="DataObject"/>. All clipboard access is marshaled to the UI (STA)
/// dispatcher and retried a few times because the clipboard is a shared, lockable
/// OS resource (<c>CLIPBRD_E_CANT_OPEN</c> / HRESULT 0x800401D0). For images we
/// place both a decoded bitmap and, when a backing file exists, a file-drop list
/// so paste targets that prefer files still work.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed partial class WpfClipboardService : IClipboardService
{
    private const int OpenClipboardFailed = unchecked((int)0x800401D0); // CLIPBRD_E_CANT_OPEN
    private const int MaxAttempts = 8;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(60);

    private readonly ILogger<WpfClipboardService> _logger;

    /// <summary>Creates the clipboard service.</summary>
    public WpfClipboardService(ILogger<WpfClipboardService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static Dispatcher Dispatcher =>
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public bool ContainsImage() => OnSta(() => System.Windows.Clipboard.ContainsImage());

    /// <inheritdoc />
    public void SetImage(EncodedImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        BitmapSource bitmap = Decode(image);

        OnSta(() =>
        {
            var data = new DataObject();
            data.SetImage(bitmap);
            SetDataObjectWithRetry(data);
        });
    }

    /// <inheritdoc />
    public void SetImageFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        BitmapSource bitmap = FrameImaging.LoadFromFile(filePath);

        OnSta(() =>
        {
            var data = new DataObject();
            data.SetImage(bitmap);

            if (File.Exists(filePath))
            {
                var files = new System.Collections.Specialized.StringCollection { filePath };
                data.SetFileDropList(files);
            }

            SetDataObjectWithRetry(data);
        });
    }

    /// <inheritdoc />
    public void SetFileDropList(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        var files = new System.Collections.Specialized.StringCollection();
        foreach (string path in filePaths)
        {
            if (!string.IsNullOrWhiteSpace(path))
            {
                files.Add(path);
            }
        }

        if (files.Count == 0)
        {
            return;
        }

        OnSta(() =>
        {
            var data = new DataObject();
            data.SetFileDropList(files);
            SetDataObjectWithRetry(data);
        });
    }

    /// <inheritdoc />
    public void SetText(string text)
    {
        text ??= string.Empty;
        OnSta(() =>
        {
            var data = new DataObject();
            data.SetText(text);
            SetDataObjectWithRetry(data);
        });
    }

    /// <inheritdoc />
    public string? TryGetText()
    {
        return OnSta<string?>(() =>
        {
            try
            {
                return System.Windows.Clipboard.ContainsText()
                    ? System.Windows.Clipboard.GetText()
                    : null;
            }
            catch (Exception ex)
            {
                LogFailedToReadClipboardText(_logger, ex);
                return null;
            }
        });
    }

    /// <inheritdoc />
    public EncodedImage? TryGetImage()
    {
        return OnSta<EncodedImage?>(() =>
        {
            if (!System.Windows.Clipboard.ContainsImage())
            {
                return null;
            }

            try
            {
                BitmapSource? bitmap = System.Windows.Clipboard.GetImage();
                if (bitmap is null)
                {
                    return null;
                }

                byte[] png = FrameImaging.EncodePng(bitmap);
                return new EncodedImage(png, ExportImageFormat.Png);
            }
            catch (Exception ex)
            {
                LogFailedToReadClipboardImage(_logger, ex);
                return null;
            }
        });
    }

    private static BitmapSource Decode(EncodedImage image)
    {
        using var stream = new MemoryStream(image.Bytes.ToArray());
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        frame.Freeze();
        return frame;
    }

    private void SetDataObjectWithRetry(DataObject data)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                // copy=true flushes the data so it survives after Octadock exits.
                System.Windows.Clipboard.SetDataObject(data, copy: true);
                return;
            }
            catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == OpenClipboardFailed && attempt < MaxAttempts)
            {
                LogClipboardLocked(_logger, attempt, MaxAttempts);
                Thread.Sleep(RetryDelay);
            }
            catch (Exception ex)
            {
                LogFailedToSetClipboardData(_logger, ex);
                throw;
            }
        }
    }

    private static void OnSta(Action action)
    {
        Dispatcher dispatcher = Dispatcher;
        if (dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }

    private static T OnSta<T>(Func<T> func)
    {
        Dispatcher dispatcher = Dispatcher;
        return dispatcher.CheckAccess() ? func() : dispatcher.Invoke(func);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Failed to read clipboard text.")]
    private static partial void LogFailedToReadClipboardText(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Failed to read the clipboard image.")]
    private static partial void LogFailedToReadClipboardImage(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Clipboard locked (attempt {Attempt}/{Max}); retrying.")]
    private static partial void LogClipboardLocked(ILogger logger, int attempt, int max);

    [LoggerMessage(EventId = 4, Level = LogLevel.Warning, Message = "Failed to set clipboard data.")]
    private static partial void LogFailedToSetClipboardData(ILogger logger, Exception exception);
}
