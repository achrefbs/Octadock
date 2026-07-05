using Octadock.Core.Abstractions;
using Octadock.Core.Models;
using Octadock.Core.Persistence;

namespace Octadock.App.Services;

/// <summary>
/// Decorates the SQLite <see cref="IAiSessionRepository"/> so EVERY session
/// mutation — regardless of which service performed it (discovery, run/watch,
/// hook ingestion, exit watcher) — publishes on the
/// <see cref="IAiSessionChangeBus"/>. The overlay and windows subscribe to the
/// bus instead of polling, which is what makes status changes appear
/// immediately.
/// </summary>
public sealed class NotifyingAiSessionRepository : IAiSessionRepository
{
    private readonly IAiSessionRepository _inner;
    private readonly IAiSessionChangeBus _bus;

    /// <summary>Wraps the inner repository.</summary>
    public NotifyingAiSessionRepository(IAiSessionRepository inner, IAiSessionChangeBus bus)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
    }

    /// <inheritdoc />
    public async Task AddAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
    {
        await _inner.AddAsync(record, cancellationToken).ConfigureAwait(false);
        _bus.Publish();
    }

    /// <inheritdoc />
    public async Task UpdateAsync(AiSessionRecord record, CancellationToken cancellationToken = default)
    {
        await _inner.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        _bus.Publish();
    }

    /// <inheritdoc />
    public Task<AiSessionRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => _inner.GetAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AiSessionRecord>> ListAsync(
        AiSessionFilter filter, CancellationToken cancellationToken = default)
        => _inner.ListAsync(filter, cancellationToken);

    /// <inheritdoc />
    public async Task AddEventAsync(AiSessionEventRecord record, CancellationToken cancellationToken = default)
    {
        await _inner.AddEventAsync(record, cancellationToken).ConfigureAwait(false);
        _bus.Publish();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiSessionEventRecord>> GetEventsAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
        => _inner.GetEventsAsync(sessionId, cancellationToken);

    /// <inheritdoc />
    public async Task AddArtifactAsync(AiSessionArtifactRecord record, CancellationToken cancellationToken = default)
    {
        await _inner.AddArtifactAsync(record, cancellationToken).ConfigureAwait(false);
        _bus.Publish();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AiSessionArtifactRecord>> GetArtifactsAsync(
        Guid sessionId, CancellationToken cancellationToken = default)
        => _inner.GetArtifactsAsync(sessionId, cancellationToken);
}
