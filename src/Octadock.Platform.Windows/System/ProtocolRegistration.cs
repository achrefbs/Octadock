using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Registers the <c>octadock://</c> URL protocol for the current user under
/// <c>HKCU\Software\Classes\octadock</c>. Per-user registration avoids the need
/// for elevation. The open command is <c>"exe" "%1"</c>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ProtocolRegistration : IProtocolRegistration
{
    private const string Scheme = "octadock";
    private const string ClassesRoot = @"Software\Classes\";
    private const string UrlProtocolValue = "URL Protocol";

    private readonly ILogger<ProtocolRegistration> _logger;

    /// <summary>Creates the protocol registration helper.</summary>
    public ProtocolRegistration(ILogger<ProtocolRegistration> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string ProtocolKeyPath => ClassesRoot + Scheme;

    private static string CommandKeyPath => $@"{ProtocolKeyPath}\shell\open\command";

    /// <inheritdoc />
    public bool IsRegistered()
    {
        try
        {
            using RegistryKey? protocolKey = Registry.CurrentUser.OpenSubKey(ProtocolKeyPath, writable: false);
            if (protocolKey?.GetValue(UrlProtocolValue) is null)
            {
                return false;
            }

            using RegistryKey? commandKey = Registry.CurrentUser.OpenSubKey(CommandKeyPath, writable: false);
            if (commandKey?.GetValue(null) is not string command || string.IsNullOrWhiteSpace(command))
            {
                return false;
            }

            return command.Contains(ExecutablePath(), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the protocol registration.");
            return false;
        }
    }

    /// <inheritdoc />
    public void Register()
    {
        try
        {
            using (RegistryKey protocolKey = Registry.CurrentUser.CreateSubKey(ProtocolKeyPath, writable: true))
            {
                protocolKey.SetValue(null, "URL:Octadock Protocol", RegistryValueKind.String);
                protocolKey.SetValue(UrlProtocolValue, string.Empty, RegistryValueKind.String);

                using RegistryKey iconKey = protocolKey.CreateSubKey("DefaultIcon", writable: true);
                iconKey.SetValue(null, $"\"{ExecutablePath()}\",0", RegistryValueKind.String);
            }

            using RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true);
            commandKey.SetValue(null, $"\"{ExecutablePath()}\" \"%1\"", RegistryValueKind.String);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register the octadock:// protocol.");
            throw;
        }
    }

    /// <inheritdoc />
    public void Unregister()
    {
        try
        {
            using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true);
            classes?.DeleteSubKeyTree(Scheme, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister the octadock:// protocol.");
            throw;
        }
    }

    private static string ExecutablePath()
        => Environment.ProcessPath
           ?? Environment.GetCommandLineArgs().FirstOrDefault()
           ?? string.Empty;
}
