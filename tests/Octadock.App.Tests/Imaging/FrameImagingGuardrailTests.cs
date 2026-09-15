using FluentAssertions;
using Octadock.App.Imaging;
using Xunit;

namespace Octadock.App.Tests.Imaging;

/// <summary>
/// Decode guardrails shared by the capture, thumbnail, clipboard and annotation
/// image paths. (Moved here when the generic preview layer was removed; these
/// limits still protect every remaining image-decode entry point.)
/// </summary>
public sealed class FrameImagingGuardrailTests
{
    private const uint WinCodecErrComponentNotFound = 0x88982F50;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Frozen_decoded_image_can_be_encoded_on_another_thread(bool jpeg)
    {
        System.Windows.Media.Imaging.BitmapSource? loaded = null;
        Exception? failure = null;
        var decodeThread = new Thread(() =>
        {
            try
            {
                var source = System.Windows.Media.Imaging.BitmapSource.Create(
                    2, 2, 96, 96, System.Windows.Media.PixelFormats.Bgra32,
                    null, new byte[] { 0, 0, 255, 255, 0, 255, 0, 255,
                        255, 0, 0, 255, 255, 255, 255, 255 }, 8);
                loaded = FrameImaging.LoadFromBytes(FrameImaging.EncodePng(source));
            }
            catch (Exception ex) { failure = ex; }
        });
        decodeThread.Start();
        decodeThread.Join();
        failure.Should().BeNull();
        loaded!.IsFrozen.Should().BeTrue();

        byte[] encoded = jpeg ? FrameImaging.EncodeJpeg(loaded, 95) : FrameImaging.EncodePng(loaded);
        var decoded = FrameImaging.LoadFromBytes(encoded);
        decoded.PixelWidth.Should().Be(2);
        decoded.PixelHeight.Should().Be(2);
    }

    [Fact]
    public void MapDecodeFailure_labels_a_missing_optional_codec_truthfully()
    {
        var codecMissing = new InvalidOperationException("outer", new FileFormatException("wrapped")
        {
            HResult = unchecked((int)WinCodecErrComponentNotFound),
        });

        ImageDecodeException result = FrameImaging.MapDecodeFailure("optional.webp", codecMissing);

        result.FailureKind.Should().Be(ImageFailureKind.CodecUnavailable);
        result.Message.Should().Contain("codec");
    }

    [Fact]
    public void MapDecodeFailure_labels_missing_files_and_busy_sharing_violations()
    {
        FrameImaging.MapDecodeFailure("capture.png", new FileNotFoundException())
            .FailureKind.Should().Be(ImageFailureKind.NotFound);
        FrameImaging.MapDecodeFailure("capture.png", new IOException("used", 32))
            .FailureKind.Should().Be(ImageFailureKind.Busy);
        FrameImaging.MapDecodeFailure("capture.png", new FormatException())
            .FailureKind.Should().Be(ImageFailureKind.Malformed);
    }

    [Fact]
    public void ValidateDimensions_rejects_excessively_large_decodes()
    {
        Action act = () => FrameImaging.ValidateDimensions(8192, 8192, bitsPerPixel: 128);

        act.Should().Throw<ImageDecodeException>()
            .Which.FailureKind.Should().Be(ImageFailureKind.TooLarge);
    }

    [Fact]
    public void ValidateDimensions_rejects_dimensions_beyond_the_pixel_cap()
    {
        Action act = () => FrameImaging.ValidateDimensions(FrameImaging.MaxPixelDimension + 1, 1);

        act.Should().Throw<ImageDecodeException>()
            .Which.FailureKind.Should().Be(ImageFailureKind.TooLarge);
    }

    [Fact]
    public void ValidateDimensions_rejects_degenerate_sizes_as_malformed()
    {
        Action act = () => FrameImaging.ValidateDimensions(0, 100);

        act.Should().Throw<ImageDecodeException>()
            .Which.FailureKind.Should().Be(ImageFailureKind.Malformed);
    }
}
