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
}
