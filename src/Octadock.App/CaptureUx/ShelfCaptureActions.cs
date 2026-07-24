using System.Runtime.Versioning;
using MahApps.Metro.IconPacks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Commands;
using Octadock.Core.Models;
using Octadock.Core.Settings;

namespace Octadock.App.CaptureUx;

/// <summary>
/// Secondary capture modes that live on the expanded Shelf instead of crowding
/// the primary Dock. Every entry is still an explicit user action.
/// </summary>
internal enum ShelfCaptureAction
{
    AllMonitors = 0,
    PreviousArea,
    Timer,
    Scrolling,
    Ocr,
}

/// <summary>A compact Shelf action's shared visual and accessibility contract.</summary>
internal sealed record ShelfCaptureActionDescriptor(
    ShelfCaptureAction Action,
    string Label,
    string ToolTip,
    PackIconLucideKind Icon);

/// <summary>
/// One catalog drives the Shelf action strip, automation names, tooltips, and
/// tests so truthful Beta/local wording cannot drift between those surfaces.
/// </summary>
internal static class ShelfCaptureActionCatalog
{
    public static IReadOnlyList<ShelfCaptureActionDescriptor> CompactActions { get; } =
    [
        new(
            ShelfCaptureAction.AllMonitors,
            "Capture all monitors",
            "Capture all monitors",
            PackIconLucideKind.MonitorUp),
        new(
            ShelfCaptureAction.PreviousArea,
            "Capture previous area",
            "Capture the previous area again",
            PackIconLucideKind.ScanLine),
        new(
            ShelfCaptureAction.Timer,
            "Capture with timer",
            "Capture an area after the configured timer",
            PackIconLucideKind.Timer),
        new(
            ShelfCaptureAction.Scrolling,
            "Scrolling capture — manual vertical (Beta)",
            "Scrolling capture — manual vertical only (Beta)",
            PackIconLucideKind.GalleryVerticalEnd),
        new(
            ShelfCaptureAction.Ocr,
            "Capture text with OCR",
            "Capture text from a region with local OCR",
            PackIconLucideKind.ScanText),
    ];

    public static ShelfCaptureActionDescriptor Get(ShelfCaptureAction action)
        => CompactActions.Single(descriptor => descriptor.Action == action);
}

/// <summary>
/// Narrow integration seam between the reusable Shelf window and the existing
/// capture/OCR services. Dependencies resolve only after a button is clicked,
/// avoiding a singleton cycle because <see cref="CaptureCoordinator"/> already
/// depends on <see cref="IShelfService"/>.
/// </summary>
[SupportedOSPlatform("windows10.0.19041.0")]
internal sealed partial class ShelfCaptureActionDispatcher
{
    private readonly IServiceProvider _services;

    public ShelfCaptureActionDispatcher(IServiceProvider services)
    {
        _services = services;
    }

    public async Task ExecuteAsync(ShelfCaptureAction action, CancellationToken cancellationToken = default)
    {
        try
        {
            ISettingsService settings = _services.GetRequiredService<ISettingsService>();
            PostCaptureAction destination = settings.Current.Capture.DefaultAction;

            switch (action)
            {
                case ShelfCaptureAction.AllMonitors:
                    await _services.GetRequiredService<CaptureCoordinator>()
                        .CaptureFullscreenAsync(destination, monitorToken: null, allMonitors: true, cancellationToken)
                        .ConfigureAwait(true);
                    break;

                case ShelfCaptureAction.PreviousArea:
                    await _services.GetRequiredService<CaptureCoordinator>()
                        .CapturePreviousAreaAsync(destination, cancellationToken)
                        .ConfigureAwait(true);
                    break;

                case ShelfCaptureAction.Timer:
                    await _services.GetRequiredService<CaptureCoordinator>()
                        .CaptureSelfTimerAsync(destination, region: null, cancellationToken)
                        .ConfigureAwait(true);
                    break;

                case ShelfCaptureAction.Scrolling:
                    await _services.GetRequiredService<CaptureCoordinator>()
                        .CaptureScrollingAsync(destination, cancellationToken)
                        .ConfigureAwait(true);
                    break;

                case ShelfCaptureAction.Ocr:
                    await _services.GetRequiredService<IOcrService>()
                        .CaptureRegionTextAsync(settings.Current.Ocr.OutputMode, language: null, cancellationToken)
                        .ConfigureAwait(true);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (_services.GetService<ILogger<ShelfCaptureActionDispatcher>>() is { } logger)
            {
                LogCaptureActionFailed(logger, ex, action);
            }

            _services.GetService<INotificationService>()?.Notify(
                "Capture could not start",
                $"{ShelfCaptureActionCatalog.Get(action).Label} failed. Try again or check Capture settings.",
                NotificationKind.Error);
        }
    }

    [LoggerMessage(
        EventId = 4201,
        Level = LogLevel.Warning,
        Message = "Shelf capture action {Action} failed.")]
    private static partial void LogCaptureActionFailed(
        ILogger logger,
        Exception exception,
        ShelfCaptureAction action);
}
