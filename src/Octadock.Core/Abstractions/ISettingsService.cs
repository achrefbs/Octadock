using Octadock.Core.Settings;

namespace Octadock.Core.Abstractions;

/// <summary>Event args carrying the newly applied settings.</summary>
public sealed class SettingsChangedEventArgs(OctadockSettings settings) : EventArgs
{
    public OctadockSettings Settings { get; } = settings;
}

/// <summary>
/// The typed, cached view over persisted settings. Loads the key/value rows into
/// a <see cref="OctadockSettings"/> aggregate, persists edits, and raises
/// <see cref="Changed"/> so live components (hotkeys, shelf, theme) react.
/// </summary>
public interface ISettingsService
{
    /// <summary>The current settings snapshot (never null after <see cref="LoadAsync"/>).</summary>
    OctadockSettings Current { get; }

    /// <summary>Loads settings from the store, applying defaults for missing keys.</summary>
    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a complete settings object and raises <see cref="Changed"/>.</summary>
    Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Applies a functional update to the current settings and persists the result.</summary>
    Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default);

    /// <summary>Raised after settings are successfully saved.</summary>
    event EventHandler<SettingsChangedEventArgs>? Changed;
}
