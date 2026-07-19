using System.Windows;
using FluentAssertions;
using Octadock.App.Preview;
using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class PreviewCardWindowTests
{
    [Fact]
    public void CalculateFittedImageHostSize_uses_visible_viewport_minus_image_margin()
    {
        Size size = PreviewCardWindow.CalculateFittedImageHostSize(
            viewportWidth: 900,
            viewportHeight: 620,
            fallbackWidth: 1200,
            fallbackHeight: 800,
            margin: new Thickness(14, 10, 14, 12));

        size.Width.Should().Be(872);
        size.Height.Should().Be(598);
    }

    [Fact]
    public void CalculateFittedImageHostSize_falls_back_before_scroll_viewer_reports_viewport()
    {
        Size size = PreviewCardWindow.CalculateFittedImageHostSize(
            viewportWidth: 0,
            viewportHeight: double.NaN,
            fallbackWidth: 640,
            fallbackHeight: 480,
            margin: new Thickness(20));

        size.Width.Should().Be(600);
        size.Height.Should().Be(440);
    }

    [Fact]
    public void CalculatePreviewPhysicalBounds_places_quick_look_near_top_of_negative_monitor()
    {
        var workArea = new PixelRect(-1920, 0, 1920, 1080);

        PixelRect bounds = PreviewCardWindow.CalculatePreviewPhysicalBounds(workArea, dpiScale: 1.0);

        bounds.Should().Be(new PixelRect(-1450, 40, 980, 640));
        workArea.Contains(bounds).Should().BeTrue();
    }

    [Fact]
    public void CalculatePreviewPhysicalBounds_scales_and_stays_inside_monitor()
    {
        var workArea = new PixelRect(0, 0, 2560, 1440);

        PixelRect bounds = PreviewCardWindow.CalculatePreviewPhysicalBounds(workArea, dpiScale: 1.75);

        bounds.Should().Be(new PixelRect(537, 70, 1485, 893));
        workArea.Contains(bounds).Should().BeTrue();
    }

    [Fact]
    public void CalculatePreviewPhysicalBounds_remains_usable_at_400_percent_scaling()
    {
        var workArea = new PixelRect(0, 0, 3840, 2160);

        PixelRect bounds = PreviewCardWindow.CalculatePreviewPhysicalBounds(workArea, dpiScale: 4.0);

        workArea.Contains(bounds).Should().BeTrue();
        bounds.Width.Should().BeGreaterThan(0);
        bounds.Height.Should().BeGreaterThan(0);
    }

    [Fact]
    public void High_contrast_palette_uses_system_colors_for_every_interactive_surface()
    {
        PreviewCardPalette palette = PreviewCardWindow.CreatePreviewPalette(highContrast: true);

        palette.CardBackground.Should().BeSameAs(SystemColors.WindowBrush);
        palette.Text.Should().BeSameAs(SystemColors.WindowTextBrush);
        palette.GlassBorder.Should().BeSameAs(SystemColors.WindowTextBrush);
        palette.Accent.Should().BeSameAs(SystemColors.HighlightBrush);
        palette.RowSelected.Should().BeSameAs(SystemColors.HighlightBrush);
        palette.ScrollThumb.Should().BeSameAs(SystemColors.WindowTextBrush);
        palette.ColumnHeaderText.Should().BeSameAs(SystemColors.WindowTextBrush);
    }

    [Fact]
    public void Context_selection_transition_keeps_preview_until_user_returns()
    {
        var guard = new PreviewContextSelectionCloseGuard();

        guard.BeginSelection();

        guard.ShouldClose(transientSuppression: false, uiAudit: false).Should().BeFalse();
        guard.OnActivated();
        guard.ShouldClose(transientSuppression: false, uiAudit: false).Should().BeTrue();
    }

    [Fact]
    public void Context_selection_guard_clears_after_a_transiently_suppressed_transition_returns()
    {
        var guard = new PreviewContextSelectionCloseGuard();
        guard.BeginSelection();

        guard.ShouldClose(transientSuppression: true, uiAudit: false).Should().BeFalse();
        guard.OnActivated();

        guard.ShouldClose(transientSuppression: false, uiAudit: false).Should().BeTrue();
    }

    [Fact]
    public void CalculatePreviewPhysicalBounds_clamps_to_small_work_area()
    {
        var workArea = new PixelRect(1600, -300, 500, 300);

        PixelRect bounds = PreviewCardWindow.CalculatePreviewPhysicalBounds(workArea, dpiScale: 1.25);

        workArea.Contains(bounds).Should().BeTrue();
        bounds.Width.Should().Be(470);
        bounds.Height.Should().Be(270);
    }

    [Fact]
    public void CalculatePreviewMinimumSize_never_exceeds_clamped_small_monitor_bounds()
    {
        var physicalBounds = new PixelRect(1615, -270, 470, 270);

        Size minimum = PreviewCardWindow.CalculatePreviewMinimumSize(physicalBounds, dpiScale: 1.25);

        minimum.Should().Be(new Size(376, 216));
        minimum.Width.Should().BeLessThanOrEqualTo(physicalBounds.Width / 1.25);
        minimum.Height.Should().BeLessThanOrEqualTo(physicalBounds.Height / 1.25);
    }

    [Fact]
    public void CalculatePreviewMinimumSize_restores_normal_minima_on_large_monitor()
    {
        var physicalBounds = new PixelRect(100, 70, 1485, 893);

        Size minimum = PreviewCardWindow.CalculatePreviewMinimumSize(physicalBounds, dpiScale: 1.75);

        minimum.Should().Be(new Size(680, 360));
    }

    [Fact]
    public void PreviewInspectorModel_includes_true_source_image_dimensions_and_wrapped_path()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.Image,
            FilePath = @"C:\shots\capture.png",
            ImagePath = @"C:\shots\capture.png",
            ImagePixelWidth = 4800,
            ImagePixelHeight = 2700,
        };

        IReadOnlyList<PreviewInspectorRow> rows = PreviewInspectorModel.BuildRows(result);

        rows.Should().Contain(row => row.Label == "Name" && row.Value == "capture.png");
        rows.Should().Contain(row => row.Label == "Dimensions" && row.Value == "4800 × 2700");
        rows.Should().Contain(row => row.Label == "Preview" && row.Value == "Image");
        rows.Should().Contain(row => row.Label == "Path" && row.Wrap);
    }

    [Fact]
    public void PreviewInspectorModel_includes_csv_shape()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.Csv,
            FilePath = @"C:\data\sales.csv",
            Csv = new CsvPreviewModel
            {
                Columns =
                [
                    new CsvColumn("date", CsvColumnType.DateTime),
                    new CsvColumn("revenue", CsvColumnType.Number),
                ],
                Rows =
                [
                    ["2026-07-08", "120"],
                    ["2026-07-09", "140"],
                ],
                TotalRowCount = 42,
                Delimiter = ',',
            },
        };

        IReadOnlyList<PreviewInspectorRow> rows = PreviewInspectorModel.BuildRows(result);

        rows.Should().Contain(row => row.Label == "Columns" && row.Value == "2");
        rows.Should().Contain(row => row.Label == "Rows" && row.Value == "42");
        rows.Should().Contain(row => row.Label == "Delimiter" && row.Value == ",");
    }

    [Fact]
    public void PreviewInspectorModel_labels_csv_rows_as_a_sample()
    {
        var result = new FilePreviewResult
        {
            Kind = FilePreviewKind.Csv,
            FilePath = @"C:\data\large.csv",
            Scope = new PreviewContentScope
            {
                StartRow = 1,
                ShownRowCount = 500,
                IsSampled = true,
                Label = "First 500 data rows shown",
            },
            Csv = new CsvPreviewModel
            {
                Columns = [new CsvColumn("id", CsvColumnType.Number)],
                Rows = Enumerable.Range(1, 500).Select(index => new[] { index.ToString() }).ToList(),
                IsPartial = true,
                TotalRowCount = null,
                Delimiter = ',',
            },
        };

        IReadOnlyList<PreviewInspectorRow> rows = PreviewInspectorModel.BuildRows(result);

        rows.Should().Contain(row => row.Label == "Rows" && row.Value == "First 500 data rows shown");
    }

    [Fact]
    public void PreviewInspectorModel_does_not_read_file_metadata_from_path()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "preview metadata probe");
            File.SetLastWriteTime(path, new DateTime(2026, 7, 17, 12, 34, 0));
            var result = new FilePreviewResult
            {
                Kind = FilePreviewKind.PlainText,
                FilePath = path,
                Text = "preview metadata probe",
            };

            IReadOnlyList<PreviewInspectorRow> rows = PreviewInspectorModel.BuildRows(result);

            rows.Should().NotContain(row => row.Label == "Size" || row.Label == "Modified");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetPreviewInspectorChrome_keeps_toolbar_label_and_visibility_in_sync()
    {
        PreviewInspectorChrome visible = PreviewCardWindow.GetPreviewInspectorChrome(visible: true);
        PreviewInspectorChrome hidden = PreviewCardWindow.GetPreviewInspectorChrome(visible: false);

        visible.Visibility.Should().Be(Visibility.Visible);
        visible.ToolTip.Should().Be("Hide details");
        visible.MenuLabel.Should().Be("Hide details");
        hidden.Visibility.Should().Be(Visibility.Collapsed);
        hidden.ToolTip.Should().Be("Show details");
        hidden.MenuLabel.Should().Be("Show details");
    }
}
