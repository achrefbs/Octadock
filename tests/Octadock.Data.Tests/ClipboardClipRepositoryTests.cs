using FluentAssertions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class ClipboardClipRepositoryTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Add_then_Get_round_trips_text_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clip = TextClip(
            createdAt: BaseTime,
            lastSeenAt: BaseTime.AddMinutes(2),
            text: "ship clipboard history",
            contentHash: "sha256-text",
            isFavorite: true);

        await db.ClipboardClips.AddAsync(clip);
        var loaded = await db.ClipboardClips.GetAsync(clip.Id);

        loaded.Should().NotBeNull();
        loaded!.Should().BeEquivalentTo(clip);
        loaded!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Add_then_Get_round_trips_image_fields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clip = ImageClip(
            imagePath: "Clipboard\\2026\\07\\03\\image.png",
            thumbnailPath: "Thumbnails\\clipboard-image.jpg",
            contentHash: "sha256-image");

        await db.ClipboardClips.AddAsync(clip);
        var loaded = await db.ClipboardClips.GetAsync(clip.Id);

        loaded.Should().NotBeNull();
        loaded!.Text.Should().BeNull();
        loaded.Should().BeEquivalentTo(clip);
    }

    [Fact]
    public async Task Created_and_last_seen_are_normalized_to_utc_on_round_trip()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clip = TextClip(
            createdAt: new DateTimeOffset(2026, 7, 3, 12, 0, 0, TimeSpan.FromHours(2)),
            lastSeenAt: new DateTimeOffset(2026, 7, 3, 12, 5, 0, TimeSpan.FromHours(2)));

        await db.ClipboardClips.AddAsync(clip);
        var loaded = await db.ClipboardClips.GetAsync(clip.Id);

        loaded!.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        loaded.LastSeenAt.Offset.Should().Be(TimeSpan.Zero);
        loaded.CreatedAt.Should().Be(clip.CreatedAt.ToUniversalTime());
        loaded.LastSeenAt.Should().Be(clip.LastSeenAt.ToUniversalTime());
    }

    [Fact]
    public async Task Get_returns_null_for_unknown_id()
    {
        await using var db = await TestDatabase.CreateAsync();

        var loaded = await db.ClipboardClips.GetAsync(Guid.NewGuid());

        loaded.Should().BeNull();
    }

    [Fact]
    public async Task Update_replaces_stored_record()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clip = TextClip(text: "first", contentHash: "sha256-first");
        await db.ClipboardClips.AddAsync(clip);

        var updated = clip with
        {
            LastSeenAt = BaseTime.AddHours(1),
            SeenCount = 3,
            Text = "updated",
            ContentHash = "sha256-updated",
            IsFavorite = true,
            MetadataJson = "{\"formats\":[\"Unicode Text\"]}",
        };
        await db.ClipboardClips.UpdateAsync(updated);

        var loaded = await db.ClipboardClips.GetAsync(clip.Id);
        loaded!.Should().BeEquivalentTo(updated);
    }

    [Fact]
    public async Task GetRecent_returns_newest_last_seen_first_and_excludes_deleted()
    {
        await using var db = await TestDatabase.CreateAsync();
        var oldest = TextClip(lastSeenAt: BaseTime, contentHash: "old");
        var middle = TextClip(lastSeenAt: BaseTime.AddMinutes(1), contentHash: "mid");
        var newest = TextClip(lastSeenAt: BaseTime.AddMinutes(2), contentHash: "new");
        var deleted = TextClip(
            lastSeenAt: BaseTime.AddMinutes(3),
            contentHash: "deleted",
            deletedAt: BaseTime.AddMinutes(4));

        foreach (var clip in new[] { oldest, middle, newest, deleted })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        var recent = await db.ClipboardClips.GetRecentAsync(2);

        recent.Select(c => c.Id).Should().ContainInOrder(newest.Id, middle.Id);
        recent.Should().HaveCount(2);
        recent.Should().NotContain(c => c.Id == deleted.Id);
    }

    [Fact]
    public async Task Query_filters_by_kind_date_range_last_seen_and_favorite()
    {
        await using var db = await TestDatabase.CreateAsync();
        var before = TextClip(
            createdAt: BaseTime.AddDays(-2),
            lastSeenAt: BaseTime.AddDays(-2),
            contentHash: "before",
            isFavorite: true);
        var inRange = TextClip(
            createdAt: BaseTime,
            lastSeenAt: BaseTime.AddHours(1),
            contentHash: "in-range",
            isFavorite: true);
        var wrongKind = ImageClip(
            createdAt: BaseTime,
            lastSeenAt: BaseTime.AddHours(1),
            contentHash: "wrong-kind");
        var notFavorite = TextClip(
            createdAt: BaseTime,
            lastSeenAt: BaseTime.AddHours(1),
            contentHash: "not-favorite");

        foreach (var clip in new[] { before, inRange, wrongKind, notFavorite })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        var result = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter
        {
            Kinds = new[] { ClipboardClipKind.Text },
            CreatedAfter = BaseTime.AddDays(-1),
            CreatedBefore = BaseTime.AddDays(1),
            LastSeenAfter = BaseTime.AddMinutes(30),
            LastSeenBefore = BaseTime.AddHours(2),
            IsFavorite = true,
        });

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(inRange.Id);
    }

    [Fact]
    public async Task Query_search_matches_text_source_window_or_format()
    {
        await using var db = await TestDatabase.CreateAsync();
        var text = TextClip(
            text: "Azure deployment token",
            contentHash: "text",
            sourceProcess: null,
            sourceWindow: null);
        var source = TextClip(
            text: "editor clipboard",
            contentHash: "source",
            sourceProcess: "Code.exe",
            sourceWindow: null);
        var window = TextClip(
            text: "build logs",
            contentHash: "window",
            sourceProcess: null,
            sourceWindow: "Terminal build output");
        var format = ImageClip(formatName: "image/png", contentHash: "format");
        var unrelated = TextClip(
            text: "nothing to see",
            contentHash: "unrelated",
            sourceProcess: "explorer.exe",
            sourceWindow: "notes");

        foreach (var clip in new[] { text, source, window, format, unrelated })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { SearchText = "token" }))
            .Select(c => c.Id).Should().ContainSingle().Which.Should().Be(text.Id);
        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { SearchText = "code" }))
            .Select(c => c.Id).Should().ContainSingle().Which.Should().Be(source.Id);
        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { SearchText = "terminal" }))
            .Select(c => c.Id).Should().ContainSingle().Which.Should().Be(window.Id);
        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { SearchText = "png" }))
            .Select(c => c.Id).Should().ContainSingle().Which.Should().Be(format.Id);
    }

    [Fact]
    public async Task Query_search_treats_wildcards_as_literals()
    {
        await using var db = await TestDatabase.CreateAsync();
        var literal = TextClip(text: "Use 100% local storage", contentHash: "literal");
        var wouldMatchWildcard = TextClip(text: "Use 100x local storage", contentHash: "wildcard");
        await db.ClipboardClips.AddAsync(literal);
        await db.ClipboardClips.AddAsync(wouldMatchWildcard);

        var result = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { SearchText = "100%" });

        result.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(literal.Id);
    }

    [Fact]
    public async Task Query_includes_deleted_only_when_requested()
    {
        await using var db = await TestDatabase.CreateAsync();
        var live = TextClip(contentHash: "live");
        var deleted = TextClip(contentHash: "deleted", deletedAt: BaseTime.AddMinutes(5));
        await db.ClipboardClips.AddAsync(live);
        await db.ClipboardClips.AddAsync(deleted);

        var defaultResult = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter());
        var withDeleted = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter { IncludeDeleted = true });

        defaultResult.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(live.Id);
        withDeleted.Select(c => c.Id).Should().BeEquivalentTo(new[] { live.Id, deleted.Id });
    }

    [Fact]
    public async Task Query_sorts_and_pages()
    {
        await using var db = await TestDatabase.CreateAsync();
        var first = TextClip(createdAt: BaseTime, lastSeenAt: BaseTime, contentHash: "first");
        var second = TextClip(createdAt: BaseTime.AddMinutes(1), lastSeenAt: BaseTime.AddMinutes(1), contentHash: "second");
        var third = TextClip(createdAt: BaseTime.AddMinutes(2), lastSeenAt: BaseTime.AddMinutes(2), contentHash: "third");
        foreach (var clip in new[] { second, third, first })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        var newest = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter());
        var oldestPage = await db.ClipboardClips.QueryAsync(new ClipboardClipFilter
        {
            SortOrder = ClipboardClipSortOrder.OldestFirst,
            Limit = 1,
            Offset = 1,
        });

        newest.Select(c => c.Id).Should().ContainInOrder(third.Id, second.Id, first.Id);
        oldestPage.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(second.Id);
    }

    [Fact]
    public async Task Count_honors_filter()
    {
        await using var db = await TestDatabase.CreateAsync();
        await db.ClipboardClips.AddAsync(TextClip(contentHash: "text-a"));
        await db.ClipboardClips.AddAsync(TextClip(contentHash: "text-b"));
        await db.ClipboardClips.AddAsync(ImageClip(contentHash: "image"));
        await db.ClipboardClips.AddAsync(TextClip(contentHash: "deleted", deletedAt: BaseTime.AddMinutes(5)));

        (await db.ClipboardClips.CountAsync(new ClipboardClipFilter())).Should().Be(3);
        (await db.ClipboardClips.CountAsync(new ClipboardClipFilter
        {
            Kinds = new[] { ClipboardClipKind.Text },
        })).Should().Be(2);
        (await db.ClipboardClips.CountAsync(new ClipboardClipFilter { IncludeDeleted = true })).Should().Be(4);
    }

    [Fact]
    public async Task GetLatestByContentHash_returns_newest_live_matching_clip()
    {
        await using var db = await TestDatabase.CreateAsync();
        var deletedNewest = TextClip(
            lastSeenAt: BaseTime.AddMinutes(3),
            contentHash: "same",
            deletedAt: BaseTime.AddMinutes(4));
        var older = TextClip(lastSeenAt: BaseTime, contentHash: "same");
        var newer = TextClip(lastSeenAt: BaseTime.AddMinutes(2), contentHash: "same");
        var wrongKind = ImageClip(lastSeenAt: BaseTime.AddMinutes(5), contentHash: "same");
        foreach (var clip in new[] { deletedNewest, older, newer, wrongKind })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        var loaded = await db.ClipboardClips.GetLatestByContentHashAsync(ClipboardClipKind.Text, "same");

        loaded!.Id.Should().Be(newer.Id);
    }

    [Fact]
    public async Task SoftDelete_restore_and_hard_delete_update_visibility()
    {
        await using var db = await TestDatabase.CreateAsync();
        var clip = TextClip(contentHash: "delete-me");
        await db.ClipboardClips.AddAsync(clip);

        await db.ClipboardClips.SoftDeleteAsync(clip.Id, BaseTime.AddMinutes(1));
        (await db.ClipboardClips.GetAsync(clip.Id))!.IsDeleted.Should().BeTrue();
        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter())).Should().BeEmpty();

        await db.ClipboardClips.RestoreAsync(clip.Id);
        (await db.ClipboardClips.GetAsync(clip.Id))!.IsDeleted.Should().BeFalse();
        (await db.ClipboardClips.QueryAsync(new ClipboardClipFilter())).Should().ContainSingle();

        await db.ClipboardClips.HardDeleteAsync(clip.Id);
        (await db.ClipboardClips.GetAsync(clip.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Retention_helpers_return_live_old_and_soft_deleted_expired_clips()
    {
        await using var db = await TestDatabase.CreateAsync();
        var liveOld = TextClip(lastSeenAt: BaseTime.AddDays(-10), contentHash: "live-old");
        var liveNew = TextClip(lastSeenAt: BaseTime, contentHash: "live-new");
        var deletedExpired = TextClip(
            lastSeenAt: BaseTime,
            contentHash: "deleted-expired",
            deletedAt: BaseTime.AddDays(-3));
        var deletedRecent = TextClip(
            lastSeenAt: BaseTime,
            contentHash: "deleted-recent",
            deletedAt: BaseTime);

        foreach (var clip in new[] { liveOld, liveNew, deletedExpired, deletedRecent })
        {
            await db.ClipboardClips.AddAsync(clip);
        }

        var oldLive = await db.ClipboardClips.GetOlderThanAsync(BaseTime.AddDays(-1));
        var expiredDeleted = await db.ClipboardClips.GetSoftDeletedBeforeAsync(BaseTime.AddDays(-1));

        oldLive.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(liveOld.Id);
        expiredDeleted.Select(c => c.Id).Should().ContainSingle().Which.Should().Be(deletedExpired.Id);
    }

    private static ClipboardClipRecord TextClip(
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastSeenAt = null,
        string text = "hello from clipboard",
        string? contentHash = null,
        string? sourceProcess = "code.exe",
        string? sourceWindow = "Program.cs",
        bool isFavorite = false,
        DateTimeOffset? deletedAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Kind = ClipboardClipKind.Text,
        CreatedAt = createdAt ?? BaseTime,
        LastSeenAt = lastSeenAt ?? createdAt ?? BaseTime,
        SeenCount = 1,
        SourceProcess = sourceProcess,
        SourceWindow = sourceWindow,
        FormatName = "text/plain",
        Text = text,
        ContentHash = contentHash ?? $"sha256-{Guid.NewGuid():N}",
        SizeBytes = text.Length,
        IsFavorite = isFavorite,
        DeletedAt = deletedAt,
        MetadataJson = "{\"formats\":[\"Unicode Text\"]}",
    };

    private static ClipboardClipRecord ImageClip(
        Guid? id = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? lastSeenAt = null,
        string imagePath = "Clipboard\\2026\\07\\03\\image.png",
        string thumbnailPath = "Thumbnails\\clipboard-image.jpg",
        string? contentHash = null,
        string? formatName = "image/png",
        DateTimeOffset? deletedAt = null) => new()
    {
        Id = id ?? Guid.NewGuid(),
        Kind = ClipboardClipKind.Image,
        CreatedAt = createdAt ?? BaseTime,
        LastSeenAt = lastSeenAt ?? createdAt ?? BaseTime,
        SeenCount = 1,
        FormatName = formatName,
        ImagePath = imagePath,
        ThumbnailPath = thumbnailPath,
        ContentHash = contentHash ?? $"sha256-{Guid.NewGuid():N}",
        SizeBytes = 4096,
        DeletedAt = deletedAt,
        MetadataJson = "{\"formats\":[\"PNG\"]}",
    };
}
