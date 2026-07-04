using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Manages the "launch at login" registration via the current-user Run key
/// (<c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>). The value name is
/// <c>Octadock</c> and the data is the quoted executable path.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StartupRegistration : IStartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Octadock";

    private readonly ILogger<StartupRegistration> _logger;

    /// <summary>Creates the startup registration helper.</summary>
    public StartupRegistration(ILogger<StartupRegistration> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string value || string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            // Consider it enabled only when it points at the current executable.
            string expected = QuotedExecutablePath();
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the startup registration.");
            return false;
        }
    }

    /// <inheritdoc />
    public void Enable()
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            key.SetValue(ValueName, QuotedExecutablePath(), RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable launch at login.");
            throw;
        }
    }

    /// <inheritdoc />
    public void Disable()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable launch at login.");
            throw;
        }
    }

    private static string QuotedExecutablePath()
    {
        string path = Environment.ProcessPath
            ?? Environment.GetCommandLineArgs().FirstOrDefault()
            ?? string.Empty;
        return $"\"{path}\"";
    }
}
