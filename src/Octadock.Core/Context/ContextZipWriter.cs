using System.IO;
using System.IO.Compression;
using System.Text;

namespace Octadock.Core.Context;

/// <summary>
/// Assembles a Context export <see cref="ContextExportPlan"/> into a zip stream (WS10).
/// Pure over an injected byte reader, so the invariant-respecting plan is what determines
/// the zip contents — an excluded item (and every derivative of it) is simply never read.
/// The manifest is written at the package root. The App layer supplies the reader (managed
/// storage / referenced original) and writes the finished zip atomically via SafeFileWriter.
/// </summary>
public static class ContextZipWriter
{
    /// <summary>Writes the export plan's manifest + entries into <paramref name="output"/> as a zip.</summary>
    public static void Write(
        ContextExportPlan plan,
        Func<ContextExportEntry, byte[]> readEntryBytes,
        Stream output)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(readEntryBytes);
        ArgumentNullException.ThrowIfNull(output);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        ZipArchiveEntry manifestEntry = archive.CreateEntry(plan.ManifestPackagePath, CompressionLevel.Optimal);
        using (Stream manifestStream = manifestEntry.Open())
        {
            byte[] manifestBytes = Encoding.UTF8.GetBytes(plan.ManifestJson);
            manifestStream.Write(manifestBytes, 0, manifestBytes.Length);
        }

        foreach (ContextExportEntry entry in plan.Entries)
        {
            ZipArchiveEntry zipEntry = archive.CreateEntry(entry.PackagePath, CompressionLevel.Optimal);
            using Stream entryStream = zipEntry.Open();
            byte[] bytes = readEntryBytes(entry);
            entryStream.Write(bytes, 0, bytes.Length);
        }
    }

    /// <summary>
    /// Asynchronously streams the export plan's manifest and entries into
    /// <paramref name="output"/>. The injected writer copies directly into each zip
    /// entry, avoiding a whole-file allocation for large referenced originals.
    /// </summary>
    public static async Task WriteAsync(
        ContextExportPlan plan,
        Func<ContextExportEntry, Stream, CancellationToken, Task> writeEntry,
        Stream output,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(writeEntry);
        ArgumentNullException.ThrowIfNull(output);

        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);

        ZipArchiveEntry manifestEntry = archive.CreateEntry(plan.ManifestPackagePath, CompressionLevel.Optimal);
        await using (Stream manifestStream = manifestEntry.Open())
        {
            byte[] manifestBytes = Encoding.UTF8.GetBytes(plan.ManifestJson);
            await manifestStream.WriteAsync(manifestBytes, cancellationToken).ConfigureAwait(false);
        }

        foreach (ContextExportEntry entry in plan.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ZipArchiveEntry zipEntry = archive.CreateEntry(entry.PackagePath, CompressionLevel.Optimal);
            await using Stream entryStream = zipEntry.Open();
            await writeEntry(entry, entryStream, cancellationToken).ConfigureAwait(false);
        }
    }
}
