using System.Text;
using Octadock.Core.Abstractions;

namespace Octadock.App.Preview;

/// <summary>Formats preview payloads for clipboard-friendly copy actions.</summary>
internal static class PreviewClipboardContent
{
    public static string? ForResult(FilePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Kind switch
        {
            FilePreviewKind.PlainText or FilePreviewKind.FileInfo or FilePreviewKind.Markdown
                when !string.IsNullOrEmpty(result.Text) => result.Text,
            FilePreviewKind.Csv when result.Csv is not null => ForCsv(result.Csv),
            _ => null,
        };
    }

    public static string ForCsv(CsvPreviewModel model, IEnumerable<string[]>? rows = null)
    {
        ArgumentNullException.ThrowIfNull(model);

        List<string[]> materializedRows = (rows ?? model.Rows).ToList();
        int width = model.Columns.Count;
        foreach (string[] row in materializedRows)
        {
            width = Math.Max(width, row.Length);
        }

        var builder = new StringBuilder();
        AppendRow(
            builder,
            Enumerable.Range(0, width).Select(index =>
                index < model.Columns.Count ? model.Columns[index].Name : $"Column {index + 1}"));

        foreach (string[] row in materializedRows)
        {
            AppendRow(
                builder,
                Enumerable.Range(0, width).Select(index => index < row.Length ? row[index] : string.Empty));
        }

        return builder.ToString();
    }

    private static void AppendRow(StringBuilder builder, IEnumerable<string?> cells)
    {
        bool first = true;
        foreach (string? cell in cells)
        {
            if (!first)
            {
                builder.Append('\t');
            }

            builder.Append(EscapeCell(cell));
            first = false;
        }

        builder.AppendLine();
    }

    private static string EscapeCell(string? cell)
    {
        if (string.IsNullOrEmpty(cell))
        {
            return string.Empty;
        }

        return cell.IndexOfAny(['\t', '\r', '\n', '"']) < 0
            ? cell
            : $"\"{cell.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
