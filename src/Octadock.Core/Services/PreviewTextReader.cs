using System.Buffers.Binary;
using System.Text;
using Octadock.Core.Abstractions;

namespace Octadock.Core.Services;

/// <summary>
/// Shared byte-bounded, strict text reader for preview providers. It recognizes
/// supported BOMs, validates BOM-less UTF-8 without replacement, preserves code
/// point boundaries, and observes one fixed source snapshot.
/// </summary>
internal static class PreviewTextReader
{
    private const int EncodingProbeBytes = 64 * 1024;

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
    private static readonly Encoding StrictUtf16Le = new UnicodeEncoding(false, true, true);
    private static readonly Encoding StrictUtf16Be = new UnicodeEncoding(true, true, true);
    private static readonly Encoding StrictUtf32Le = new UTF32Encoding(false, true, true);
    private static readonly Encoding StrictUtf32Be = new UTF32Encoding(true, true, true);

    public static async Task<PreviewTextReadResult> ReadHeadAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken,
        bool allowControlCharacters = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        await using FileStream stream = OpenShared(path);
        PreviewSourceSnapshot snapshot = CaptureSnapshot(path, stream);
        long sourceLength = snapshot.Length;
        if (sourceLength == 0)
        {
            EnsureUnchanged(path, stream, snapshot);
            return PreviewTextReadResult.Empty;
        }

        DetectedTextEncoding detected = await DetectEncodingAsync(stream, sourceLength, cancellationToken)
            .ConfigureAwait(false);
        long payloadLength = sourceLength - detected.PreambleLength;
        bool truncated = payloadLength > maxBytes;
        int requested = (int)Math.Min(payloadLength, (long)maxBytes + 4);
        byte[] bytes = new byte[requested];
        stream.Position = detected.PreambleLength;
        int read = await ReadAtMostAsync(stream, bytes, requested, cancellationToken).ConfigureAwait(false);
        EnsureUnchanged(path, stream, snapshot);

        ValidateEncodingWindow(
            bytes.AsSpan(0, read),
            detected.Encoding,
            flush: read == payloadLength,
            sourceLength);
        int visibleByteCount = Math.Min(read, maxBytes);
        (string text, int consumedBytes) = DecodeCompleteSuffixBounded(
            bytes.AsSpan(0, visibleByteCount),
            detected.Encoding,
            detected.Metadata.Kind,
            allowTrimAtEnd: truncated,
            sourceLength);
        if (!allowControlCharacters)
        {
            EnsureLooksLikeText(text, sourceLength);
        }

        long endByte = detected.PreambleLength + consumedBytes;
        IReadOnlyList<FilePreviewWarning> warnings = truncated
            ? [new FilePreviewWarning(
                FilePreviewWarningKind.Truncated,
                $"Showing the first {consumedBytes:N0} source bytes of {sourceLength:N0} bytes.")]
            : [];

