using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;

namespace Octadock.App.Theming;

/// <summary>
/// Resolves the effective light/dark theme from the user's
/// <see cref="ThemePreference"/> (honoring the Windows "apps use light theme"
/// registry value for <see cref="ThemePreference.System"/>) and swaps the merged
/// theme dictionary on <see cref="Application.Resources"/> so every open window
/// re-themes live. Also listens to <see cref="ISettingsService.Changed"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ThemeManager : IDisposable
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    private static readonly Uri SharedUri = Pack("Shared.xaml");
    private static readonly Uri LightUri = Pack("Light.xaml");
    private static readonly Uri DarkUri = Pack("Dark.xaml");

    private readonly ISettingsService _settings;
    private readonly ILogger<ThemeManager> _logger;

    private ResourceDictionary? _paletteDictionary;
    private bool _isDark;
    private bool _disposed;

    /// <summary>Creates the theme manager.</summary>
    public ThemeManager(ISettingsService settings, ILogger<ThemeManager> logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings.Changed += OnSettingsChanged;
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>True when the currently applied palette is the dark one.</summary>
    public bool IsDark => _isDark;

    /// <summary>
    /// Installs the shared + palette dictionaries into the application resources
    /// for the first time. Call once during startup after <see cref="Application"/>
    /// exists.
    /// </summary>
    public void Initialize()
    {
        Application app = Application.Current
            ?? throw new InvalidOperationException("Application.Current is null; create the App first.");

        // Shared (theme-independent) dictionary is added once.
        var shared = new ResourceDictionary { Source = SharedUri };
        app.Resources.MergedDictionaries.Add(shared);

        Apply(_settings.Current.General.Theme);
    }

    /// <summary>Applies the palette for the given preference, swapping the merged dictionary.</summary>
    public void Apply(ThemePreference preference)
    {
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        bool dark = ResolveDark(preference);
        var next = new ResourceDictionary { Source = dark ? DarkUri : LightUri };

        if (_paletteDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_paletteDictionary);
        }

        app.Resources.MergedDictionaries.Add(next);
        _paletteDictionary = next;
        _isDark = dark;

        _logger.LogDebug("Applied {Theme} theme (preference {Preference}).", dark ? "dark" : "light", preference);
    }

    /// <summary>Re-evaluates the current preference (e.g. after a system theme change).</summary>
    public void Refresh() => Apply(_settings.Current.General.Theme);

    private bool ResolveDark(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => false,
        ThemePreference.Dark => true,
        _ => !ReadAppsUseLightTheme(),
    };

    private bool ReadAppsUseLightTheme()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            if (key?.GetValue(AppsUseLightThemeValue) is int value)
            {
                return value != 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read {Value}; assuming light theme.", AppsUseLightThemeValue);
        }

        // Default to light when the value is missing/unreadable.
        return true;
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // Marshal to the UI thread; settings can be saved from any thread.
        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        void DoApply() => Apply(e.Settings.General.Theme);

        if (app.Dispatcher.CheckAccess())
        {
            DoApply();
        }
        else
        {
            app.Dispatcher.Invoke(DoApply);
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is not UserPreferenceCategory.General
            and not UserPreferenceCategory.Color
            and not UserPreferenceCategory.Window)
        {
            return;
        }

        Application? app = Application.Current;
        if (app is null)
        {
            return;
        }

        void DoRefresh() => Refresh();

        if (app.Dispatcher.CheckAccess())
        {
            DoRefresh();
        }
        else
        {
            _ = app.Dispatcher.BeginInvoke(DoRefresh, DispatcherPriority.Normal);
        }
    }

    private static Uri Pack(string file)
        => new($"pack://application:,,,/Octadock;component/Resources/Themes/{file}", UriKind.Absolute);

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= OnSettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
