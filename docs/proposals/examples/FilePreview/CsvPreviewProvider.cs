// Example implementation for the file-preview proposal.
// Target location: src/Octadock.Core/Services/ — no external dependencies, unit-testable.
//
// Handles: quoted fields, embedded delimiters/newlines inside quotes, CRLF/LF,
// delimiter auto-detection (',' ';' '\t' '|' — es-ES Excel emits ';'), column
// type inference (invariant then current culture), and streaming with an
// immediate-rows window so the card opens instantly on huge files.

using System.Globalization;
using System.IO;
using System.Text;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

public sealed class CsvPreviewProvider : IFilePreviewProvider
{
    private static readonly char[] CandidateDelimiters = [',', ';', '\t', '|'];

    public bool CanPreview(string extension)
        => extension is ".csv" or ".tsv";

    public async Task<FilePreviewResult> LoadAsync(
        string path, FilePreviewOptions options, CancellationToken cancellationToken)
    {
        try
        {
            // ReadWrite|Delete share: previewing a file another process is
            // actively writing (logs, exports) must not lock it.
            await using var stream = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024, useAsync: true);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

            char delimiter = options.Delimiter
                ?? (Path.GetExtension(path).Equals(".tsv", StringComparison.OrdinalIgnoreCase)
                    ? '\t'
                    : await DetectDelimiterAsync(reader, cancellationToken).ConfigureAwait(false));

            var rows = new List<string[]>(Math.Min(options.MaxImmediateRows, 1024));
            string[]? header = null;

            await foreach (string[] record in ParseAsync(reader, delimiter, cancellationToken))
            {
                if (header is null)
                {
                    header = record;
                    continue;
                }

                rows.Add(record);
                if (rows.Count >= options.MaxImmediateRows)
                {
                    break; // Background continuation streams the rest (see FilePreviewService).
                }
            }

            if (header is null)
            {
                return FilePreviewResult.Fail(path, "The file is empty.");
            }

            List<CsvColumn> columns = InferColumns(header, rows);
            return new FilePreviewResult
            {
                Kind = FilePreviewKind.Csv,
                FilePath = path,
                Csv = new CsvPreviewModel
                {
                    Columns = columns,
                    Rows = rows,
                    Delimiter = delimiter,
                    IsPartial = rows.Count >= options.MaxImmediateRows,
                    TotalRowCount = rows.Count >= options.MaxImmediateRows ? null : rows.Count,
                },
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return FilePreviewResult.Fail(path, ex.Message);
        }
    }

    /// <summary>
    /// Scores each candidate delimiter over the first ~20 lines: a good
    /// delimiter yields the same field count (&gt;1) on every line. Never
    /// assumes ',' — es-ES Excel writes ';'.
    /// </summary>
    private static async Task<char> DetectDelimiterAsync(StreamReader reader, CancellationToken ct)
    {
        var sample = new List<string>(20);
        while (sample.Count < 20 && await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length > 0)
            {
                sample.Add(line);
            }
        }

        // Rewind for the real parse.
        reader.BaseStream.Seek(0, SeekOrigin.Begin);
        reader.DiscardBufferedData();

        char best = ',';
        double bestScore = -1;
        foreach (char candidate in CandidateDelimiters)
        {
            List<int> counts = sample
                .Select(l => CountFieldsOutsideQuotes(l, candidate))
                .ToList();
            if (counts.Count == 0 || counts[0] < 2)
            {
                continue;
            }

            // Consistency: fraction of lines matching the first line's count.
            double consistency = counts.Count(c => c == counts[0]) / (double)counts.Count;
            double score = consistency * counts[0];
            if (score > bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private static int CountFieldsOutsideQuotes(string line, char delimiter)
    {
        int fields = 1;
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == delimiter && !inQuotes)
            {
                fields++;
            }
        }

        return fields;
    }

    /// <summary>
    /// RFC-4180-style record parser: '"' quoting, "" escapes, delimiters and
    /// newlines legal inside quotes, tolerant of CRLF/LF and a missing final EOL.
    /// </summary>
    internal static async IAsyncEnumerable<string[]> ParseAsync(
        TextReader reader,
        char delimiter,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var field = new StringBuilder();
        var record = new List<string>();
        bool inQuotes = false;
        bool any = false;

        var buffer = new char[16 * 1024];
        int read;
        char prev = '\0';

        while ((read = await reader.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < read; i++)
            {
                char c = buffer[i];
                any = true;

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        inQuotes = false; // Possibly a "" escape; resolved on the next char.
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    if (prev == '"' && field.Length >= 0 && !inQuotes)
                    {
                        field.Append('"'); // "" escape inside a quoted field.
                    }

                    inQuotes = true;
                }
                else if (c == delimiter)
                {
                    record.Add(field.ToString());
                    field.Clear();
                }
                else if (c == '\n')
                {
                    record.Add(TrimTrailingCr(field));
                    field.Clear();
                    yield return record.ToArray();
                    record.Clear();
                }
                else
                {
                    field.Append(c);
                }

                prev = c;
            }
        }

        if (any && (field.Length > 0 || record.Count > 0))
        {
            record.Add(TrimTrailingCr(field));
            yield return record.ToArray();
        }
    }

    private static string TrimTrailingCr(StringBuilder field)
        => field.Length > 0 && field[^1] == '\r'
            ? field.ToString(0, field.Length - 1)
            : field.ToString();

    /// <summary>
    /// Infers a column type from up to 100 sample values. Tries invariant
    /// culture first, then the current culture (so "1.234,56" types as Number
    /// on es-ES machines).
    /// </summary>
    private static List<CsvColumn> InferColumns(string[] header, List<string[]> rows)
    {
        var columns = new List<CsvColumn>(header.Length);
        for (int col = 0; col < header.Length; col++)
        {
            var type = CsvColumnType.Text;
            int samples = 0, numbers = 0, dates = 0, bools = 0;

            foreach (string[] row in rows.Take(100))
            {
                if (col >= row.Length || string.IsNullOrWhiteSpace(row[col]))
                {
                    continue;
                }

                string v = row[col].Trim();
                samples++;
                if (double.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out _) ||
                    double.TryParse(v, NumberStyles.Any, CultureInfo.CurrentCulture, out _))
                {
                    numbers++;
                }
                else if (DateTimeOffset.TryParse(v, CultureInfo.InvariantCulture, DateTimeStyles.None, out _) ||
                         DateTimeOffset.TryParse(v, CultureInfo.CurrentCulture, DateTimeStyles.None, out _))
                {
                    dates++;
                }
                else if (bool.TryParse(v, out _))
                {
                    bools++;
                }
            }

            if (samples > 0)
            {
                if (numbers == samples)
                {
                    type = CsvColumnType.Number;
                }
                else if (dates == samples)
                {
                    type = CsvColumnType.DateTime;
                }
                else if (bools == samples)
                {
                    type = CsvColumnType.Boolean;
                }
            }

            string name = string.IsNullOrWhiteSpace(header[col]) ? $"Column {col + 1}" : header[col];
            columns.Add(new CsvColumn(name, type));
        }

        return columns;
    }
}
