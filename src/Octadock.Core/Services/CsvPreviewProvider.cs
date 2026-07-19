using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// Strict, bounded CSV/TSV sampling. The provider probes one logical record past
/// the visible cap and normalizes all shown rows to one visible schema.
/// </summary>
public sealed class CsvPreviewProvider : IFilePreviewProvider
{
    private const int DelimiterProbeCharacters = 64 * 1024;
    private const int HardMaxImmediateRows = 10_000;
    private const int HardMaxColumns = 4_096;
    private const int HardMaxFieldCharacters = 4 * 1024 * 1024;
    private const int HardMaxRecordCharacters = 16 * 1024 * 1024;
    private const int HardMaxSampleCharacters = 32 * 1024 * 1024;
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    public bool CanPreview(string extension)
        => extension is ".csv" or ".tsv";

    public async Task<FilePreviewResult> LoadAsync(
        string path,
        FilePreviewOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        long? sourceByteLength = null;

        try
        {
            await using PreviewTextSession session = await PreviewTextReader
                .OpenSessionAsync(path, cancellationToken)
                .ConfigureAwait(false);
            sourceByteLength = session.SourceByteLength;

            if (session.SourceByteLength == 0)
            {
                session.VerifyUnchanged();
                var emptyScope = new PreviewContentScope
                {
                    StartRow = 1,
                    ShownRowCount = 0,
                    TotalRowCount = 0,
                    Label = "0 data rows",
                };
                return new FilePreviewResult
                {
                    Kind = FilePreviewKind.Csv,
                    FilePath = path,
                    SourceByteLength = 0,
                    Scope = emptyScope,
                    Warnings =
                    [
                        new FilePreviewWarning(
                            FilePreviewWarningKind.Empty,
                            "0 bytes—nothing to preview"),
                    ],
                    Csv = new CsvPreviewModel
                    {
                        Columns = [],
                        Rows = [],
                        Delimiter = options.Delimiter ?? DefaultDelimiter(path),
                        TotalRowCount = 0,
                        IsPartial = false,
                    },
                };
            }

            char delimiter = options.Delimiter ?? await DetectDelimiterAsync(
                session,
                DefaultDelimiter(path),
                cancellationToken).ConfigureAwait(false);

            await using IAsyncEnumerator<string[]> records = ParseAsync(
                session.Reader,
                delimiter,
                options,
                session.SourceByteLength,
                cancellationToken).GetAsyncEnumerator(cancellationToken);

            if (!await records.MoveNextAsync().ConfigureAwait(false))
            {
                session.VerifyUnchanged();
                return EmptyDecodedFile(path, session, delimiter);
            }

            string[] header = records.Current;
            long retainedCharacters = CountCharacters(header);
            EnsureSampleBudget(retainedCharacters, options, session.SourceByteLength);
            var rows = new List<string[]>(Math.Min(options.MaxImmediateRows, 1024));
            while (rows.Count < options.MaxImmediateRows &&
                   await records.MoveNextAsync().ConfigureAwait(false))
            {
                string[] row = records.Current;
                retainedCharacters += CountCharacters(row);
                EnsureSampleBudget(retainedCharacters, options, session.SourceByteLength);
                rows.Add(row);
            }

            // The decisive cap+1 probe: exactly 500 rows is complete; 501 is sampled.
            bool hasMoreRows = await records.MoveNextAsync().ConfigureAwait(false);
            session.VerifyUnchanged();

            int schemaWidth = Math.Max(
                header.Length,
                rows.Count == 0 ? 0 : rows.Max(row => row.Length));
            if (schemaWidth > options.MaxCsvColumns)
            {
                throw new CsvPreviewException(
                    FilePreviewFailureKind.TooLarge,
                    session.SourceByteLength);
            }

            bool addedColumns = schemaWidth > header.Length;
            bool normalizedRaggedRows = rows.Any(row => row.Length != schemaWidth);
            string[] normalizedHeader = NormalizeHeader(header, schemaWidth);
            List<string[]> normalizedRows = rows
                .Select(row => NormalizeRow(row, schemaWidth))
                .ToList();
            List<CsvColumn> columns = InferColumns(normalizedHeader, normalizedRows);

            long? totalRows = hasMoreRows ? null : normalizedRows.Count;
            string scopeLabel = hasMoreRows
                ? $"First {normalizedRows.Count:N0} data rows shown"
                : $"All {normalizedRows.Count:N0} data rows shown";
            var scope = new PreviewContentScope
            {
                StartRow = 1,
                ShownRowCount = normalizedRows.Count,
                TotalRowCount = totalRows,
                IsSampled = hasMoreRows,
                Label = scopeLabel,
            };

            var warnings = new List<FilePreviewWarning>();
            if (hasMoreRows)
            {
                warnings.Add(new FilePreviewWarning(
                    FilePreviewWarningKind.Sampled,
                    $"Sample only: the first {normalizedRows.Count:N0} rows are shown. Filter, sort, statistics, and copy use this sample."));
            }

            if (addedColumns)
            {
                warnings.Add(new FilePreviewWarning(
                    FilePreviewWarningKind.ExtraColumnsAdded,
                    "Some rows contain columns beyond the header. Placeholder columns were added to the visible schema."));
            }

            if (normalizedRaggedRows)
            {
                warnings.Add(new FilePreviewWarning(
                    FilePreviewWarningKind.RaggedRowsNormalized,
                    "Rows with missing cells were padded to match the visible schema."));
            }

            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Csv,
                FilePath = path,
                SourceByteLength = session.SourceByteLength,
                DetectedEncoding = session.Encoding,
                Scope = scope,
                Warnings = warnings,
                Csv = new CsvPreviewModel
                {
                    Columns = columns,
                    Rows = normalizedRows,
                    Delimiter = delimiter,
                    IsPartial = hasMoreRows,
                    TotalRowCount = totalRows,
                },
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (CsvPreviewException ex)
        {
            return FilePreviewResult.Fail(path, ex.FailureKind, ex.SourceByteLength);
        }
        catch (DecoderFallbackException)
        {
            return FilePreviewResult.Fail(
                path,
                FilePreviewFailureKind.UnsupportedEncoding,
                sourceByteLength);
        }
        catch (Exception ex)
        {
            return PreviewFailureMapper.FromException(path, ex);
        }
    }

    private static FilePreviewResult EmptyDecodedFile(
        string path,
        PreviewTextSession session,
        char delimiter)
    {
        var scope = new PreviewContentScope
        {
            StartRow = 1,
            ShownRowCount = 0,
            TotalRowCount = 0,
            Label = "0 data rows",
        };
        return new FilePreviewResult
        {
            Kind = FilePreviewKind.Csv,
            FilePath = path,
            SourceByteLength = session.SourceByteLength,
            DetectedEncoding = session.Encoding,
            Scope = scope,
            Warnings =
            [
                new FilePreviewWarning(FilePreviewWarningKind.Empty, "No CSV records were found."),
            ],
            Csv = new CsvPreviewModel
            {
                Columns = [],
                Rows = [],
                Delimiter = delimiter,
                IsPartial = false,
                TotalRowCount = 0,
            },
        };
    }

    private static async Task<char> DetectDelimiterAsync(
        PreviewTextSession session,
        char fallback,
        CancellationToken cancellationToken)
    {
        char[] buffer = new char[DelimiterProbeCharacters];
        int read = 0;
        while (read < buffer.Length)
        {
            int count = await session.Reader
                .ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)
                .ConfigureAwait(false);
            if (count == 0)
            {
                break;
            }

            read += count;
        }

        session.Reset();
        return SelectDelimiter(new string(buffer, 0, read), fallback);
    }

