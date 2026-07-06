using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using Octadock.Core.Context;
using Xunit;

namespace Octadock.Core.Tests.Context;

/// <summary>The Context export assembles into a zip that honors the selection invariant end-to-end (WS10).</summary>
public class ContextZipWriterTests
{
    private static readonly Guid ItemA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid ItemB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static ContextPackage Package() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Repro",
        CreatedAt = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero),
        Items = new List<ContextItem>
        {
            new()
            {
                Id = ItemA, DisplayName = "a.png", Ownership = ContextOwnership.Snapshot,
                StorageRelativePath = "Context/a/a.png", SizeBytes = 3,
                Derivatives = new List<ContextDerivative> { new(ContextDerivativeKind.Ocr, "Context/a/ocr.txt") },
            },
            new()
            {
                Id = ItemB, DisplayName = "b.png", Ownership = ContextOwnership.Snapshot,
                StorageRelativePath = "Context/b/b.png", SizeBytes = 3,
            },
        },
    };

    private static Dictionary<string, string> ReadZipEntries(byte[] zipBytes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            result[entry.FullName] = reader.ReadToEnd();
        }

        return result;
    }

    private static byte[] Export(ContextExportSelection? selection)
    {
        ContextExportPlan plan = ContextExporter.BuildPlan(Package(), selection);
        using var buffer = new MemoryStream();
        ContextZipWriter.Write(plan, e => Encoding.UTF8.GetBytes($"bytes:{e.PackagePath}"), buffer);
        return buffer.ToArray();
    }

    [Fact]
    public void Writes_the_manifest_and_every_included_entry()
    {
        Dictionary<string, string> entries = ReadZipEntries(Export(selection: null));

        entries.Should().ContainKey("context-manifest.json");
        entries.Keys.Should().Contain(k => k.Contains("/a.png"))
            .And.Contain(k => k.Contains("/ocr.txt"))
            .And.Contain(k => k.Contains("/b.png"));
        entries["context-manifest.json"].Should().Contain("\"name\": \"Repro\"");
    }

    [Fact]
    public void An_excluded_item_and_its_derivatives_are_absent_from_the_zip()
    {
        Dictionary<string, string> entries = ReadZipEntries(Export(new ContextExportSelection().ExcludeItem(ItemA)));

        entries.Keys.Should().NotContain(k => k.Contains("/a.png"));
        entries.Keys.Should().NotContain(k => k.Contains("/ocr.txt"), "an excluded item's OCR must never be zipped");
        entries.Keys.Should().Contain(k => k.Contains("/b.png"));
    }
}
