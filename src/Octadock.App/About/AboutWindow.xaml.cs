using System.Reflection;
using System.Runtime.Versioning;
using System.Windows;

namespace Octadock.App.About;

/// <summary>
/// The About window: product name, version and the "original work, not affiliated
/// with any other product" note.
/// </summary>
[SupportedOSPlatform("windows")]
public partial class AboutWindow : Window
{
    /// <summary>Creates the About window.</summary>
    public AboutWindow()
    {
        InitializeComponent();

        Assembly assembly = typeof(AboutWindow).Assembly;
        string version = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        // Strip any build metadata suffix (e.g. "+abc123").
        int plus = version.IndexOf('+', StringComparison.Ordinal);
        if (plus > 0)
        {
            version = version[..plus];
        }

        VersionText.Text = $"Version {version}";

        int year = DateTime.Now.Year;
        CopyrightText.Text = $"Copyright © {year} Surus Labs";
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
