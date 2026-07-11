using FluentAssertions;
using Octadock.Core.Context;
using Xunit;

namespace Octadock.Core.Tests.Context;

/// <summary>
/// The Context export planner (WS10). The hard invariant — excluding an item excludes ALL
/// of its derivatives (OCR is the stealth leak) — and relative-paths-only are proven here.
/// </summary>
public class ContextExporterTests
{
    private static readonly Guid ItemA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ItemB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ContextPackage Package() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Bug repro",
        CreatedAt = new DateTimeOffset(2026, 7, 7, 0, 0, 0, TimeSpan.Zero),
        Items = new List<ContextItem>
        {
            new()
            {
                Id = ItemA,
                DisplayName = "screenshot.png",
                Ownership = ContextOwnership.Snapshot,
                StorageRelativePath = "context/a/screenshot.png",
                SizeBytes = 1024,
                Derivatives = new List<ContextDerivative>
                {
                    new(ContextDerivativeKind.Ocr, "context/a/ocr.txt"),
                    new(ContextDerivativeKind.Thumbnail, "context/a/thumb.png"),
                },
            },
            new()
            {
                Id = ItemB,
                DisplayName = "big-video.mp4",
                Ownership = ContextOwnership.Reference,
                ReferenceSourcePath = @"C:\Users\alice\Videos\big-video.mp4",
                ReferenceSha256 = "abc123",
                SizeBytes = 80L * 1024 * 1024,
                Derivatives = new List<ContextDerivative>
                {
                    new(ContextDerivativeKind.Thumbnail, "context/b/thumb.png"),
                },
            },
        },
    };

    [Fact]
    public void Default_selection_includes_every_item_and_derivative()
    {
        ContextExportPlan plan = ContextExporter.BuildPlan(Package());

        // 2 primaries + 2 derivatives (A) + 1 derivative (B) = 5 entries.
        plan.Entries.Should().HaveCount(5);
        plan.Entries.Count(e => e.ItemId == ItemA).Should().Be(3);
        plan.Entries.Count(e => e.ItemId == ItemB).Should().Be(2);
    }

    [Fact]
    public void Excluding_an_item_excludes_all_of_its_derivatives()
    {
        var selection = new ContextExportSelection().ExcludeItem(ItemA);

        ContextExportPlan plan = ContextExporter.BuildPlan(Package(), selection);

        plan.Entries.Should().OnlyContain(e => e.ItemId == ItemB, "the excluded item and all its derivatives are gone");
        plan.Entries.Should().NotContain(e => e.Derivative == ContextDerivativeKind.Ocr,
            "the OCR text of an excluded item must never ship");
        plan.ManifestJson.Should().NotContain("ocr.txt");
    }

    [Fact]
    public void Excluding_a_single_derivative_keeps_the_item_and_other_derivatives()
    {
        var selection = new ContextExportSelection().ExcludeDerivative(ItemA, ContextDerivativeKind.Ocr);

        ContextExportPlan plan = ContextExporter.BuildPlan(Package(), selection);

        plan.Entries.Should().Contain(e => e.ItemId == ItemA && e.Derivative == null, "the item primary stays");
        plan.Entries.Should().Contain(e => e.ItemId == ItemA && e.Derivative == ContextDerivativeKind.Thumbnail);
        plan.Entries.Should().NotContain(e => e.ItemId == ItemA && e.Derivative == ContextDerivativeKind.Ocr);
    }

    [Fact]
    public void Every_package_path_is_relative_and_never_leaks_an_absolute_source()
    {
        ContextExportPlan plan = ContextExporter.BuildPlan(Package());

        foreach (ContextExportEntry entry in plan.Entries)
        {
            Path.IsPathRooted(entry.PackagePath).Should().BeFalse($"'{entry.PackagePath}' must be relative");
            entry.PackagePath.Should().NotContain("..").And.NotContain(":");
        }

        // The manifest records provenance (hash) but never the absolute referenced source path.
        plan.ManifestJson.Should().NotContain(@"C:\Users").And.NotContain("alice");
        plan.ManifestPackagePath.Should().Be("context-manifest.json");
    }

    [Fact]
    public void Reference_item_primary_reads_from_the_original_but_exports_to_a_relative_path()
    {
        ContextExportEntry primaryB = ContextExporter.BuildPlan(Package())
            .Entries.Single(e => e.ItemId == ItemB && e.Derivative == null);

        primaryB.Source.Should().Be(ContextExportSource.ReferenceOriginal);
        primaryB.SourceLocation.Should().Be(@"C:\Users\alice\Videos\big-video.mp4");
        primaryB.ExpectedSizeBytes.Should().Be(80L * 1024 * 1024);
        primaryB.ExpectedSha256.Should().Be("abc123");
        Path.IsPathRooted(primaryB.PackagePath).Should().BeFalse();
    }

    [Fact]
    public void Reference_without_a_source_path_stays_a_reference_so_export_can_fail_closed()
    {
        ContextPackage package = Package();
        ContextItem reference = package.Items.Single(i => i.Id == ItemB) with { ReferenceSourcePath = null };
        package = package with
        {
            Items = package.Items.Select(i => i.Id == ItemB ? reference : i).ToList(),
        };

        ContextExportEntry primary = ContextExporter.BuildPlan(package)
            .Entries.Single(e => e.ItemId == ItemB && e.Derivative == null);

        primary.Source.Should().Be(ContextExportSource.ReferenceOriginal);
        primary.SourceLocation.Should().BeEmpty();
        primary.ExpectedSizeBytes.Should().Be(80L * 1024 * 1024);
    }
}
