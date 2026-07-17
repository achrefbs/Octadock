using System.Globalization;
using System.IO;
using Octadock.Core.Abstractions;

namespace Octadock.App.Preview;

/// <summary>
/// Builds the file metadata shown by the preview's optional inspector rail.
/// Kept independent from WPF so the content contract can be tested directly.
/// </summary>
internal static class PreviewInspectorModel
{
    public static IReadOnlyList<PreviewInspectorRow> BuildRows(FilePreviewResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var rows = new List<PreviewInspectorRow>();
        string name = Path.GetFileName(result.FilePath);
        rows.Add(new PreviewInspectorRow(
            "Name",
            string.IsNullOrWhiteSpace(name) ? result.FilePath : name,
            Wrap: true));

        string extension = Path.GetExtension(result.FilePath).TrimStart('.').ToUpperInvariant();
        rows.Add(new PreviewInspectorRow(
            "Type",
            string.IsNullOrWhiteSpace(extension) ? result.Kind.ToString() : extension));


        switch (result.Kind)
        {
            case FilePreviewKind.Image:
                if (result.ImagePixelWidth is > 0 && result.ImagePixelHeight is > 0)
                {
                    rows.Add(new PreviewInspectorRow(
                        "Dimensions",
                        $"{result.ImagePixelWidth.Value} × {result.ImagePixelHeight.Value}"));
                }

                rows.Add(new PreviewInspectorRow("Preview", "Image"));
                break;

            case FilePreviewKind.Csv when result.Csv is not null:
                rows.Add(new PreviewInspectorRow(
                    "Columns",
                    result.Csv.Columns.Count.ToString(CultureInfo.InvariantCulture)));
                rows.Add(new PreviewInspectorRow(
                    "Rows",
                    (result.Csv.TotalRowCount ?? result.Csv.Rows.Count).ToString(CultureInfo.InvariantCulture)));
                rows.Add(new PreviewInspectorRow("Delimiter", result.Csv.Delimiter.ToString()));
                break;

            case FilePreviewKind.PlainText:
            case FilePreviewKind.Markdown:
                if (result.Text is not null)
                {
                    rows.Add(new PreviewInspectorRow(
                        "Lines",
                        CountTextLines(result.Text).ToString(CultureInfo.InvariantCulture)));
                    rows.Add(new PreviewInspectorRow(
                        "Characters",
                        result.Text.Length.ToString(CultureInfo.InvariantCulture)));
                }

                rows.Add(new PreviewInspectorRow(
                    "Preview",
                    result.Kind == FilePreviewKind.Markdown ? "Markdown" : "Text"));
                break;

            case FilePreviewKind.FileInfo:
                rows.Add(new PreviewInspectorRow("Preview", "Fallback"));
                break;

            case FilePreviewKind.Error:
                rows.Add(new PreviewInspectorRow("Preview", "Failed"));
                break;
        }

        rows.Add(new PreviewInspectorRow("Path", result.FilePath, Wrap: true));
        return rows;
    }

    private static int CountTextLines(string text)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        int count = 1;
        foreach (char character in text)
        {
            if (character == '\n')
            {
                count++;
            }
        }

        return count;
    }
}

internal sealed record PreviewInspectorRow(string Label, string Value, bool Wrap = false);
