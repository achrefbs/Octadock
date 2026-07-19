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
    private const string EnableTransparencyValue = "EnableTransparency";

    private static readonly Uri SharedUri = Pack("Shared.xaml");
    private static readonly Uri LightUri = Pack("Light.xaml");
    private static readonly Uri DarkUri = Pack("Dark.xaml");
    private static readonly Uri HighContrastUri = Pack("HighContrast.xaml");

    private readonly ISettingsService _settings;
    private readonly ILogger<ThemeManager> _logger;

    private ResourceDictionary? _paletteDictionary;
    private ResourceDictionary? _accessibilityDictionary;
    private bool _isDark;
    private bool _disposed;

    /// <summary>Raised on the UI thread after a complete palette/accessibility refresh.</summary>
    public event EventHandler? ThemeApplied;

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

        // Every window gets themed native chrome (dark title bars, rounded
        // corners) as soon as it loads.
        WindowChromeStyler.RegisterAutoStyling();

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
        bool highContrast = SystemParameters.HighContrast;
        bool transparencyEnabled = !highContrast && ReadTransparencyEnabled();

        if (!transparencyEnabled)
        {
            ApplyOpaqueFloatingSurfaces(next);
        }

        if (_accessibilityDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_accessibilityDictionary);
            _accessibilityDictionary = null;
        }

        if (_paletteDictionary is not null)
        {
            app.Resources.MergedDictionaries.Remove(_paletteDictionary);
        }

        app.Resources.MergedDictionaries.Add(next);
        _paletteDictionary = next;
        _isDark = dark;

        if (highContrast)
        {
            _accessibilityDictionary = new ResourceDictionary { Source = HighContrastUri };
            app.Resources.MergedDictionaries.Add(_accessibilityDictionary);
        }

        app.Resources["Octadock.Glass.Enabled"] = transparencyEnabled;
        app.Resources["Octadock.Motion.Enabled"] = SystemParameters.ClientAreaAnimation;

        // Native chrome (title bars) follows the palette on every open window.
        WindowChromeStyler.ApplyToAllWindows(dark);

        _logger.LogDebug(
            "Applied {Theme} theme (preference {Preference}, high contrast {HighContrast}, transparency {Transparency}).",
            dark ? "dark" : "light",
            preference,
            highContrast,
            transparencyEnabled);
        ThemeApplied?.Invoke(this, EventArgs.Empty);
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

    private bool ReadTransparencyEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey, writable: false);
            return key?.GetValue(EnableTransparencyValue) is not int value || value != 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to read {Value}; assuming transparency is enabled.", EnableTransparencyValue);
            return true;
        }
    }

    private static void ApplyOpaqueFloatingSurfaces(ResourceDictionary palette)
    {
        object raised = palette["Octadock.Brush.SurfaceRaised"];
        object overlay = palette["Octadock.Brush.SurfaceOverlay"];
        palette["Octadock.Brush.GlassSurface"] = raised;
        palette["Octadock.Brush.GlassChrome"] = overlay;
        palette["Octadock.Brush.GlassRail"] = raised;
        palette["Octadock.Brush.GlassRow"] = raised;
        palette["Octadock.Brush.GlassRowHover"] = overlay;
        palette["Octadock.Brush.GlassRowDense"] = raised;
        palette["Octadock.Brush.GlassRowDenseHover"] = overlay;
        palette["Octadock.Brush.RailFade"] = raised;
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
            and not UserPreferenceCategory.Accessibility
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
