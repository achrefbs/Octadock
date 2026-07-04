using Octadock.Core.Persistence;

namespace Octadock.Core.Tests.Fakes;

/// <summary>A simple in-memory <see cref="ISettingsStore"/> backed by a dictionary, for tests.</summary>
internal sealed class InMemorySettingsStore : ISettingsStore
{
    private readonly Dictionary<string, string> _data;

    public InMemorySettingsStore(IReadOnlyDictionary<string, string>? seed = null)
        => _data = seed is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(seed, StringComparer.Ordinal);

    /// <summary>Direct access to the backing dictionary for assertions.</summary>
    public IReadOnlyDictionary<string, string> Snapshot => _data;

    public int SetManyCallCount { get; private set; }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string>(_data, StringComparer.Ordinal));

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
        => Task.FromResult(_data.TryGetValue(key, out string? v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        _data[key] = value;
        return Task.CompletedTask;
    }

    public Task SetManyAsync(IReadOnlyDictionary<string, string> values, CancellationToken cancellationToken = default)
    {
        SetManyCallCount++;
        foreach (KeyValuePair<string, string> pair in values)
        {
            _data[pair.Key] = pair.Value;
        }

        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        _data.Remove(key);
        return Task.CompletedTask;
    }
}
