using CommunityToolkit.Mvvm.ComponentModel;
using Octadock.App.Services;
using Octadock.Core.Context;
using Octadock.Core.Persistence;

namespace Octadock.App.Context;

/// <summary>
/// Shared in-process destination for every explicit "Add to Context" action.
/// It never guesses among multiple packages and validates a stale id before use.
/// </summary>
public sealed partial class ActiveContextState : ObservableObject
{
    private const string ActivePackageSettingKey = "context.active-package-id";
    private readonly ISettingsStore? _settingsStore;
    private readonly object _persistenceGate = new();
    private Task _persistenceTail = Task.CompletedTask;
    private long _persistenceVersion;

    public ActiveContextState()
    {
    }

    public ActiveContextState(ISettingsStore settingsStore)
        => _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasActiveContext))]
    [NotifyPropertyChangedFor(nameof(AddActionLabel))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private Guid? _activePackageId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AddActionLabel))]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private string? _activePackageName;

    public bool HasActiveContext => ActivePackageId.HasValue;

    public string AddActionLabel => HasActiveContext
        ? $"Add to Context · {ActivePackageName}"
        : "Choose a Context…";

    public string StatusLabel => HasActiveContext
        ? $"Active Context: {ActivePackageName}"
        : "No active Context";

    public void SetActive(ContextPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);
        ActivePackageName = package.Name;
        ActivePackageId = package.Id;
        QueuePersistence(package.Id);
    }

    public void Clear()
    {
        ActivePackageId = null;
        ActivePackageName = null;
        QueuePersistence(null);
    }

    /// <summary>Restores the last explicitly selected destination after storage initialization.</summary>
    public async Task InitializeAsync(
        ContextService contextService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextService);
        if (_settingsStore is null || HasActiveContext)
        {
            return;
        }

        try
        {
            string? value = await _settingsStore
                .GetAsync(ActivePackageSettingKey, cancellationToken)
                .ConfigureAwait(false);
            if (!Guid.TryParse(value, out Guid packageId))
            {
                return;
            }

            ContextPackage? package = await contextService
                .GetPackageAsync(packageId, cancellationToken)
                .ConfigureAwait(false);
            if (package is null)
            {
                await _settingsStore.RemoveAsync(ActivePackageSettingKey, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            // Startup owns the state at this point; avoid writing the value back.
            ActivePackageName = package.Name;
            ActivePackageId = package.Id;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Restoring a convenience destination is best-effort. A transient
            // settings/repository failure must not prevent Octadock from starting.
            ActivePackageId = null;
            ActivePackageName = null;
        }
    }

    public async Task<ContextPackage?> ResolveAsync(
        ContextService contextService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contextService);
        while (ActivePackageId is Guid packageId)
        {
            ContextPackage? package = await contextService
                .GetPackageAsync(packageId, cancellationToken)
                .ConfigureAwait(false);

            // The user may have selected another Context while persistence was
            // loading. Resolve that newer destination instead of letting A's
            // late completion clear or restore stale state over B.
            if (ActivePackageId != packageId)
            {
                continue;
            }

            if (package is null)
            {
                Clear();
                return null;
            }

            if (!string.Equals(ActivePackageName, package.Name, StringComparison.Ordinal))
            {
                SetActive(package);
            }

            return package;
        }

        return null;
    }

    private void QueuePersistence(Guid? packageId)
    {
        if (_settingsStore is null)
        {
            return;
        }

        long version = Interlocked.Increment(ref _persistenceVersion);
        lock (_persistenceGate)
        {
            _persistenceTail = PersistLatestAsync(_persistenceTail, version, packageId);
        }
    }

    private async Task PersistLatestAsync(Task predecessor, long version, Guid? packageId)
    {
        try
        {
            await predecessor.ConfigureAwait(false);
            if (version != Volatile.Read(ref _persistenceVersion))
            {
                return;
            }

            if (packageId is Guid id)
            {
                await _settingsStore!.SetAsync(ActivePackageSettingKey, id.ToString("D"))
                    .ConfigureAwait(false);
            }
            else
            {
                await _settingsStore!.RemoveAsync(ActivePackageSettingKey).ConfigureAwait(false);
            }
        }
        catch
        {
            // Active selection remains usable in memory when settings persistence is unavailable.
        }
    }
}
