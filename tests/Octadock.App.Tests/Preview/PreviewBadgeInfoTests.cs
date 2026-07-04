using FluentAssertions;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class PreviewBadgeInfoTests
{
    [Theory]
    [InlineData("data.csv", FilePreviewKind.Csv, "CSV", "TABLE")]
    [InlineData("data.tsv", FilePreviewKind.Csv, "TSV", "TABLE")]
    [InlineData("notes.md", FilePreviewKind.PlainText, "MD", "MARKDOWN")]
    [InlineData("package.json", FilePreviewKind.PlainText, "JSON", "DATA")]
    [InlineData("app.log", FilePreviewKind.PlainText, "LOG", "LOG")]
    [InlineData("Program.cs", FilePreviewKind.PlainText, "CS", "CODE")]
    [InlineData("settings.yaml", FilePreviewKind.PlainText, "YAML", "CONFIG")]
    [InlineData("index.html", FilePreviewKind.PlainText, "HTML", "WEB")]
    [InlineData("image.jpeg", FilePreviewKind.Image, "JPEG", "IMAGE")]
    [InlineData("archive.octadock", FilePreviewKind.FileInfo, "FILE", "FILE")]
    [InlineData("broken.txt", FilePreviewKind.Error, "TXT", "ERROR")]
    public void BuildPreviewBadgeInfo_classifies_provider_category(
        string path,
        FilePreviewKind kind,
        string shortLabel,
        string providerLabel)
    {
        var result = new FilePreviewResult
        {
            Kind = kind,
            FilePath = path,
        };

        PreviewBadgeInfo badge = PreviewCardWindow.BuildPreviewBadgeInfo(result);

        badge.ShortLabel.Should().Be(shortLabel);
        badge.ProviderLabel.Should().Be(providerLabel);
        badge.ToolTip.Should().Contain(shortLabel);
        badge.ToolTip.Should().Contain(providerLabel);
    }
}
