using Octadock.Core.Geometry;
using Octadock.Core.Models;

namespace Octadock.Data.Tests.Infrastructure;

/// <summary>Convenience builders for the test records so individual tests stay terse.</summary>
internal static class RecordFactory
{
    /// <summary>A well-populated capture with every column set (including nullables) for round-trip tests.</summary>
    public static CaptureRecord FullCapture(
        Guid? id = null,
        CaptureType type = CaptureType.Area,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 6, 15, 9, 30, 15, 123, TimeSpan.Zero),
        Source = new CaptureSource("explorer.exe", "Documents", "sha256-abcdef"),
        MonitorId = new MonitorId("\\\\.\\DISPLAY2"),
        PixelWidth = 1800,
        PixelHeight = 1200,
        DpiScale = 1.5,
        OriginalPath = "Captures\\2026\\06\\15\\image.png",
        ThumbnailPath = "Thumbnails\\image.jpg",
        ProjectPath = "Projects\\image.octadock",
        DurationMs = 4200,
        DeletedAt = null,
    };

    /// <summary>A minimal capture with all optional columns left null.</summary>
    public static CaptureRecord MinimalCapture(
        Guid? id = null,
        CaptureType type = CaptureType.Fullscreen,
        DateTimeOffset? createdAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Type = type,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        Source = CaptureSource.Empty,
        OriginalPath = "Captures\\2026\\06\\01\\min.png",
    };

    /// <summary>An action record for the given capture.</summary>
    public static ActionRecord Action(
        Guid captureId,
        ActionType actionType = ActionType.Copied,
        DateTimeOffset? createdAt = null,
        string? destination = null,
        string? metadataJson = null) => new()
    {
        Id = Guid.NewGuid(),
        CaptureId = captureId,
        ActionType = actionType,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 6, 15, 10, 0, 0, TimeSpan.Zero),
        Destination = destination,
        MetadataJson = metadataJson,
    };

    /// <summary>A pin record.</summary>
    public static PinRecord Pin(
        Guid? id = null,
        Guid? captureId = null,
        string? imagePath = null,
        int x = 10,
        int y = 20,
        int width = 300,
        int height = 200,
        double opacity = 0.9,
        bool clickThrough = false,
        DateTimeOffset? lastVisibleAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        CaptureId = captureId,
        ImagePath = imagePath,
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Opacity = opacity,
        ClickThrough = clickThrough,
        MonitorId = new MonitorId("\\\\.\\DISPLAY1"),
        LastVisibleAt = lastVisibleAt,
    };

    /// <summary>An AI session with representative fields filled in.</summary>
    public static AiSessionRecord AiSession(
        Guid? id = null,
        AiSessionProvider provider = AiSessionProvider.Codex,
        AiSessionStatus status = AiSessionStatus.Running,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        DateTimeOffset? lastEventAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Provider = provider,
        Title = "Fix tray icon menu",
        Cwd = @"C:\Users\acera\Desktop\Workspace\Octadock",
        GitBranch = "codex/fix-tray-menu",
        Command = "codex exec \"fix tray\"",
        Pid = 12345,
        Status = status,
        StartedAt = startedAt ?? new DateTimeOffset(2026, 7, 3, 9, 0, 0, TimeSpan.Zero),
        EndedAt = endedAt,
        ExitCode = endedAt is null ? null : 0,
        LastEventAt = lastEventAt,
        NotificationMode = AiSessionNotificationMode.ToastAndTts,
        MetadataJson = "{\"threadId\":\"abc\"}",
    };

    /// <summary>An event for the given AI session.</summary>
    public static AiSessionEventRecord AiSessionEvent(
        Guid sessionId,
        AiSessionEventType eventType = AiSessionEventType.Output,
        DateTimeOffset? createdAt = null,
        string? message = "Working on it.",
        string? metadataJson = null) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        EventType = eventType,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 7, 3, 9, 5, 0, TimeSpan.Zero),
        Message = message,
        MetadataJson = metadataJson,
    };

    /// <summary>An artifact link for the given AI session.</summary>
    public static AiSessionArtifactRecord AiSessionArtifact(
        Guid sessionId,
        AiSessionArtifactKind artifactKind = AiSessionArtifactKind.File,
        DateTimeOffset? createdAt = null,
        Guid? captureId = null) => new()
    {
        Id = Guid.NewGuid(),
        SessionId = sessionId,
        ArtifactKind = artifactKind,
        CreatedAt = createdAt ?? new DateTimeOffset(2026, 7, 3, 9, 10, 0, TimeSpan.Zero),
        CaptureId = captureId,
        ExternalId = "pr-42",
        Path = @"C:\Users\acera\Desktop\Workspace\Octadock\README.md",
        Uri = "https://github.com/example/Octadock/pull/42",
        Title = "Implementation PR",
        MetadataJson = "{\"provider\":\"github\"}",
    };
}
