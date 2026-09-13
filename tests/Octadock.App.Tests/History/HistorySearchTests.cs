using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.History;
using Octadock.Core.Models;
using Octadock.Core.Persistence;
using Xunit;

namespace Octadock.App.Tests.History;

public sealed class HistorySearchTests
{
    [Fact]
    public void Editing_and_clearing_search_refreshes_without_pressing_enter()
    {
        var repository = new SearchRepository();
        HistoryViewModel viewModel = CreateViewModel(repository);

        viewModel.SearchText = "  Browser  ";
        repository.Queries.Should().ContainSingle().Which.SearchText.Should().Be("Browser");
        viewModel.IsBusy.Should().BeFalse();

        viewModel.SearchText = string.Empty;
        repository.Queries.Should().HaveCount(2);
        repository.Queries[1].SearchText.Should().BeNull();
        repository.Queries[1].Offset.Should().Be(0);
    }

    [Fact]
    public async Task A_slow_previous_query_cannot_repopulate_a_new_search()
    {
        var previousResult = new TaskCompletionSource<IReadOnlyList<CaptureRecord>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var repository = new SearchRepository { FirstResult = previousResult.Task };
        HistoryViewModel viewModel = CreateViewModel(repository);
        Task previousRefresh = viewModel.RefreshAsync();

        viewModel.SearchText = "new search";
        viewModel.IsBusy.Should().BeFalse();
        previousResult.SetResult([new CaptureRecord
        {
            Id = Guid.NewGuid(), Type = CaptureType.Area,
            CreatedAt = DateTimeOffset.UtcNow, OriginalPath = "Captures/old.png",
        }]);
        await previousRefresh;

        viewModel.Items.Should().BeEmpty();
        viewModel.StatusMessage.Should().Be("No captures match your filters.");
    }

    // Empty/stale results never resolve image, clipboard or window services.
    private static HistoryViewModel CreateViewModel(ICaptureRepository repository) => new(
        repository, null!, null!, null!, null!, null!, null!, null!,
        NullLogger<HistoryViewModel>.Instance);

    private sealed class SearchRepository : ICaptureRepository
    {
        public List<CaptureFilter> Queries { get; } = [];
        public Task<IReadOnlyList<CaptureRecord>>? FirstResult { get; init; }
        public Task<IReadOnlyList<CaptureRecord>> QueryAsync(CaptureFilter filter, CancellationToken cancellationToken = default)
        {
            Queries.Add(filter);
            return Queries.Count == 1 && FirstResult is not null
                ? FirstResult : Task.FromResult<IReadOnlyList<CaptureRecord>>([]);
        }

        public Task AddAsync(CaptureRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<CaptureRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<int> CountAsync(CaptureFilter filter, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CaptureRecord>> GetRecentAsync(int count, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdateAsync(CaptureRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SoftDeleteAsync(Guid id, DateTimeOffset deletedAt, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RestoreAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task HardDeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CaptureRecord>> GetOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<CaptureRecord>> GetSoftDeletedBeforeAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
