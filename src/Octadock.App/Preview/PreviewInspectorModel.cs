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

        if (result.SourceByteLength is long sourceBytes)
        {
            rows.Add(new PreviewInspectorRow(
                "Size",
                sourceBytes == 1 ? "1 byte" : $"{sourceBytes:N0} bytes"));
        }

        if (result.DetectedEncoding is { } encoding)
        {
            rows.Add(new PreviewInspectorRow("Encoding", encoding.DisplayName));
        }


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
                    result.Scope?.IsSampled == true
                        ? result.Scope.Label ?? $"First {result.Csv.Rows.Count:N0} shown"
                        : (result.Csv.TotalRowCount ?? result.Csv.Rows.Count)
                            .ToString(CultureInfo.InvariantCulture)));
                rows.Add(new PreviewInspectorRow("Delimiter", result.Csv.Delimiter.ToString()));
                break;

            case FilePreviewKind.PlainText:
            case FilePreviewKind.Markdown:
                string? source = result.SourceContent ?? result.Text;
                if (source is not null)
                {
                    rows.Add(new PreviewInspectorRow(
                        "Lines",
                        CountTextLines(source).ToString(CultureInfo.InvariantCulture)));
                    rows.Add(new PreviewInspectorRow(
                        "Characters",
                        source.Length.ToString(CultureInfo.InvariantCulture)));
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

            case FilePreviewKind.Loading:
                rows.Add(new PreviewInspectorRow("Preview", "Loading"));
                break;
        }

        if (result.Scope?.Label is { Length: > 0 } scopeLabel && result.Kind != FilePreviewKind.Csv)
        {
            rows.Add(new PreviewInspectorRow("Shown", scopeLabel, Wrap: true));
        }

        if (result.Failure is { } failure)
        {
            rows.Add(new PreviewInspectorRow("Status", failure.Kind.ToString()));
        }

        if (result.Warnings.Count > 0)
        {
            rows.Add(new PreviewInspectorRow(
                "Notice",
                string.Join(" ", result.Warnings.Select(warning => warning.Message)),
                Wrap: true));
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
