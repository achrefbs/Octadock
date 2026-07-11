using System.Text;
using FluentAssertions;

namespace Octadock.WorkflowIntelligence.Internal.Tests;

public sealed class EncryptedRawTraceStoreTests
{
    [Fact]
    public async Task Store_round_trips_content_without_plaintext_at_rest()
    {
        using var consent = new FullConsentScope();
        using var directory = new TestDirectory();
        var paths = new TracePaths(directory.Path);
        var store = new EncryptedRawTraceStore(paths);
        string secret = $"sensitive-hook-content-{Guid.NewGuid():N}";
        DateTimeOffset created = DateTimeOffset.UtcNow;

        Guid id = await store.StoreAsync("claude-hook-message", secret, created, created.AddDays(7));
        RawTraceContent? restored = await store.GetAsync(id);

        restored.Should().NotBeNull();
        restored!.Content.Should().Be(secret);
        string databaseBytesAsText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(paths.DatabasePath));
        string protectedKeyAsText = Encoding.UTF8.GetString(await File.ReadAllBytesAsync(paths.KeyPath));
        databaseBytesAsText.Should().NotContain(secret);
        protectedKeyAsText.Should().NotContain(secret);
    }

    [Fact]
    public async Task Purge_removes_only_expired_content()
    {
        using var consent = new FullConsentScope();
        using var directory = new TestDirectory();
        var store = new EncryptedRawTraceStore(new TracePaths(directory.Path));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid expired = await store.StoreAsync("codex", "expired", now.AddDays(-2), now.AddDays(-1));
        Guid active = await store.StoreAsync("codex", "active", now, now.AddDays(1));

        int count = await store.PurgeExpiredAsync(now);

        count.Should().Be(1);
        (await store.GetAsync(expired)).Should().BeNull();
        (await store.GetAsync(active))!.Content.Should().Be("active");
    }

    [Fact]
    public async Task Delete_all_removes_database_key_sidecars_exports_and_quarantine_files()
    {
        using var consent = new FullConsentScope();
        using var directory = new TestDirectory();
        var paths = new TracePaths(directory.Path);
        var store = new EncryptedRawTraceStore(paths);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = await store.StoreAsync("claude", "raw", now, now.AddDays(1));
        await File.WriteAllTextAsync(Path.Combine(paths.Root, "workflow-traces.internal.db.corrupt-test"), "ciphertext-only");
        await File.WriteAllTextAsync(Path.Combine(paths.TempPath, "temp.bin"), "temp");
        await File.WriteAllTextAsync(Path.Combine(paths.ExportPath, "export.bin"), "export");

        await store.DeleteAllAsync();

        Directory.Exists(paths.Root).Should().BeFalse();
    }

    [Fact]
    public async Task Delete_all_preserves_files_not_owned_by_the_trace_store()
    {
        using var consent = new FullConsentScope();
        using var directory = new TestDirectory();
        var paths = new TracePaths(directory.Path);
        var store = new EncryptedRawTraceStore(paths);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _ = await store.StoreAsync("claude", "raw", now, now.AddDays(1));
        string unrelated = Path.Combine(paths.Root, "keep-me.txt");
        await File.WriteAllTextAsync(unrelated, "not owned by the trace store");

        await store.DeleteAllAsync();

        File.Exists(unrelated).Should().BeTrue();
        File.Exists(paths.DatabasePath).Should().BeFalse();
        File.Exists(paths.KeyPath).Should().BeFalse();
    }

    [Fact]
    public async Task Raw_store_fails_closed_without_explicit_full_consent()
    {
        string? previous = Environment.GetEnvironmentVariable(InternalConsent.FullConsentEnvironmentVariable);
        Environment.SetEnvironmentVariable(InternalConsent.FullConsentEnvironmentVariable, null);
        try
        {
            using var directory = new TestDirectory();
            var store = new EncryptedRawTraceStore(new TracePaths(directory.Path));
            Func<Task> action = () => store.InitializeAsync();

            await action.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*Full developer trace is disabled*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(InternalConsent.FullConsentEnvironmentVariable, previous);
        }
    }

    [Fact]
    public async Task Store_once_is_idempotent_for_duplicate_completion_events()
    {
        using var consent = new FullConsentScope();
        using var directory = new TestDirectory();
        var paths = new TracePaths(directory.Path);
        var store = new EncryptedRawTraceStore(paths);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Guid stableId = Guid.NewGuid();

        RawTraceStoreResult first = await store.StoreOnceAsync(
            stableId, "trusted-brief", "first", now, now.AddDays(1));
        RawTraceStoreResult duplicate = await store.StoreOnceAsync(
            stableId, "trusted-brief", "different duplicate", now.AddSeconds(1), now.AddDays(1));

        first.Created.Should().BeTrue();
        duplicate.Created.Should().BeFalse();
        duplicate.Id.Should().Be(first.Id);
        (await store.GetAsync(stableId))!.Content.Should().Be("first");
    }
}
