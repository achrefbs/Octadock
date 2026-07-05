using FluentAssertions;
using Octadock.App.Services;
using Octadock.Core.Commands;
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
}