        return new PreviewTextReadResult(
            text,
            sourceLength,
            detected.Metadata,
            new PreviewContentScope
            {
                StartByte = detected.PreambleLength,
                EndByteExclusive = endByte,
                IsTruncated = truncated,
                Label = truncated ? $"First {consumedBytes:N0} source bytes shown" : "Complete file",
            },
            warnings);
    }

    /// <summary>
    /// Reads a fixed-size tail ending at the source length observed on open.
    /// The returned window starts on a complete code point and, when possible,
    /// the first complete line in that window.
    /// </summary>
    public static async Task<PreviewTextReadResult> ReadTailAsync(
        string path,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        await using FileStream stream = OpenShared(path);
        PreviewSourceSnapshot snapshot = CaptureSnapshot(path, stream);
        long sourceLength = snapshot.Length;
        if (sourceLength == 0)
        {
            EnsureUnchanged(path, stream, snapshot);
            return PreviewTextReadResult.Empty;
        }

        DetectedTextEncoding detected = await DetectEncodingAsync(stream, sourceLength, cancellationToken)
            .ConfigureAwait(false);
        long payloadLength = sourceLength - detected.PreambleLength;
        bool truncated = payloadLength > maxBytes;
        long startByte = truncated
            ? Math.Max(detected.PreambleLength, sourceLength - maxBytes)
            : detected.PreambleLength;

        startByte = AlignStartToCodeUnit(startByte, detected.PreambleLength, detected.Metadata.Kind);
        int lookBehindCount = detected.Metadata.Kind switch
        {
            PreviewEncodingKind.Utf8 => 3,
            PreviewEncodingKind.Utf16LittleEndian or PreviewEncodingKind.Utf16BigEndian => 2,
            PreviewEncodingKind.Utf32LittleEndian or PreviewEncodingKind.Utf32BigEndian => 4,
            _ => 0,
        };
        int availableLookBehind = (int)Math.Min(
            lookBehindCount,
            Math.Max(0, startByte - detected.PreambleLength));
        byte[] lookBehind = new byte[availableLookBehind];
        if (availableLookBehind > 0)
        {
            stream.Position = startByte - availableLookBehind;
            await ReadAtMostAsync(stream, lookBehind, availableLookBehind, cancellationToken)
                .ConfigureAwait(false);
        }

        int requested = checked((int)(sourceLength - startByte));
        byte[] bytes = new byte[requested];
        stream.Position = startByte;
        int read = await ReadAtMostAsync(stream, bytes, requested, cancellationToken).ConfigureAwait(false);
        EnsureUnchanged(path, stream, snapshot);

        (string text, int skippedPrefixBytes) = DecodeCompletePrefixBounded(
            bytes.AsSpan(0, read),
            detected.Encoding,
            detected.Metadata.Kind,
            allowTrimAtStart: truncated,
            lookBehind,
            sourceLength);
        startByte += skippedPrefixBytes;
        EnsureLooksLikeText(text, sourceLength);

        bool beginsAfterLineBreak = EndsWithLineFeed(lookBehind, detected.Metadata.Kind);
        if (truncated && !beginsAfterLineBreak)
        {
            int firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
            if (firstBreak >= 0 && firstBreak + 1 < text.Length)
            {
                string removed = text[..(firstBreak + 1)];
                startByte += detected.Encoding.GetByteCount(removed);
                text = text[(firstBreak + 1)..];
            }
        }

        int shownBytes = checked((int)Math.Max(0, sourceLength - startByte));
        IReadOnlyList<FilePreviewWarning> warnings = truncated
            ? [new FilePreviewWarning(
                FilePreviewWarningKind.Truncated,
                $"Showing the latest {shownBytes:N0} source bytes of {sourceLength:N0} bytes.")]
            : [];

        return new PreviewTextReadResult(
            text,
            sourceLength,
            detected.Metadata,
            new PreviewContentScope
            {
                StartByte = startByte,
                EndByteExclusive = sourceLength,
                IsTruncated = truncated,
                Label = truncated ? $"Latest {shownBytes:N0} source bytes shown" : "Complete file",
            },
            warnings);
    }

    /// <summary>Opens a strict streaming reader positioned after the detected BOM.</summary>
    public static async Task<PreviewTextSession> OpenSessionAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FileStream stream = OpenShared(path);
        try
        {
            PreviewSourceSnapshot snapshot = CaptureSnapshot(path, stream);
            long sourceLength = snapshot.Length;
            DetectedTextEncoding detected = sourceLength == 0
                ? EmptyEncoding
                : await DetectEncodingAsync(stream, sourceLength, cancellationToken).ConfigureAwait(false);
            EnsureUnchanged(path, stream, snapshot);
            stream.Position = detected.PreambleLength;
            var reader = new StreamReader(
                stream,
                detected.Encoding,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 64 * 1024,
                leaveOpen: false);
            return new PreviewTextSession(
                reader,
                stream,
                path,
                snapshot,
                detected.Metadata,
                detected.PreambleLength);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024.0):0.#} MB",
    };

    private static DetectedTextEncoding EmptyEncoding => new(
        StrictUtf8,
        new PreviewEncoding(
            PreviewEncodingKind.Unknown,
            "No encoding detected",
            HasByteOrderMark: false,
            PreviewEncodingConfidence.Unknown),
        PreambleLength: 0);

    private static async Task<DetectedTextEncoding> DetectEncodingAsync(
        FileStream stream,
        long sourceLength,
        CancellationToken cancellationToken)
    {
        int count = (int)Math.Min(sourceLength, EncodingProbeBytes);
        byte[] probe = new byte[count];
        stream.Position = 0;
        int read = await ReadAtMostAsync(stream, probe, count, cancellationToken).ConfigureAwait(false);
        return DetectEncoding(probe, read, sourceLength);
    }

    private static DetectedTextEncoding DetectEncoding(byte[] bytes, int count, long sourceLength)
    {
        if (HasPrefix(bytes, count, 0x00, 0x00, 0xFE, 0xFF))
        {
            return Bom(StrictUtf32Be, PreviewEncodingKind.Utf32BigEndian, "UTF-32 BE", 4);
        }

        if (HasPrefix(bytes, count, 0xFF, 0xFE, 0x00, 0x00))
        {
            return Bom(StrictUtf32Le, PreviewEncodingKind.Utf32LittleEndian, "UTF-32 LE", 4);
        }

        if (HasPrefix(bytes, count, 0xEF, 0xBB, 0xBF))
        {
            return Bom(StrictUtf8, PreviewEncodingKind.Utf8, "UTF-8 with BOM", 3);
        }

        if (HasPrefix(bytes, count, 0xFE, 0xFF))
        {
            return Bom(StrictUtf16Be, PreviewEncodingKind.Utf16BigEndian, "UTF-16 BE", 2);
        }

        if (HasPrefix(bytes, count, 0xFF, 0xFE))
        {
            return Bom(StrictUtf16Le, PreviewEncodingKind.Utf16LittleEndian, "UTF-16 LE", 2);
        }

        try
        {
            Decoder decoder = StrictUtf8.GetDecoder();
            char[] characters = new char[StrictUtf8.GetMaxCharCount(count)];
            decoder.Convert(
                bytes,
                0,
                count,
                characters,
                0,
                characters.Length,
                flush: count == sourceLength,
                out _,
                out int charactersUsed,
                out _);
            string sample = new(characters, 0, charactersUsed);
            EnsureLooksLikeText(sample, sourceLength);
            bool asciiOnly = true;
            for (int index = 0; index < count; index++)
            {
                if (bytes[index] >= 0x80)
                {
                    asciiOnly = false;
                    break;
                }
            }

            return new DetectedTextEncoding(
                StrictUtf8,
                new PreviewEncoding(
                    PreviewEncodingKind.Utf8,
                    "UTF-8",
                    HasByteOrderMark: false,
                    asciiOnly ? PreviewEncodingConfidence.Medium : PreviewEncodingConfidence.High),
                PreambleLength: 0);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PreviewTextReadException(
                FilePreviewFailureKind.UnsupportedEncoding,
                sourceLength,
                ex);
        }
    }

    private static bool HasPrefix(byte[] bytes, int count, params byte[] prefix)
    {
        if (count < prefix.Length)
        {
            return false;
        }

        for (int index = 0; index < prefix.Length; index++)
        {
            if (bytes[index] != prefix[index])
            {
                return false;
            }
        }

        return true;
    }

    private static DetectedTextEncoding Bom(
        Encoding encoding,
        PreviewEncodingKind kind,
        string name,
        int preambleLength)
        => new(
            encoding,
            new PreviewEncoding(kind, name, HasByteOrderMark: true, PreviewEncodingConfidence.High),
            preambleLength);

    private static (string Text, int ConsumedBytes) DecodeCompleteSuffixBounded(
        ReadOnlySpan<byte> bytes,
        Encoding encoding,
        PreviewEncodingKind kind,
        bool allowTrimAtEnd,
        long sourceLength)
    {
        int count = allowTrimAtEnd
            ? CompletePrefixLength(bytes, kind)
            : bytes.Length;
        try
        {
            return (encoding.GetString(bytes[..count]), count);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PreviewTextReadException(
                FilePreviewFailureKind.UnsupportedEncoding,
                sourceLength,
                ex);
        }
    }

    private static (string Text, int SkippedBytes) DecodeCompletePrefixBounded(
        ReadOnlySpan<byte> bytes,
        Encoding encoding,
        PreviewEncodingKind kind,
        bool allowTrimAtStart,
        ReadOnlySpan<byte> lookBehind,
        long sourceLength)
    {
        int skip = allowTrimAtStart
            ? CompletePrefixSkip(bytes, kind, lookBehind, sourceLength)
            : 0;
        try
        {
            return (encoding.GetString(bytes[skip..]), skip);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PreviewTextReadException(
                FilePreviewFailureKind.UnsupportedEncoding,
                sourceLength,
                ex);
        }
    }

    private static int CompletePrefixLength(ReadOnlySpan<byte> bytes, PreviewEncodingKind kind)
    {
        int count = kind switch
        {
            PreviewEncodingKind.Utf16LittleEndian or PreviewEncodingKind.Utf16BigEndian =>
                bytes.Length - (bytes.Length % 2),
            PreviewEncodingKind.Utf32LittleEndian or PreviewEncodingKind.Utf32BigEndian =>
                bytes.Length - (bytes.Length % 4),
            PreviewEncodingKind.Utf8 => CompleteUtf8PrefixLength(bytes),
            _ => bytes.Length,
        };

        if (count >= 2 && kind is PreviewEncodingKind.Utf16LittleEndian or PreviewEncodingKind.Utf16BigEndian)
        {
            ushort last = ReadUtf16(bytes[(count - 2)..count], kind);
            if (last is >= 0xD800 and <= 0xDBFF)
            {
                count -= 2;
            }
        }

        return count;
    }

    private static int CompleteUtf8PrefixLength(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        int sequenceStart = bytes.Length - 1;
        while (sequenceStart > 0 && IsUtf8Continuation(bytes[sequenceStart]))
        {
            sequenceStart--;
        }

        byte lead = bytes[sequenceStart];
        int expected = lead switch
        {
            <= 0x7F => 1,
            >= 0xC2 and <= 0xDF => 2,
            >= 0xE0 and <= 0xEF => 3,
            >= 0xF0 and <= 0xF4 => 4,
            _ => 1,
        };
        int available = bytes.Length - sequenceStart;
        return available < expected ? sequenceStart : bytes.Length;
    }

    private static int CompletePrefixSkip(
        ReadOnlySpan<byte> bytes,
        PreviewEncodingKind kind,
        ReadOnlySpan<byte> lookBehind,
        long sourceLength)
    {
        if (bytes.IsEmpty)
        {
            return 0;
        }

        if (kind == PreviewEncodingKind.Utf8)
        {
            int skip = 0;
            while (skip < Math.Min(3, bytes.Length) && IsUtf8Continuation(bytes[skip]))
            {
                skip++;
            }

            if (skip > 0)
            {
                byte[] crossingSequence = new byte[lookBehind.Length + skip];
                lookBehind.CopyTo(crossingSequence);
                bytes[..skip].CopyTo(crossingSequence.AsSpan(lookBehind.Length));
                int lead = lookBehind.Length - 1;
                while (lead >= 0 && IsUtf8Continuation(crossingSequence[lead]))
                {
                    lead--;
                }

                if (lead < 0)
                {
                    throw new PreviewTextReadException(
                        FilePreviewFailureKind.UnsupportedEncoding,
                        sourceLength);
                }

                try
                {
                    _ = StrictUtf8.GetString(crossingSequence.AsSpan(lead));
                }
                catch (DecoderFallbackException ex)
                {
                    throw new PreviewTextReadException(
                        FilePreviewFailureKind.UnsupportedEncoding,
                        sourceLength,
                        ex);
                }
            }

            return skip;
        }

        if (bytes.Length >= 2 && kind is PreviewEncodingKind.Utf16LittleEndian or PreviewEncodingKind.Utf16BigEndian)
        {
            ushort first = ReadUtf16(bytes[..2], kind);
            if (first is >= 0xDC00 and <= 0xDFFF)
            {
                if (lookBehind.Length < 2)
                {
                    throw new PreviewTextReadException(
                        FilePreviewFailureKind.UnsupportedEncoding,
                        sourceLength);
                }

                byte[] pair = [.. lookBehind[^2..], .. bytes[..2]];
                try
                {
                    _ = (kind == PreviewEncodingKind.Utf16LittleEndian ? StrictUtf16Le : StrictUtf16Be)
                        .GetString(pair);
                }
                catch (DecoderFallbackException ex)
                {
                    throw new PreviewTextReadException(
                        FilePreviewFailureKind.UnsupportedEncoding,
                        sourceLength,
                        ex);
                }

                return 2;
            }
        }

        return 0;
    }

    private static bool IsUtf8Continuation(byte value) => (value & 0xC0) == 0x80;

    private static ushort ReadUtf16(ReadOnlySpan<byte> bytes, PreviewEncodingKind kind)
        => kind == PreviewEncodingKind.Utf16LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(bytes)
            : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static void ValidateEncodingWindow(
        ReadOnlySpan<byte> bytes,
        Encoding encoding,
        bool flush,
        long sourceLength)
    {
        try
        {
            Decoder decoder = encoding.GetDecoder();
            char[] characters = new char[encoding.GetMaxCharCount(bytes.Length)];
            decoder.Convert(
                bytes,
                characters,
                flush,
                out _,
                out _,
                out _);
        }
        catch (DecoderFallbackException ex)
        {
            throw new PreviewTextReadException(
                FilePreviewFailureKind.UnsupportedEncoding,
                sourceLength,
                ex);
        }
    }

    private static long AlignStartToCodeUnit(
        long start,
        int preambleLength,
        PreviewEncodingKind kind)
    {
        int unit = kind switch
        {
            PreviewEncodingKind.Utf16LittleEndian or PreviewEncodingKind.Utf16BigEndian => 2,
            PreviewEncodingKind.Utf32LittleEndian or PreviewEncodingKind.Utf32BigEndian => 4,
            _ => 1,
        };
        if (unit == 1)
        {
            return start;
        }

        long remainder = (start - preambleLength) % unit;
        return remainder == 0 ? start : start + (unit - remainder);
    }

    private static bool EndsWithLineFeed(ReadOnlySpan<byte> bytes, PreviewEncodingKind kind)
        => kind switch
        {
            PreviewEncodingKind.Utf8 => bytes.Length >= 1 && bytes[^1] == 0x0A,
            PreviewEncodingKind.Utf16LittleEndian =>
                bytes.Length >= 2 && bytes[^2] == 0x0A && bytes[^1] == 0x00,
            PreviewEncodingKind.Utf16BigEndian =>
                bytes.Length >= 2 && bytes[^2] == 0x00 && bytes[^1] == 0x0A,
            PreviewEncodingKind.Utf32LittleEndian =>
                bytes.Length >= 4 && bytes[^4] == 0x0A &&
                bytes[^3] == 0x00 && bytes[^2] == 0x00 && bytes[^1] == 0x00,
            PreviewEncodingKind.Utf32BigEndian =>
                bytes.Length >= 4 && bytes[^4] == 0x00 &&
                bytes[^3] == 0x00 && bytes[^2] == 0x00 && bytes[^1] == 0x0A,
            _ => false,
        };

    private static void EnsureLooksLikeText(string text, long sourceLength)
    {
        if (text.Length == 0)
        {
            return;
        }

        // NUL is the reliable signal used for a binary file renamed as text.
        // Other valid Unicode controls (for example ANSI terminal ESC sequences)
        // remain source content and are never mislabeled as an encoding failure.
        if (text.Contains('\0'))
        {
            throw new PreviewTextReadException(FilePreviewFailureKind.UnsupportedEncoding, sourceLength);
        }
    }

    private static async Task<int> ReadAtMostAsync(
        Stream stream,
        byte[] buffer,
        int count,
        CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(total, count - total), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static PreviewSourceSnapshot CaptureSnapshot(string path, FileStream stream)
    {
        long streamLength = stream.Length;
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists || info.Length != streamLength)
        {
            throw new PreviewTextReadException(FilePreviewFailureKind.Changed, streamLength);
        }

        return new PreviewSourceSnapshot(
            streamLength,
            info.LastWriteTimeUtc,
            info.CreationTimeUtc);
    }

    internal static void EnsureUnchanged(
        string path,
        FileStream stream,
        PreviewSourceSnapshot expected)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            if (stream.Length != expected.Length ||
                !info.Exists ||
                info.Length != expected.Length ||
                info.LastWriteTimeUtc != expected.LastWriteTimeUtc ||
                info.CreationTimeUtc != expected.CreationTimeUtc)
            {
                throw new PreviewTextReadException(FilePreviewFailureKind.Changed, expected.Length);
            }
        }
        catch (PreviewTextReadException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new PreviewTextReadException(FilePreviewFailureKind.Changed, expected.Length, ex);
        }
    }

    private static FileStream OpenShared(string path)
        => new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private sealed record DetectedTextEncoding(
        Encoding Encoding,
        PreviewEncoding Metadata,
        int PreambleLength);
}

