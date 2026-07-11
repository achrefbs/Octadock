using System.Text;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed record IncrementalTailBatch(
    long RequestedOffset,
    long EffectiveOffset,
    long NextOffset,
    long FileLength,
    bool ResetAfterTruncation,
    bool HasIncompleteLine,
    bool HasUnreadBytes,
    bool BlockedByOversizedLine,
    IReadOnlyList<string> Lines);

internal static class IncrementalJsonlTailer
{
    private const int DefaultMaxBytes = 8 * 1024 * 1024;
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static async Task<IncrementalTailBatch> ReadAsync(
        string path,
        long offset,
        int maxBytes = DefaultMaxBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        string fullPath = Path.GetFullPath(path);
        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        long fileLength = stream.Length;
        bool reset = offset > fileLength;
        long effectiveOffset = reset ? 0 : offset;
        stream.Seek(effectiveOffset, SeekOrigin.Begin);
        int bytesToRead = (int)Math.Min(maxBytes, fileLength - effectiveOffset);
        if (bytesToRead == 0)
        {
            return new IncrementalTailBatch(
                offset, effectiveOffset, effectiveOffset, fileLength, reset, false, false, false, []);
        }

        byte[] buffer = new byte[bytesToRead];
        int bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(bytesRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            bytesRead += read;
        }

        int lastNewline = Array.LastIndexOf(buffer, (byte)'\n', bytesRead - 1, bytesRead);
        if (lastNewline < 0)
        {
            return new IncrementalTailBatch(
                offset,
                effectiveOffset,
                effectiveOffset,
                fileLength,
                reset,
                HasIncompleteLine: bytesRead > 0,
                HasUnreadBytes: effectiveOffset + bytesRead < fileLength,
                BlockedByOversizedLine: bytesRead == maxBytes,
                []);
        }

        string completeText = StrictUtf8.GetString(buffer, 0, lastNewline);
        string[] lines = completeText
            .Split('\n', StringSplitOptions.None)
            .Select(line => line.EndsWith('\r') ? line[..^1] : line)
            .ToArray();
        if (effectiveOffset == 0 && lines.Length > 0)
        {
            lines[0] = lines[0].TrimStart('\uFEFF');
        }

        long nextOffset = effectiveOffset + lastNewline + 1L;
        return new IncrementalTailBatch(
            offset,
            effectiveOffset,
            nextOffset,
            fileLength,
            reset,
            HasIncompleteLine: nextOffset < fileLength,
            HasUnreadBytes: effectiveOffset + bytesRead < fileLength,
            BlockedByOversizedLine: false,
            lines);
    }
}
