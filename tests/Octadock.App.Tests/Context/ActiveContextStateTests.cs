using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Context;
using Octadock.App.Services;

using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Context;
using Octadock.Core.Io;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Xunit;

namespace Octadock.App.Tests.Context;

public sealed class ActiveContextStateTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "octadock-active-context-" + Guid.NewGuid().ToString("N"));

    public ActiveContextStateTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [Fact]
    public void Active_context_label_visibly_names_the_destination()
    {
        var state = new ActiveContextState();
        var package = Package("Launch handoff");

        state.SetActive(package);

        state.HasActiveContext.Should().BeTrue();
        state.AddActionLabel.Should().Be("Add to Context · Launch handoff");
        state.StatusLabel.Should().Be("Active Context: Launch handoff");

        state.Clear();
        state.HasActiveContext.Should().BeFalse();
        state.AddActionLabel.Should().Be("Choose a Context…");
    }

    [Fact]
    public async Task Late_resolution_of_old_context_cannot_overwrite_new_active_destination()
    {
        ContextPackage first = Package("First");
        ContextPackage second = Package("Second");
        var repository = new DelayedContextRepository(first, second);
        ContextService context = BuildContextService(repository);
        var state = new ActiveContextState();
        state.SetActive(first);

        Task<ContextPackage?> resolving = state.ResolveAsync(context);
        await repository.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        state.SetActive(second);
        repository.ReleaseFirstRead.TrySetResult();

        ContextPackage? resolved = await resolving;
        resolved!.Id.Should().Be(second.Id);
        state.ActivePackageId.Should().Be(second.Id);
        state.AddActionLabel.Should().Contain("Second");
    }

    [Fact]
    public async Task Startup_restores_only_the_last_explicit_active_context()
    {
        ContextPackage first = Package("First");
        ContextPackage second = Package("Second");
        var repository = new DelayedContextRepository(first, second);
        var store = new InMemorySettingsStore();
        await store.SetAsync("context.active-package-id", second.Id.ToString("D"));
        var state = new ActiveContextState(store);

        await state.InitializeAsync(BuildContextService(repository));

        state.ActivePackageId.Should().Be(second.Id);
        state.AddActionLabel.Should().Be("Add to Context · Second");
    }

    [Fact]
    public async Task Startup_context_restore_is_best_effort_when_settings_are_unavailable()
    {
        ContextPackage package = Package("Available later");
        var repository = new DelayedContextRepository(package);
        var state = new ActiveContextState(new ThrowingSettingsStore());

        Func<Task> initialize = () => state.InitializeAsync(BuildContextService(repository));

        await initialize.Should().NotThrowAsync();
        state.HasActiveContext.Should().BeFalse();
        state.StatusLabel.Should().Be("No active Context");
    }

    private ContextService BuildContextService(IContextRepository repository)
    {
        var paths = new StoragePaths(_root);
        paths.EnsureDirectories();
        return new ContextService(
            repository,
            paths,
            new SafeFileWriter(new FileRevisionStore(Path.Combine(_root, "revisions"))),
            new NoopNotifications(),
            SystemClock.Instance,
            NullLogger<ContextService>.Instance);
    }

    private static ContextPackage Package(string name)
        => new() { Id = Guid.NewGuid(), Name = name, CreatedAt = DateTimeOffset.UtcNow };

    private sealed class DelayedContextRepository(params ContextPackage[] packages) : IContextRepository
    {
        private readonly Dictionary<Guid, ContextPackage> _packages = packages.ToDictionary(package => package.Id);
        private readonly Guid _delayedId = packages[0].Id;

        public TaskCompletionSource FirstReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRead { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<ContextPackage> CreatePackageAsync(string name, DateTimeOffset now, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task RenamePackageAsync(Guid id, string name, DateTimeOffset now, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdatePackageNotesAsync(Guid id, string notes, DateTimeOffset now, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task DeletePackageAsync(Guid id, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ContextPackage>>(_packages.Values.ToList());

        public async Task<ContextPackage?> GetPackageAsync(Guid id, CancellationToken ct = default)
        {
            if (id == _delayedId)
            {
                FirstReadStarted.TrySetResult();
                await ReleaseFirstRead.Task.WaitAsync(ct);
            }

            return _packages.GetValueOrDefault(id);
        }

        public Task AddItemAsync(Guid packageId, ContextItem item, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task RemoveItemAsync(Guid itemId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task ReorderItemsAsync(Guid packageId, IReadOnlyList<Guid> orderedItemIds, DateTimeOffset now, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoopNotifications : INotificationService
    {
        public void Notify(
            string title,
            string message,
            NotificationKind kind = NotificationKind.Info,
            Action? clickAction = null)
        {
        }
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>(_values));

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task SetManyAsync(
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
        {
            foreach ((string key, string value) in values)
            {
                _values[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingSettingsStore : ISettingsStore
    {
        private static Task<T> Unavailable<T>()
            => Task.FromException<T>(new IOException("settings unavailable"));

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(
            CancellationToken cancellationToken = default)
            => Unavailable<IReadOnlyDictionary<string, string>>();

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Unavailable<string?>();

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
            => Unavailable<object?>();

        public Task SetManyAsync(
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
            => Unavailable<object?>();

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
            => Unavailable<object?>();
    }
}