    private static char SelectDelimiter(string sample, char fallback)
    {
        char best = fallback;
        double bestScore = -1;
        foreach (char candidate in CandidateDelimiters)
        {
            IReadOnlyList<int> counts = CountLogicalRecordFields(sample, candidate, maxRecords: 20);
            if (counts.Count == 0 || counts[0] < 2)
            {
                continue;
            }

            int mode = counts
                .GroupBy(value => value)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Key)
                .First().Key;
            double consistency = counts.Count(value => value == mode) / (double)counts.Count;
            double score = consistency * mode;
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static List<int> CountLogicalRecordFields(
        string sample,
        char delimiter,
        int maxRecords)
    {
        var counts = new List<int>(maxRecords);
        int fields = 1;
        bool inQuotes = false;
        for (int index = 0; index < sample.Length && counts.Count < maxRecords; index++)
        {
            char character = sample[index];
            if (inQuotes)
            {
                if (character == '"')
                {
                    if (index + 1 < sample.Length && sample[index + 1] == '"')
                    {
                        index++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }

                continue;
            }

            if (character == '"')
            {
                inQuotes = true;
            }
            else if (character == delimiter)
            {
                fields++;
            }
            else if (character == '\n')
            {
                counts.Add(fields);
                fields = 1;
            }
        }

        if (!inQuotes && fields > 1 && counts.Count < maxRecords)
        {
            counts.Add(fields);
        }

        return counts;
    }

    internal static async IAsyncEnumerable<string[]> ParseAsync(
        TextReader reader,
        char delimiter,
        FilePreviewOptions options,
        long sourceByteLength,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var field = new StringBuilder();
        var record = new List<string>();
        bool inQuotes = false;
        bool sawClosingQuote = false;
        bool recordAwaitingLineFeed = false;
        bool fieldStarted = false;
        int recordCharacters = 0;
        char[] buffer = new char[16 * 1024];

        int read;
        while ((read = await reader.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (int index = 0; index < read; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                char character = buffer[index];

                if (recordAwaitingLineFeed)
                {
                    recordAwaitingLineFeed = false;
                    if (character != '\n')
                    {
                        throw new CsvPreviewException(FilePreviewFailureKind.Malformed, sourceByteLength);
                    }

                    sawClosingQuote = false;
                    AddField(record, field, options.MaxCsvColumns, sourceByteLength);
                    fieldStarted = false;
                    recordCharacters = 0;
                    yield return record.ToArray();
                    record.Clear();
                    continue;
                }

                // Record terminators are framing, not record content. Embedded
                // CR/LF inside a quoted field still count toward the bound.
                if (inQuotes || character is not '\r' and not '\n')
                {
                    recordCharacters++;
                    if (recordCharacters > options.MaxCsvRecordCharacters)
                    {
                        throw new CsvPreviewException(FilePreviewFailureKind.TooLarge, sourceByteLength);
                    }
                }

                if (inQuotes)
                {
                    if (character == '"')
                    {
                        inQuotes = false;
                        sawClosingQuote = true;
                    }
                    else
                    {
                        AppendBounded(field, character, options.MaxCsvFieldCharacters, sourceByteLength);
                    }

                    continue;
                }

                if (sawClosingQuote)
                {
                    if (character == '"')
                    {
                        sawClosingQuote = false;
                        AppendBounded(field, '"', options.MaxCsvFieldCharacters, sourceByteLength);
                        inQuotes = true;
                        continue;
                    }

                    if (character == delimiter)
                    {
                        sawClosingQuote = false;
                        AddField(record, field, options.MaxCsvColumns, sourceByteLength);
                        fieldStarted = true;
                        continue;
                    }

                    if (character == '\n')
                    {
                        sawClosingQuote = false;
                        AddField(record, field, options.MaxCsvColumns, sourceByteLength);
                        fieldStarted = false;
                        recordCharacters = 0;
                        yield return record.ToArray();
                        record.Clear();
                        continue;
                    }

                    // Permit CR only as the first half of CRLF. Keeping the
                    // closing-quote state makes any following non-LF character
                    // malformed instead of silently joining it to the field.
                    if (character == '\r')
                    {
                        recordAwaitingLineFeed = true;
                        continue;
                    }

                    throw new CsvPreviewException(FilePreviewFailureKind.Malformed, sourceByteLength);
                }

                if (character == '"' && field.Length == 0)
                {
                    inQuotes = true;
                    fieldStarted = true;
                }
                else if (character == '"')
                {
                    throw new CsvPreviewException(FilePreviewFailureKind.Malformed, sourceByteLength);
                }
                else if (character == delimiter)
                {
                    AddField(record, field, options.MaxCsvColumns, sourceByteLength);
                    fieldStarted = true;
                }
                else if (character == '\n')
                {
                    AddField(record, field, options.MaxCsvColumns, sourceByteLength);
                    fieldStarted = false;
                    recordCharacters = 0;
                    yield return record.ToArray();
                    record.Clear();
                }
                else if (character == '\r')
                {
                    recordAwaitingLineFeed = true;
                }
                else
                {
                    AppendBounded(field, character, options.MaxCsvFieldCharacters, sourceByteLength);
                    fieldStarted = true;
                }
            }
        }

        if (inQuotes || recordAwaitingLineFeed)
        {
            throw new CsvPreviewException(FilePreviewFailureKind.Malformed, sourceByteLength);
        }

        if (sawClosingQuote || fieldStarted || field.Length > 0 || record.Count > 0)
        {
            AddField(record, field, options.MaxCsvColumns, sourceByteLength);
            yield return record.ToArray();
        }
    }

    private static void AppendBounded(
        StringBuilder field,
        char character,
        int maxCharacters,
        long sourceByteLength)
    {
        if (field.Length >= maxCharacters)
        {
            throw new CsvPreviewException(FilePreviewFailureKind.TooLarge, sourceByteLength);
        }

        field.Append(character);
    }

    private static void AddField(
        List<string> record,
        StringBuilder field,
        int maxColumns,
        long sourceByteLength)
    {
        if (record.Count >= maxColumns)
        {
            throw new CsvPreviewException(FilePreviewFailureKind.TooLarge, sourceByteLength);
        }

        record.Add(field.ToString());
        field.Clear();
    }

    private static string[] NormalizeHeader(string[] header, int width)
    {
        var normalized = new string[width];
        for (int index = 0; index < width; index++)
        {
            normalized[index] = index < header.Length && !string.IsNullOrWhiteSpace(header[index])
                ? header[index]
                : $"Column {index + 1}";
        }

        return normalized;
    }

    private static string[] NormalizeRow(string[] row, int width)
    {
        var normalized = new string[width];
        int copy = Math.Min(width, row.Length);
        Array.Copy(row, normalized, copy);
        for (int index = copy; index < width; index++)
        {
            normalized[index] = string.Empty;
        }

        return normalized;
    }

    private static List<CsvColumn> InferColumns(string[] header, List<string[]> rows)
    {
        var columns = new List<CsvColumn>(header.Length);
        for (int column = 0; column < header.Length; column++)
        {
            CsvColumnType type = CsvColumnType.Text;
            int samples = 0;
            int numbers = 0;
            int dates = 0;
            int booleans = 0;

            foreach (string[] row in rows.Take(100))
            {
                if (string.IsNullOrWhiteSpace(row[column]))
                {
                    continue;
                }

                string value = row[column].Trim();
                samples++;
                if (double.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                    double.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
                {
                    numbers++;
                }
                else if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                         DateTimeOffset.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                {
                    dates++;
                }
                else if (bool.TryParse(value, out _))
                {
                    booleans++;
                }
            }

            if (samples > 0)
            {
                type = numbers == samples
                    ? CsvColumnType.Number
                    : dates == samples
                        ? CsvColumnType.DateTime
                        : booleans == samples
                            ? CsvColumnType.Boolean
                            : CsvColumnType.Text;
            }

            columns.Add(new CsvColumn(header[column], type));
        }

        return columns;
    }

    private static char DefaultDelimiter(string path)
        => Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase) ? '\t' : ',';

    private static void ValidateOptions(FilePreviewOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxImmediateRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxTotalRows);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCsvColumns);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCsvFieldCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCsvRecordCharacters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCsvSampleCharacters);
        if (options.MaxImmediateRows > HardMaxImmediateRows ||
            options.MaxImmediateRows > options.MaxTotalRows)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                $"Immediate CSV rows must not exceed {HardMaxImmediateRows:N0} or MaxTotalRows.");
        }

        if (options.MaxCsvColumns > HardMaxColumns ||
            options.MaxCsvFieldCharacters > HardMaxFieldCharacters ||
            options.MaxCsvRecordCharacters > HardMaxRecordCharacters ||
            options.MaxCsvSampleCharacters > HardMaxSampleCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CSV safety limits exceed Octadock's hard preview bounds.");
        }

        if (options.MaxCsvFieldCharacters > options.MaxCsvRecordCharacters ||
            options.MaxCsvRecordCharacters > options.MaxCsvSampleCharacters)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "CSV field, record, and retained-sample limits must be ordered from smallest to largest.");
        }
    }

    private static long CountCharacters(IEnumerable<string> fields)
        => fields.Sum(field => (long)field.Length);

    private static void EnsureSampleBudget(
        long retainedCharacters,
        FilePreviewOptions options,
        long sourceByteLength)
    {
        if (retainedCharacters > options.MaxCsvSampleCharacters)
        {
            throw new CsvPreviewException(FilePreviewFailureKind.TooLarge, sourceByteLength);
        }
    }

    private sealed class CsvPreviewException : IOException
    {
        public CsvPreviewException(FilePreviewFailureKind failureKind, long? sourceByteLength)
            : base(FilePreviewFailureCopy.For(failureKind))
        {
            FailureKind = failureKind;
            SourceByteLength = sourceByteLength;
        }

        public FilePreviewFailureKind FailureKind { get; }

        public long? SourceByteLength { get; }
    }
}
