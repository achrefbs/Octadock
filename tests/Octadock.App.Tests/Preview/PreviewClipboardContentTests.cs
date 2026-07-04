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
    public void ForCsv_escapes_special_cells_and_preserves_extra_columns()
    {
        var model = new CsvPreviewModel
        {
            Columns =
            [
                new CsvColumn("Name", CsvColumnType.Text),
                new CsvColumn("Note", CsvColumnType.Text),
            ],
            Rows =
            [
                ["Alpha", "he said \"yes\"", "extra"],
                ["Beta", "tab\tvalue"],
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