internal readonly record struct PreviewSourceSnapshot(
    long Length,
    DateTime LastWriteTimeUtc,
    DateTime CreationTimeUtc);

internal sealed record PreviewTextReadResult(
    string Text,
    long SourceByteLength,
    PreviewEncoding? DetectedEncoding,
    PreviewContentScope Scope,
    IReadOnlyList<FilePreviewWarning> Warnings)
{
    public static PreviewTextReadResult Empty { get; } = new(
        string.Empty,
        0,
        null,
        new PreviewContentScope
        {
            StartByte = 0,
            EndByteExclusive = 0,
            Label = "Empty file",
        },
        [new FilePreviewWarning(FilePreviewWarningKind.Empty, "0 bytes—nothing to preview")]);
}

internal sealed class PreviewTextSession : IAsyncDisposable, IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private readonly PreviewSourceSnapshot _snapshot;

    public PreviewTextSession(
        StreamReader reader,
        FileStream stream,
        string path,
        PreviewSourceSnapshot snapshot,
        PreviewEncoding encoding,
        int preambleLength)
    {
        Reader = reader;
        _stream = stream;
        _path = path;
        _snapshot = snapshot;
        SourceByteLength = snapshot.Length;
        Encoding = snapshot.Length == 0 ? null : encoding;
        PreambleLength = preambleLength;
    }

    public StreamReader Reader { get; }

    public long SourceByteLength { get; }

    public PreviewEncoding? Encoding { get; }

    public int PreambleLength { get; }

    public void Reset()
    {
        VerifyUnchanged();
        Reader.DiscardBufferedData();
        _stream.Position = PreambleLength;
    }

    public void VerifyUnchanged()
        => PreviewTextReader.EnsureUnchanged(_path, _stream, _snapshot);

    public void Dispose() => Reader.Dispose();

    public ValueTask DisposeAsync()
    {
        Reader.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class PreviewTextReadException : IOException
{
    public PreviewTextReadException(
        FilePreviewFailureKind failureKind,
        long? sourceByteLength,
        Exception? innerException = null)
        : base(FilePreviewFailureCopy.For(failureKind), innerException)
    {
        FailureKind = failureKind;
        SourceByteLength = sourceByteLength;
    }

    public FilePreviewFailureKind FailureKind { get; }

    public long? SourceByteLength { get; }
}

internal static class PreviewFailureMapper
{
    public static FilePreviewResult FromException(string path, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        (FilePreviewFailureKind kind, long? sourceLength) = exception switch
        {
            PreviewTextReadException read => (read.FailureKind, read.SourceByteLength),
            FileNotFoundException or DirectoryNotFoundException => (FilePreviewFailureKind.NotFound, null),
            UnauthorizedAccessException => (FilePreviewFailureKind.AccessDenied, null),
            DecoderFallbackException => (FilePreviewFailureKind.UnsupportedEncoding, null),
            IOException io when IsSharingViolation(io) => (FilePreviewFailureKind.Busy, null),
            IOException => (FilePreviewFailureKind.Unknown, null),
            _ => (FilePreviewFailureKind.Unknown, null),
        };

        return FilePreviewResult.Fail(path, kind, sourceLength);
    }

    private static bool IsSharingViolation(IOException exception)
    {
        int code = exception.HResult & 0xFFFF;
        return code is 32 or 33;
    }
}
