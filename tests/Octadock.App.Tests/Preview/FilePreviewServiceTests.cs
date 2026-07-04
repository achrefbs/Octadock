using FluentAssertions;
using Octadock.App.Preview;
using Xunit;

namespace Octadock.App.Tests.Preview;

public sealed class FilePreviewServiceTests
{
    [Theory]
    [InlineData(".png", "PNG files (*.png)|*.png|All files (*.*)|*.*")]
    [InlineData("csv", "CSV files (*.csv)|*.csv|All files (*.*)|*.*")]
    public void BuildSaveFilter_uses_source_extension(string extension, string expected)
    {
        FilePreviewService.BuildSaveFilter(extension).Should().Be(expected);
    }

    [Fact]
    public void BuildSaveFilter_without_extension_allows_all_files()
    {
        FilePreviewService.BuildSaveFilter(string.Empty).Should().Be("All files (*.*)|*.*");
    }

    [Theory]
    [InlineData(".png")]
    [InlineData("JPG")]
    [InlineData(".jfif")]
    [InlineData(".webp")]
    [InlineData(".tiff")]
    [InlineData(".heic")]
    [InlineData(".avif")]
    public void ImageFileSupport_accepts_previewable_raster_images(string extension)
    {
        ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeTrue();
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".csv")]
    [InlineData("")]
    public void ImageFileSupport_rejects_non_image_extensions(string extension)
    {
        ImageFileSupport.IsSupportedRasterExtension(extension).Should().BeFalse();
    }
}
