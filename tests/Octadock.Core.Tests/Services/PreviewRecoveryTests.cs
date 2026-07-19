using System.Text;
using FluentAssertions;
using Octadock.Core.Abstractions;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.Core.Tests.Services;

public sealed class PreviewRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "octadock-preview-recovery-" + Guid.NewGuid().ToString("N"));

    public PreviewRecoveryTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public async Task Preview_providers_report_empty_files_without_inventing_content()
    {
        (IFilePreviewProvider Provider, string Name)[] cases =
        [
            (new TextPreviewProvider(), "empty.txt"),
            (new MarkdownPreviewProvider(), "empty.md"),
            (new JsonPreviewProvider(), "empty.json"),
            (new LogPreviewProvider(), "empty.log"),
            (new CsvPreviewProvider(), "empty.csv"),
        ];

        foreach ((IFilePreviewProvider provider, string name) in cases)
        {
            string path = WriteBytes(name, []);

            FilePreviewResult result = await provider.LoadAsync(
                path,
                new FilePreviewOptions(),
                CancellationToken.None);

            result.Kind.Should().NotBe(FilePreviewKind.Error, name);
            result.SourceByteLength.Should().Be(0, name);
            result.CopyableSourceContent.Should().BeNullOrEmpty(name);
            result.Warnings.Should().Contain(warning =>
                warning.Kind == FilePreviewWarningKind.Empty &&
                warning.Message == "0 bytes—nothing to preview", name);
        }
    }

    [Theory]
    [InlineData(TextPreviewProvider.MaxBytes, false)]
    [InlineData(TextPreviewProvider.MaxBytes + 1, true)]
    public async Task Text_preview_byte_cap_is_exact(int byteCount, bool expectedTruncated)
    {
        string path = WriteBytes("bounded.txt", Enumerable.Repeat((byte)'a', byteCount).ToArray());

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.SourceByteLength.Should().Be(byteCount);
        result.SourceContent.Should().HaveLength(Math.Min(byteCount, TextPreviewProvider.MaxBytes));
        result.Scope!.IsTruncated.Should().Be(expectedTruncated);
        result.Warnings.Any(warning => warning.Kind == FilePreviewWarningKind.Truncated)
            .Should().Be(expectedTruncated);
    }

    [Fact]
    public async Task Text_preview_preserves_code_point_at_head_boundary()
    {
        string prefix = new('a', TextPreviewProvider.MaxBytes - 1);
        string path = WriteBytes("boundary.txt", Encoding.UTF8.GetBytes(prefix + "😀tail"));

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().Be(prefix);
        result.SourceContent.Should().NotContain("�");
        result.Scope!.IsTruncated.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(SupportedEncodingCases))]
    public async Task Text_preview_detects_supported_encodings(
        string name,
        byte[] bytes,
        PreviewEncodingKind expectedKind,
        bool expectedBom)
    {
        string path = WriteBytes(name, bytes);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().Be("café 😀");
        result.DetectedEncoding!.Kind.Should().Be(expectedKind);
        result.DetectedEncoding.HasByteOrderMark.Should().Be(expectedBom);
    }

    public static IEnumerable<object[]> SupportedEncodingCases()
    {
        const string source = "café 😀";
        yield return ["utf8.txt", Encoding.UTF8.GetBytes(source), PreviewEncodingKind.Utf8, false];
        yield return
        [
            "utf8-bom.txt",
            (byte[])[.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(source)],
            PreviewEncodingKind.Utf8,
            true,
        ];

        var utf16Le = new UnicodeEncoding(false, true, true);
        yield return
        [
            "utf16-le.txt",
            (byte[])[.. utf16Le.GetPreamble(), .. utf16Le.GetBytes(source)],
            PreviewEncodingKind.Utf16LittleEndian,
            true,
        ];

        var utf16Be = new UnicodeEncoding(true, true, true);
        yield return
        [
            "utf16-be.txt",
            (byte[])[.. utf16Be.GetPreamble(), .. utf16Be.GetBytes(source)],
            PreviewEncodingKind.Utf16BigEndian,
            true,
        ];
    }

    [Fact]
    public async Task Utf8_probe_allows_a_valid_code_point_crossing_the_probe_boundary()
    {
        byte[] prefix = Enumerable.Repeat((byte)'a', (64 * 1024) - 1).ToArray();
        string path = WriteBytes(
            "probe-boundary.txt",
            [.. prefix, .. Encoding.UTF8.GetBytes("€tail")]);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().EndWith("€tail");
    }

    [Fact]
    public async Task Invalid_utf8_crossing_the_head_cap_is_not_trimmed_as_valid_truncation()
    {
        byte[] prefix = Enumerable.Repeat((byte)'a', TextPreviewProvider.MaxBytes - 1).ToArray();
        string path = WriteBytes("invalid-boundary.txt", [.. prefix, 0xE2, 0x28, 0xA1]);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.UnsupportedEncoding);
    }

    [Fact]
    public async Task Invalid_utf8_is_rejected_without_replacement_characters()
    {
        string path = WriteBytes("invalid.txt", [0x66, 0x6F, 0x80, 0x6F]);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.UnsupportedEncoding);
        result.CopyableSourceContent.Should().BeNull();
        result.Error.Should().Be(FilePreviewFailureCopy.For(FilePreviewFailureKind.UnsupportedEncoding));
    }

    [Fact]
    public async Task Binary_data_renamed_txt_is_rejected()
    {
        byte[] bytes = Enumerable.Range(0, 128).Select(index => (byte)(index % 8)).ToArray();
        string path = WriteBytes("binary.txt", bytes);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.UnsupportedEncoding);
    }

    [Fact]
    public async Task Valid_utf8_ansi_controls_remain_previewable_source()
    {
        string ansi = string.Concat(Enumerable.Repeat("\u001b[31mred\u001b[0m ", 8));
        string path = WriteText("ansi.log", ansi);

        FilePreviewResult result = await new LogPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().Be(ansi);
        result.Failure.Should().BeNull();
    }

    [Fact]
    public async Task Malformed_json_with_raw_control_preserves_exact_source()
    {
        const string source = "{\"value\":\"before\u001bafter\"}";
        string path = WriteText("raw-control.json", source);

        FilePreviewResult result = await new JsonPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Malformed);
        result.SourceContent.Should().Be(source);
        result.CopyableSourceContent.Should().Be(source);
    }

    [Fact]
    public async Task Large_utf8_log_tail_starts_on_a_code_point_boundary()
    {
        byte[] opening = Encoding.UTF8.GetBytes("x😀");
        byte[] ending = Encoding.UTF8.GetBytes("first partial line\nlast line ✅\n");
        int fillerLength = LogPreviewProvider.MaxBytes - 2 - ending.Length;
        byte[] filler = Enumerable.Repeat((byte)'a', fillerLength).ToArray();
        string path = WriteBytes("boundary.log", [.. opening, .. filler, .. ending]);

        FilePreviewResult result = await new LogPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().EndWith("last line ✅\n");
        result.SourceContent.Should().NotContain("�");
        result.Scope!.IsTruncated.Should().BeTrue();
    }

    [Fact]
    public async Task Large_log_tail_keeps_first_line_when_window_begins_after_newline()
    {
        const string firstVisibleLine = "keep this complete first line\n";
        string visibleTail = firstVisibleLine +
            new string('x', LogPreviewProvider.MaxBytes - firstVisibleLine.Length);
        string path = WriteText("line-boundary.log", "discarded\n" + visibleTail);

        FilePreviewResult result = await new LogPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().StartWith(firstVisibleLine);
        result.SourceContent.Should().HaveLength(LogPreviewProvider.MaxBytes);
    }

    [Fact]
    public async Task Same_length_in_place_rewrite_is_reported_as_changed()
    {
        string path = WriteText("same-length.txt", "before");
        await using PreviewTextSession session = await PreviewTextReader.OpenSessionAsync(
            path,
            CancellationToken.None);

        await File.WriteAllTextAsync(path, "after!", new UTF8Encoding(false, true));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(2));

        Action verify = session.VerifyUnchanged;
        verify.Should().Throw<PreviewTextReadException>()
            .Which.FailureKind.Should().Be(FilePreviewFailureKind.Changed);
    }

    [Fact]
    public async Task Invalid_utf8_at_the_log_tail_boundary_is_not_silently_skipped()
    {
        const int prefixLength = 70_000;
        byte[] prefix = Enumerable.Repeat((byte)'a', prefixLength).ToArray();
        byte[] tail = Enumerable.Repeat((byte)'b', LogPreviewProvider.MaxBytes - 1).ToArray();
        string path = WriteBytes("invalid-tail.log", [.. prefix, 0x80, .. tail]);

        FilePreviewResult result = await new LogPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.UnsupportedEncoding);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Utf16_log_tail_validates_a_surrogate_pair_crossing_the_boundary(bool bigEndian)
    {
        var encoding = new UnicodeEncoding(bigEndian, true, true);
        byte[] opening = encoding.GetBytes("x😀");
        string marker = "partial\nlast 😀\n";
        byte[] ending = encoding.GetBytes(marker);
        int fillerBytes = LogPreviewProvider.MaxBytes - 2 - ending.Length;
        byte[] filler = encoding.GetBytes(new string('a', fillerBytes / 2));
        byte[] payload = [.. opening, .. filler, .. ending];
        string path = WriteBytes(
            bigEndian ? "utf16-be.log" : "utf16-le.log",
            [.. encoding.GetPreamble(), .. payload]);

        FilePreviewResult result = await new LogPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.PlainText);
        result.SourceContent.Should().EndWith("last 😀\n");
        result.SourceContent.Should().NotContain("�");
    }

    [Theory]
    [InlineData(499, false)]
    [InlineData(500, false)]
    [InlineData(501, true)]
    public async Task Csv_sampling_probes_beyond_the_visible_cap(int dataRows, bool expectedSampled)
    {
        string path = WriteText("rows.csv", CsvRows(dataRows));

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Csv!.Rows.Should().HaveCount(Math.Min(dataRows, 500));
        result.Csv.IsSampled.Should().Be(expectedSampled);
        result.Scope!.IsSampled.Should().Be(expectedSampled);
        result.Warnings.Any(warning => warning.Kind == FilePreviewWarningKind.Sampled)
            .Should().Be(expectedSampled);
        result.Csv.TotalRowCount.Should().Be(expectedSampled ? null : dataRows);
        if (expectedSampled)
        {
            result.Warnings.Single(warning => warning.Kind == FilePreviewWarningKind.Sampled)
                .Message.Should().Contain("Filter, sort, statistics, and copy use this sample");
        }
    }

    [Fact]
    public async Task Csv_supports_quoted_multiline_records()
    {
        string path = WriteText("multiline.csv", "message,count\r\n\"hello\r\nworld\",2\r\nplain,3\r\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Csv);
        result.Csv!.Rows.Should().HaveCount(2);
        result.Csv.Rows[0][0].Should().Be("hello\r\nworld");
        result.Csv.Rows[0][1].Should().Be("2");
    }

    [Fact]
    public async Task Utf8_bom_csv_header_does_not_contain_the_preamble_character()
    {
        byte[] content = Encoding.UTF8.GetBytes("name,value\nalpha,1\n");
        string path = WriteBytes("bom.csv", [.. Encoding.UTF8.GetPreamble(), .. content]);

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Csv!.Columns[0].Name.Should().Be("name");
        result.Csv.Columns[0].Name.Should().NotStartWith("\uFEFF");
        result.DetectedEncoding!.HasByteOrderMark.Should().BeTrue();
    }

    [Fact]
    public async Task Csv_normalizes_ragged_and_extra_columns_to_one_visible_schema()
    {
        string path = WriteText("ragged.csv", "a,b\n1\n2,3,4\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Csv!.Columns.Select(column => column.Name)
            .Should().Equal("a", "b", "Column 3");
        result.Csv.Rows.Should().OnlyContain(row => row.Length == 3);
        result.Csv.Rows[0].Should().Equal("1", "", "");
        result.Csv.Rows[1].Should().Equal("2", "3", "4");
        result.Warnings.Should().Contain(warning => warning.Kind == FilePreviewWarningKind.ExtraColumnsAdded);
        result.Warnings.Should().Contain(warning => warning.Kind == FilePreviewWarningKind.RaggedRowsNormalized);
    }

    [Theory]
    [InlineData(5, 100, "header\n123456\n")]
    [InlineData(8, 8, "header\n123456789\n")]
    public async Task Csv_rejects_oversized_fields_and_records(
        int maxFieldCharacters,
        int maxRecordCharacters,
        string content)
    {
        string path = WriteText("oversized.csv", content);

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions
            {
                MaxCsvFieldCharacters = maxFieldCharacters,
                MaxCsvRecordCharacters = maxRecordCharacters,
            },
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.TooLarge);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Fact]
    public async Task Csv_crlf_does_not_count_record_separator_against_exact_field_cap()
    {
        string path = WriteText("exact-field-crlf.csv", "h\r\n12345\r\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions { MaxCsvFieldCharacters = 5 },
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Csv);
        result.Csv!.Rows.Single().Single().Should().Be("12345");
    }

    [Fact]
    public async Task Csv_crlf_does_not_count_record_separator_against_exact_record_cap()
    {
        string path = WriteText("exact-record-crlf.csv", "h\r\n12345\r\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions
            {
                MaxCsvFieldCharacters = 5,
                MaxCsvRecordCharacters = 5,
            },
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Csv);
        result.Csv!.Rows.Single().Single().Should().Be("12345");
    }

    [Fact]
    public async Task Csv_preserves_a_carriage_return_inside_a_quoted_field()
    {
        string path = WriteText("quoted-cr.csv", "h\r\n\"value\r\"\r\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Csv);
        result.Csv!.Rows.Single().Single().Should().Be("value\r");
    }

    [Fact]
    public async Task Csv_rejects_an_aggregate_sample_over_its_memory_budget()
    {
        string path = WriteText("aggregate.csv", "a,b\n12345678,1\n12345678,2\n12345678,3\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions
            {
                MaxCsvFieldCharacters = 10,
                MaxCsvRecordCharacters = 20,
                MaxCsvSampleCharacters = 20,
            },
            CancellationToken.None);

        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.TooLarge);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Fact]
    public async Task Csv_rejects_too_many_columns_with_source_length_metadata()
    {
        string header = string.Join(',', Enumerable.Range(1, 257).Select(index => $"c{index}"));
        string path = WriteText("columns.csv", header + "\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.TooLarge);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Fact]
    public async Task Csv_reports_unclosed_quotes_as_malformed_with_source_length()
    {
        string path = WriteText("malformed.csv", "a,b\n\"unterminated,2\n");

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Malformed);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Theory]
    [InlineData("a,b\r\n\"left\"oops,right\r\n")]
    [InlineData("a,b\r\n\"left\"\r,right\r\n")]
    public async Task Csv_rejects_characters_after_a_closing_quote(string content)
    {
        string path = WriteText("post-quote.csv", content);

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Malformed);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Fact]
    public async Task Csv_late_invalid_utf8_retains_source_length_metadata()
    {
        var prefix = new StringBuilder("name,value\n");
        for (int row = 0; row < 450; row++)
        {
            prefix.Append(row).Append(',').Append('a', 160).Append('\n');
        }

        byte[] valid = Encoding.UTF8.GetBytes(prefix.ToString());
        string path = WriteBytes(
            "late-invalid.csv",
            [.. valid, .. Encoding.UTF8.GetBytes("bad,"), 0xFF, (byte)'\n']);

        FilePreviewResult result = await new CsvPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.UnsupportedEncoding);
        result.SourceByteLength.Should().Be(new FileInfo(path).Length);
    }

    [Fact]
    public async Task Json_formatted_output_is_bounded_independently_from_source()
    {
        string source = "[" + string.Join(',', Enumerable.Repeat("0", 850_000)) + "]";
        string path = WriteText("expands.json", source);

        FilePreviewResult result = await new JsonPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.SourceContent.Should().Be(source);
        result.RenderedContent.Should().BeNull();
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.TooLarge);
    }

    [Fact]
    public async Task Missing_and_moved_files_have_stable_not_found_failure()
    {
        string missing = Path.Combine(_directory, "missing.txt");
        string moved = WriteText("before.txt", "content");
        File.Move(moved, Path.Combine(_directory, "after.txt"));

        foreach (string path in new[] { missing, moved })
        {
            FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
                path,
                new FilePreviewOptions(),
                CancellationToken.None);

            result.Failure!.Kind.Should().Be(FilePreviewFailureKind.NotFound);
            result.Error.Should().Be(FilePreviewFailureCopy.For(FilePreviewFailureKind.NotFound));
        }
    }

    [Fact]
    public async Task Directory_opened_as_text_has_stable_access_denied_failure()
    {
        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            _directory,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.AccessDenied);
        result.Error.Should().Be(FilePreviewFailureCopy.For(FilePreviewFailureKind.AccessDenied));
    }

    [Fact]
    public async Task Exclusively_locked_file_has_stable_busy_failure()
    {
        string path = WriteText("locked.txt", "content");
        await using var held = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        FilePreviewResult result = await new TextPreviewProvider().LoadAsync(
            path,
            new FilePreviewOptions(),
            CancellationToken.None);

        result.Kind.Should().Be(FilePreviewKind.Error);
        result.Failure!.Kind.Should().Be(FilePreviewFailureKind.Busy);
        result.Error.Should().Be(FilePreviewFailureCopy.For(FilePreviewFailureKind.Busy));
    }

    private string WriteText(string name, string content)
        => WriteBytes(name, new UTF8Encoding(false, true).GetBytes(content));

    private string WriteBytes(string name, byte[] bytes)
    {
        string path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string CsvRows(int count)
    {
        var builder = new StringBuilder("id,value\n");
        for (int index = 1; index <= count; index++)
        {
            builder.Append(index).Append(',').Append("row-").Append(index).Append('\n');
        }

        return builder.ToString();
    }
}
