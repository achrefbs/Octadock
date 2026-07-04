using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.App.Services;

/// <summary>
/// Sink that actually shows a balloon/toast. Implemented by the tray controller
/// (which owns the native tray icon). Kept as a tiny seam so the notification
/// service does not depend on a tray UI implementation or create a construction cycle with the
/// tray.
/// </summary>
public interface INotificationSink
{
    /// <summary>Shows a notification with the given severity.</summary>
    void ShowBalloon(string title, string message, NotificationKind kind, Action? clickAction = null);
}

/// <summary>
/// <see cref="INotificationService"/> that forwards to the registered
/// <see cref="INotificationSink"/> (the tray icon) on the UI thread. When no sink
/// is registered yet (very early startup) the message is logged so nothing is
/// silently lost. Never uploads or blocks.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private volatile INotificationSink? _sink;

    /// <summary>Creates the notification service.</summary>
    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Registers (or replaces) the sink that renders notifications.</summary>
    public void SetSink(INotificationSink sink) => _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>Clears the sink when its owner is disposed.</summary>
    public void ClearSink(INotificationSink sink)
    {
        if (ReferenceEquals(_sink, sink))
        {
            _sink = null;
        }
    }

    /// <inheritdoc />
    public void Notify(
        string title,
        string message,
        NotificationKind kind = NotificationKind.Info,
        Action? clickAction = null)
    {
        title ??= string.Empty;
        message ??= string.Empty;

        INotificationSink? sink = _sink;
        if (sink is null)
        {
            _logger.LogInformation("Notification ({Kind}) before tray ready: {Title} - {Message}", kind, title, message);
            return;
        }

        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        void Show()
        {
            try
            {
                sink.ShowBalloon(title, message, kind, clickAction);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to show a notification balloon.");
            }
        }

        if (dispatcher.CheckAccess())
        {
            Show();
        }
        else
        {
            dispatcher.BeginInvoke(Show);
        }
    }
}
