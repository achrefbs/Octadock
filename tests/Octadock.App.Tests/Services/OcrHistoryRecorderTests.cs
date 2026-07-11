using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Services;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Commands;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.Services;

public sealed class OcrHistoryRecorderTests
{
    [Fact]
    public void Metadata_round_trips_text_mode_and_language()
    {
        string json = OcrHistoryRecorder.BuildMetadataJson("hello\nworld", OcrTextMode.Lines, "en-US");

        json.Should().Contain("\"Mode\":\"Lines\"");
        json.Should().Contain("\"Language\":\"en-US\"");
        OcrHistoryRecorder.TryReadExtractedText(json).Should().Be("hello\nworld");
    }

    [Fact]
    public void Metadata_omits_blank_language_and_marks_truncation()
    {
        string longText = new('x', OcrHistoryRecorder.MaxStoredTextLength + 10);

        string json = OcrHistoryRecorder.BuildMetadataJson(longText, OcrTextMode.Compact, "  ");

        json.Should().Contain("\"Truncated\":true");
        OcrHistoryRecorder.TryReadExtractedText(json)!.Length.Should().Be(OcrHistoryRecorder.MaxStoredTextLength);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"Text\":\"\"}")]
    public void TryReadExtractedText_returns_null_for_missing_or_bad_metadata(string? metadata)
    {
        OcrHistoryRecorder.TryReadExtractedText(metadata).Should().BeNull();
    }

    [Fact]
    public async Task Record_honors_pre_cancelled_request_before_writing_any_history_artifact()
    {
        var recorder = new OcrHistoryRecorder(
            null!,
            null!,
            null!,
            null!,
            null!,
            new DefaultSettings(),
            NullLogger<OcrHistoryRecorder>.Instance);
        var frame = new CapturedFrame(
            new byte[4],
            1,
            1,
            4,
            FramePixelFormat.Bgra32,
            1,
            MonitorId.Unknown,
            CaptureSource.Empty,
            DateTimeOffset.UtcNow);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => recorder.RecordAsync(frame, "text", OcrTextMode.Lines, null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class DefaultSettings : ISettingsService
    {
        public OctadockSettings Current { get; private set; } = OctadockSettings.Defaults;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);

        public event EventHandler<SettingsChangedEventArgs>? Changed { add { } remove { } }
    }
}
