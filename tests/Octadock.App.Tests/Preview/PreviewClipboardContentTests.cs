using FluentAssertions;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Xunit;

namespace Octadock.App.Tests.Preview;

public class PreviewClipboardContentTests
{
    [Fact]
    public void ForResult_returns_plain_text_content()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.PlainText,
            FilePath = "sample.txt",
            Text = "hello",
        };

        PreviewClipboardContent.ForResult(result).Should().Be("hello");
    }

    [Fact]
    public void ForResult_returns_file_info_content()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.FileInfo,
            FilePath = "sample.bin",
            Text = "Full path  C:\\sample.bin",
        };

        PreviewClipboardContent.ForResult(result).Should().Be("Full path  C:\\sample.bin");
    }

    [Fact]
    public void Original_and_formatted_json_are_separate_copy_actions()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.PlainText,
            FilePath = "sample.json",
            SourceContent = "{\"a\":1}",
            RenderedContent = "{\n  \"a\": 1\n}",
            Warnings =
            [
                new FilePreviewWarning(FilePreviewWarningKind.Truncated, "Octadock presentation notice"),
            ],
        };

        PreviewClipboardContent.ForResult(result).Should().Be("{\"a\":1}");
        PreviewClipboardContent.FormattedForResult(result).Should().Be("{\n  \"a\": 1\n}");
        PreviewClipboardContent.ForResult(result).Should().NotContain("Octadock");
    }

    [Fact]
    public void Malformed_json_copy_preserves_the_exact_source_without_failure_copy()
    {
        const string source = "{ nope }";
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.PlainText,
            FilePath = "broken.json",
            SourceContent = source,
            Failure = new FilePreviewFailure(
                FilePreviewFailureKind.Malformed,
                "This JSON isn't valid. Showing the original text."),
        };

        PreviewClipboardContent.ForResult(result).Should().Be(source);
        PreviewClipboardContent.FormattedForResult(result).Should().BeNull();
    }

    [Fact]
    public void ForCsv_formats_header_and_rows_as_tab_separated_text()
    {
        var model = new CsvPreviewModel
        {
            Columns =
            [
                new CsvColumn("Name", CsvColumnType.Text),
                new CsvColumn("Count", CsvColumnType.Number),
            ],
            Rows =
            [
                ["Alpha", "2"],
                ["Beta", "3"],
            ],
            Delimiter = ',',
        };

        string expected = string.Join(
            Environment.NewLine,
            "Name\tCount",
            "Alpha\t2",
            "Beta\t3",
            string.Empty);

        PreviewClipboardContent.ForCsv(model).Should().Be(expected);
    }

    [Fact]
    public void ForCsv_escapes_special_cells_and_uses_the_declared_visible_schema()
    {
        var model = new CsvPreviewModel
        {
            Columns =
            [
                new CsvColumn("Name", CsvColumnType.Text),
                new CsvColumn("Note", CsvColumnType.Text),
                new CsvColumn("Column 3", CsvColumnType.Text),
            ],
            Rows =
            [
                ["Alpha", "he said \"yes\"", "extra"],
                ["Beta", "tab\tvalue", ""],
            ],
            Delimiter = ',',
        };

        string expected = string.Join(
            Environment.NewLine,
            "Name\tNote\tColumn 3",
            "Alpha\t\"he said \"\"yes\"\"\"\textra",
            "Beta\t\"tab\tvalue\"\t",
            string.Empty);

        PreviewClipboardContent.ForCsv(model).Should().Be(expected);
    }
}
