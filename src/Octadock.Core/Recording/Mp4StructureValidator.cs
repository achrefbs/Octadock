using System.Buffers.Binary;
using System.Text;

namespace Octadock.Core.Recording;

/// <summary>
/// Headless structural check for an MP4 recording: walks the top-level ISO-BMFF
/// boxes, requires <c>ftyp</c>/<c>mdat</c>/<c>moov</c>, and reads the <c>mvhd</c>
/// movie duration. A recording whose finalize step died (crash, power loss, disk
/// full) has no <c>moov</c> box at all, so this reliably separates completed
/// files from broken partial output without decoding any media. Only box headers
/// are read; the payload is skipped via seeks, so the check stays cheap on
/// multi-GB files.
/// </summary>
public static class Mp4StructureValidator
{
    // Bounded walks: a real recording has a handful of top-level boxes and a
    // moov with a few dozen children; the caps only guard against corrupt data
    // keeping the loop alive.
    private const int MaxTopLevelBoxes = 128;
    private const int MaxMovieBoxes = 512;
    private const int MaxBoxHeaderReadBytes = 16;

    /// <summary>
    /// Validates the MP4 at <paramref name="path"/>. Returns <c>true</c> for a
    /// structurally complete recording with a nonzero movie duration; otherwise
    /// returns <c>false</c> and sets <paramref name="error"/> to a short reason.
    /// </summary>
    public static bool IsValidRecording(string path, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return IsValidRecording(stream, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            error = $"the file could not be read ({ex.Message})";
            return false;
        }
    }

    /// <summary>Stream-based overload; requires a seekable stream.</summary>
    public static bool IsValidRecording(Stream stream, out string error)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek)
        {
            throw new ArgumentException("The stream must be seekable.", nameof(stream));
        }

        long length = stream.Length;
        long position = 0;
        bool sawFtyp = false;
        bool sawMdat = false;
        long moovStart = -1;
        long moovSize = 0;

        for (int i = 0; i < MaxTopLevelBoxes && position + 8 <= length; i++)
        {
            if (!TryReadBoxHeader(stream, position, length, out long boxSize, out string boxType, out string headerError))
            {
                error = headerError;
                return false;
            }

            if (i == 0 && boxType != "ftyp")
            {
                error = $"the first box is '{boxType}', not 'ftyp' — this is not a finalized MP4 recording";
                return false;
            }

            switch (boxType)
            {
                case "ftyp":
                    sawFtyp = true;
                    break;
                case "mdat":
                    sawMdat = true;
                    break;
                case "moov":
                    moovStart = position;
                    moovSize = boxSize;
                    break;
            }

            position += boxSize;
        }

        if (!sawFtyp)
        {
            error = "no 'ftyp' box — this is not a finalized MP4 recording";
            return false;
        }

        if (!sawMdat)
        {
            error = "no 'mdat' box — the recording contains no media data";
            return false;
        }

        if (moovStart < 0)
        {
            error = "no 'moov' box — the recording was not finalized";
            return false;
        }

        return TryReadMovieDuration(stream, moovStart, moovSize, out error);
    }

    private static bool TryReadMovieDuration(Stream stream, long moovStart, long moovSize, out string error)
    {
        long position = moovStart + 8;
        long end = moovStart + moovSize;

        for (int i = 0; i < MaxMovieBoxes && position + 8 <= end; i++)
        {
            if (!TryReadBoxHeader(stream, position, end, out long boxSize, out string boxType, out string headerError))
            {
                error = headerError;
                return false;
            }

            if (boxType == "mvhd")
            {
                return TryReadMvhd(stream, position + 8, boxSize - 8, out error);
            }

            position += boxSize;
        }

        error = "the movie box contains no 'mvhd' header";
        return false;
    }

    private static bool TryReadMvhd(Stream stream, long contentStart, long contentSize, out string error)
    {
        // mvhd v0: version/flags(4) ctime(4) mtime(4) timescale(4) duration(4).
        // mvhd v1: version/flags(4) ctime(8) mtime(8) timescale(4) duration(8).
        // Reading through the v1 duration field needs 32 bytes either way.
        Span<byte> header = stackalloc byte[32];
        if (contentSize < header.Length)
        {
            error = "the 'mvhd' box is truncated";
            return false;
        }

        stream.Position = contentStart;
        if (stream.Read(header) != header.Length)
        {
            error = "the 'mvhd' box could not be read";
            return false;
        }

        byte version = header[0];
        uint timescale = BinaryPrimitives.ReadUInt32BigEndian(header.Slice(version == 1 ? 20 : 12, 4));
        ulong duration = version == 1
            ? BinaryPrimitives.ReadUInt64BigEndian(header.Slice(24, 8))
            : BinaryPrimitives.ReadUInt32BigEndian(header.Slice(16, 4));

        if (timescale == 0)
        {
            error = "the movie timescale is zero";
            return false;
        }

        if (duration == 0)
        {
            error = "the movie duration is zero — the recording is empty";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryReadBoxHeader(
        Stream stream,
        long position,
        long limit,
        out long boxSize,
        out string boxType,
        out string error)
    {
        boxSize = 0;
        boxType = string.Empty;

        Span<byte> header = stackalloc byte[MaxBoxHeaderReadBytes];
        stream.Position = position;
        int read = stream.Read(header.Slice(0, 8));
        if (read < 8)
        {
            error = "truncated box header";
            return false;
        }

        uint size32 = BinaryPrimitives.ReadUInt32BigEndian(header);
        boxType = Encoding.ASCII.GetString(header.Slice(4, 4));

        if (size32 == 1)
        {
            if (position + MaxBoxHeaderReadBytes > limit || stream.Read(header.Slice(8, 8)) < 8)
            {
                error = "truncated large-size box header";
                return false;
            }

            boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(header.Slice(8, 8));
        }
        else if (size32 == 0)
        {
            // Box extends to the end of the file (only legal on the last box).
            boxSize = limit - position;
        }
        else
        {
            boxSize = size32;
        }

        if (boxSize < 8 || position + boxSize > limit)
        {
            error = $"malformed '{boxType}' box (size {boxSize} at offset {position})";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
