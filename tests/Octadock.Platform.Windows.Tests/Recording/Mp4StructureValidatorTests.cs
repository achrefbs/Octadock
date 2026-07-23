using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Octadock.Core.Recording;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Recording;

public sealed class Mp4StructureValidatorTests
{
    [Fact]
    public void Valid_finalized_recording_passes()
    {
        byte[] file = BuildRecording(duration: 1_500, timescale: 1_000);

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeTrue();
        error.Should().BeEmpty();
    }

    [Fact]
    public void Moov_at_the_end_of_the_file_is_accepted()
    {
        // Media Foundation writes moov after mdat on finalize.
        byte[] file = Concat(FtypBox(), MdatBox(payloadBytes: 32), MoovBox(duration: 2_000, timescale: 1_000));

        Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Version1_mvhd_with_duration_passes()
    {
        byte[] file = Concat(FtypBox(), MdatBox(payloadBytes: 16), MoovBox(duration: 5_000, timescale: 1_000, version: 1));

        Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out _)
            .Should()
            .BeTrue();
    }

    [Fact]
    public void Zero_movie_duration_is_rejected()
    {
        byte[] file = BuildRecording(duration: 0, timescale: 1_000);

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeFalse();
        error.Should().Contain("duration is zero");
    }

    [Fact]
    public void Zero_timescale_is_rejected()
    {
        byte[] file = BuildRecording(duration: 1_500, timescale: 0);

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeFalse();
        error.Should().Contain("timescale is zero");
    }

    [Fact]
    public void Missing_moov_box_is_reported_as_not_finalized()
    {
        byte[] file = Concat(FtypBox(), MdatBox(payloadBytes: 1_024));

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeFalse();
        error.Should().Contain("not finalized");
    }

    [Fact]
    public void Missing_mdat_box_is_rejected()
    {
        byte[] file = Concat(FtypBox(), MoovBox(duration: 1_500, timescale: 1_000));

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeFalse();
        error.Should().Contain("no media data");
    }

    [Fact]
    public void A_first_box_other_than_ftyp_is_rejected()
    {
        byte[] file = Concat(MdatBox(payloadBytes: 16), FtypBox(), MoovBox(duration: 1_500, timescale: 1_000));

        bool valid = Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out string error);

        valid.Should().BeFalse();
        error.Should().Contain("ftyp");
    }

    [Fact]
    public void Garbage_bytes_are_rejected()
    {
        byte[] file = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];

        Mp4StructureValidator.IsValidRecording(new MemoryStream(file), out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void A_file_truncated_inside_moov_is_rejected()
    {
        byte[] file = BuildRecording(duration: 1_500, timescale: 1_000);
        byte[] truncated = file[..(FtypBox().Length + MdatBox(32).Length + 12)];

        Mp4StructureValidator.IsValidRecording(new MemoryStream(truncated), out _)
            .Should()
            .BeFalse();
    }

    [Fact]
    public void Path_overload_validates_a_real_file()
    {
        string path = Path.Combine(Path.GetTempPath(), $"octadock-mp4test-{Guid.NewGuid():N}.mp4");
        try
        {
            File.WriteAllBytes(path, BuildRecording(duration: 1_500, timescale: 1_000));

            Mp4StructureValidator.IsValidRecording(path, out string error)
                .Should()
                .BeTrue(error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Path_overload_reports_a_missing_file_without_throwing()
    {
        bool valid = Mp4StructureValidator.IsValidRecording(
            Path.Combine(Path.GetTempPath(), $"octadock-missing-{Guid.NewGuid():N}.mp4"),
            out string error);

        valid.Should().BeFalse();
        error.Should().NotBeEmpty();
    }

    private static byte[] BuildRecording(uint duration, uint timescale)
        => Concat(FtypBox(), MdatBox(payloadBytes: 64), MoovBox(duration, timescale));

    private static byte[] FtypBox()
    {
        byte[] content = new byte[12];
        Encoding.ASCII.GetBytes("isom").CopyTo(content, 0);
        Encoding.ASCII.GetBytes("isom").CopyTo(content, 8);
        return Box("ftyp", content);
    }

    private static byte[] MdatBox(int payloadBytes)
        => Box("mdat", new byte[payloadBytes]);

    private static byte[] MoovBox(uint duration, uint timescale, byte version = 0)
    {
        // mvhd v0 content: version/flags(4) ctime(4) mtime(4) timescale(4) duration(4) + 80 reserved.
        // mvhd v1 content: version/flags(4) ctime(8) mtime(8) timescale(4) duration(8) + 80 reserved.
        byte[] content = new byte[version == 1 ? 112 : 100];
        content[0] = version;
        if (version == 1)
        {
            BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(20), timescale);
            BinaryPrimitives.WriteUInt64BigEndian(content.AsSpan(24), duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(12), timescale);
            BinaryPrimitives.WriteUInt32BigEndian(content.AsSpan(16), duration);
        }

        return Box("moov", Box("mvhd", content));
    }

    private static byte[] Box(string type, byte[] content)
    {
        byte[] box = new byte[8 + content.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        content.CopyTo(box, 8);
        return box;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        byte[] result = new byte[parts.Sum(p => p.Length)];
        int offset = 0;
        foreach (byte[] part in parts)
        {
            part.CopyTo(result, offset);
            offset += part.Length;
        }

        return result;
    }
}
