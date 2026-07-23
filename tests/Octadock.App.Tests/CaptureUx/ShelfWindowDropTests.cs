using System.Windows;
using FluentAssertions;
using Octadock.App.CaptureUx;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

/// <summary>
/// Shelf drop ingress: the Shelf accepts Octadock captures and recordings only.
/// Since the arbitrary-file drop/preview import was removed, the only payload the
/// drop handlers honor is Octadock's private capture-id format; plain FileDrop
/// payloads (arbitrary files) are refused at the drag-over boundary.
/// </summary>
public sealed class ShelfWindowDropTests
{
    [Fact]
    public void Capture_drag_payload_round_trips_the_capture_id()
    {
        Guid captureId = Guid.NewGuid();
        var data = new DataObject();
        ShelfDragPayload.SetCaptureId(data, captureId);

        ShelfDragPayload.TryGetCaptureId(data, out Guid resolved).Should().BeTrue();
        resolved.Should().Be(captureId);
    }

    [Fact]
    public void Arbitrary_file_drop_is_not_recognized_as_shelf_ingress()
    {
        // What Explorer (or any external app) puts on the clipboard for a file drag.
        var data = new DataObject();
        data.SetFileDropList(new System.Collections.Specialized.StringCollection { @"C:\repo\notes.md", @"C:\img\photo.png" });

        ShelfDragPayload.TryGetCaptureId(data, out _).Should().BeFalse(
            "the Shelf has no arbitrary-file import; only Octadock's own capture payload is accepted");
    }

    [Fact]
    public void Image_file_drop_is_also_not_recognized_as_shelf_ingress()
    {
        // Even a supported image type no longer has a drop-to-dock route.
        var data = new DataObject(DataFormats.FileDrop, new[] { @"C:\img\capture-lookalike.png" });

        ShelfDragPayload.TryGetCaptureId(data, out _).Should().BeFalse();
    }

    [Fact]
    public void Malformed_or_empty_payloads_are_rejected_without_throwing()
    {
        ShelfDragPayload.TryGetCaptureId(new DataObject(), out _).Should().BeFalse();
        ShelfDragPayload.TryGetCaptureId(new DataObject(ShelfDragPayload.CaptureIdFormat, "not-a-guid"), out _)
            .Should().BeFalse();
        ShelfDragPayload.TryGetCaptureId(new DataObject(ShelfDragPayload.CaptureIdFormat, Guid.Empty.ToString("D")), out _)
            .Should().BeFalse("an empty id is not a real capture reference");
    }
}
