using FluentAssertions;
using Octadock.Core.Context;
using Octadock.Data.Tests.Infrastructure;
using Xunit;

namespace Octadock.Data.Tests;

/// <summary>
/// Context persistence (WS10): round-trip with derivatives, cascade on removal, and the
/// key invariant that a Context item survives its source capture being discarded.
/// </summary>
public sealed class ContextRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 7, 12, 0, 0, TimeSpan.Zero);

    private static ContextItem Item(Guid? sourceCapture = null) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = "screenshot.png",
        Ownership = ContextOwnership.Snapshot,
        StorageRelativePath = "Context/a/screenshot.png",
        SizeBytes = 2048,
        SourceCaptureId = sourceCapture,
        AddedAt = Now,
        Derivatives = new List<ContextDerivative>
        {
            new(ContextDerivativeKind.Ocr, "Context/a/ocr.txt"),
            new(ContextDerivativeKind.Thumbnail, "Context/a/thumb.png"),
        },
    };

    [Fact]
    public async Task Round_trips_a_package_with_items_and_derivatives()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        ContextPackage package = await db.Context.CreatePackageAsync("Bug repro", Now);
        ContextItem item = Item();
        await db.Context.AddItemAsync(package.Id, item);

        ContextPackage loaded = (await db.Context.GetPackageAsync(package.Id))!;

        loaded.Name.Should().Be("Bug repro");
        loaded.Items.Should().ContainSingle();
        ContextItem back = loaded.Items[0];
        back.Id.Should().Be(item.Id);
        back.DisplayName.Should().Be("screenshot.png");
        back.Ownership.Should().Be(ContextOwnership.Snapshot);
        back.Derivatives.Select(d => d.Kind).Should()
            .BeEquivalentTo(new[] { ContextDerivativeKind.Ocr, ContextDerivativeKind.Thumbnail });
    }

    [Fact]
    public async Task Removing_an_item_cascades_to_its_derivatives()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        ContextPackage package = await db.Context.CreatePackageAsync("P", Now);
        ContextItem item = Item();
        await db.Context.AddItemAsync(package.Id, item);

        await db.Context.RemoveItemAsync(item.Id);

        (await db.Context.GetPackageAsync(package.Id))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_package_removes_its_items()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        ContextPackage package = await db.Context.CreatePackageAsync("P", Now);
        await db.Context.AddItemAsync(package.Id, Item());

        await db.Context.DeletePackageAsync(package.Id);

        (await db.Context.GetPackageAsync(package.Id)).Should().BeNull();
        (await db.Context.GetPackagesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_context_item_survives_its_source_capture_being_discarded()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        var capture = RecordFactory.MinimalCapture();
        await db.Captures.AddAsync(capture);

        ContextPackage package = await db.Context.CreatePackageAsync("P", Now);
        ContextItem item = Item(sourceCapture: capture.Id);
        await db.Context.AddItemAsync(package.Id, item);

        // Hard-delete the capture (retention/discard). The Context item must remain,
        // with its provenance link cleared to null — its bytes live in managed storage.
        await db.Captures.HardDeleteAsync(capture.Id);

        ContextItem survivor = (await db.Context.GetPackageAsync(package.Id))!.Items.Should().ContainSingle().Subject;
        survivor.Id.Should().Be(item.Id);
        survivor.SourceCaptureId.Should().BeNull("the capture link is set to null, never cascaded");
        survivor.StorageRelativePath.Should().Be("Context/a/screenshot.png");
    }

    [Fact]
    public async Task Get_packages_returns_newest_first()
    {
        await using TestDatabase db = await TestDatabase.CreateAsync();
        await db.Context.CreatePackageAsync("older", Now);
        await db.Context.CreatePackageAsync("newer", Now.AddMinutes(5));

        IReadOnlyList<ContextPackage> packages = await db.Context.GetPackagesAsync();

        packages.Select(p => p.Name).Should().ContainInOrder("newer", "older");
    }
}
