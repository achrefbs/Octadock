using System.IO;
using System.Text;

namespace Octadock.Core.Services;

/// <summary>
/// Shared windowed file reading for the text-based preview providers. Always
/// opens with ReadWrite|Delete share so a live log another process is writing
/// is never locked.
/// </summary>
internal static class PreviewTextReader
{
    /// <summary>Reads up to <paramref name="maxBytes"/> characters from the start of the file.</summary>
    public static async Task<(string Text, bool Truncated)> ReadHeadAsync(
        string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenShared(path);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // Read one char past the cap so "exactly max" and "truncated" differ.
        var window = new char[maxBytes + 1];
        int total = 0;
        while (total < window.Length)
        {
            int n = await reader
                .ReadAsync(window.AsMemory(total, window.Length - total), cancellationToken)
                .ConfigureAwait(false);
            if (n == 0)
            {
                break;
            }

            total += n;
        }

        bool truncated = total > maxBytes;
        return (new string(window, 0, truncated ? maxBytes : total), truncated);
    }

    /// <summary>
    /// Reads up to <paramref name="maxBytes"/> from the end of the file, starting
    /// at the first complete line inside the window. Returns the total file size
    /// so callers can describe what was skipped.
    /// </summary>
    public static async Task<(string Text, bool Truncated, long FileBytes)> ReadTailAsync(
        string path, int maxBytes, CancellationToken cancellationToken)
    {
        await using FileStream stream = OpenShared(path);
        long length = stream.Length;
        if (length <= maxBytes)
        {
            using var whole = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            string all = await whole.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return (all, false, length);
        }

        stream.Seek(length - maxBytes, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        string text = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        // Drop the (probably partial) first line so the window starts cleanly.
        int firstBreak = text.IndexOf('\n', StringComparison.Ordinal);
        if (firstBreak >= 0 && firstBreak + 1 < text.Length)
        {
            text = text[(firstBreak + 1)..];
        }

        return (text, true, length);
    }

    /// <summary>Formats a byte count as B/KB/MB for truncation notes.</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes} B"),
        < 1024 * 1024 => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB"),
        _ => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB"),
    };

    private static FileStream OpenShared(string path)
        => new(
            path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 64 * 1024, useAsync: true);
}
