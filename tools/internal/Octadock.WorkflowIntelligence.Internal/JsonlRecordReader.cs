using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed record JsonlRecord(
    long Ordinal,
    long ByteStart,
    long ByteEndExclusive,
    ReadOnlyMemory<byte> Utf8,
    string Sha256);

internal sealed class OversizedJsonlRecordException : IOException
{
    internal OversizedJsonlRecordException(long ordinal, long byteStart, int maximumBytes)
        : base($"JSONL record {ordinal} at byte {byteStart} exceeded the {maximumBytes}-byte safety limit.")
    {
        Ordinal = ordinal;
        ByteStart = byteStart;
        MaximumBytes = maximumBytes;
    }

    internal long Ordinal { get; }

    internal long ByteStart { get; }

    internal int MaximumBytes { get; }
}

internal static class JsonlRecordReader
{
    private const int BufferSize = 64 * 1024;
    private const int DefaultMaximumRecordBytes = 32 * 1024 * 1024;

    internal static async IAsyncEnumerable<JsonlRecord> ReadAsync(
        string path,
        int maximumRecordBytes = DefaultMaximumRecordBytes,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumRecordBytes);

        await using FileStream stream = new(
            Path.GetFullPath(path),
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] readBuffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        using var line = new MemoryStream(capacity: Math.Min(maximumRecordBytes, BufferSize));
        long recordStart = 0;
        long streamOffset = 0;
        long ordinal = 0;
        try
        {
            while (true)
            {
                int read = await stream.ReadAsync(readBuffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                int chunkStart = 0;
                while (chunkStart < read)
                {
                    int relativeNewline = readBuffer.AsSpan(chunkStart, read - chunkStart).IndexOf((byte)'\n');
                    if (relativeNewline < 0)
                    {
                        Append(line, readBuffer.AsSpan(chunkStart, read - chunkStart), ordinal, recordStart, maximumRecordBytes);
                        break;
                    }

                    int newline = chunkStart + relativeNewline;
                    Append(line, readBuffer.AsSpan(chunkStart, newline - chunkStart), ordinal, recordStart, maximumRecordBytes);
                    yield return CreateRecord(line, ordinal, recordStart, streamOffset + newline);
                    ordinal++;
                    line.SetLength(0);
                    recordStart = streamOffset + newline + 1;
                    chunkStart = newline + 1;
                }
                streamOffset += read;
            }

            if (line.Length > 0)
            {
                yield return CreateRecord(line, ordinal, recordStart, streamOffset);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(readBuffer, clearArray: true);
        }
    }

    internal static string Decode(JsonlRecord record)
    {
        ReadOnlySpan<byte> bytes = record.Utf8.Span;
        if (record.Ordinal == 0 && bytes.StartsWith(Encoding.UTF8.Preamble))
        {
            bytes = bytes[Encoding.UTF8.Preamble.Length..];
        }
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static void Append(
        MemoryStream line,
        ReadOnlySpan<byte> bytes,
        long ordinal,
        long recordStart,
        int maximumRecordBytes)
    {
        if (line.Length + bytes.Length > maximumRecordBytes)
        {
            throw new OversizedJsonlRecordException(ordinal, recordStart, maximumRecordBytes);
        }
        line.Write(bytes);
    }

    private static JsonlRecord CreateRecord(
        MemoryStream line,
        long ordinal,
        long byteStart,
        long byteEndExclusive)
    {
        byte[] bytes = line.ToArray();
        if (bytes.Length > 0 && bytes[^1] == '\r')
        {
            Array.Resize(ref bytes, bytes.Length - 1);
            byteEndExclusive--;
        }
        return new JsonlRecord(
            ordinal,
            byteStart,
            byteEndExclusive,
            bytes,
            Convert.ToHexString(SHA256.HashData(bytes)));
    }
}
