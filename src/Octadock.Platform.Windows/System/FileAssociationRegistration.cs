using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Registers the <c>Octadock.Preview</c> ProgID for the current user under
/// <c>HKCU\Software\Classes</c> and adds Octadock to the "Open with" list of the
/// previewable file types. Per-user registration avoids the need for elevation.
/// The open command previews the file via the CLI verb the single-instance
/// forward understands: <c>"exe" open --filepath "%1"</c>.
/// </summary>
/// <remarks>
/// Modeled on <see cref="ProtocolRegistration"/>. This class is intentionally not
/// wired into settings/DI yet — it only exposes Register/Unregister/IsRegistered
/// so the association can be turned on later.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class FileAssociationRegistration
{
    private const string ProgId = "Octadock.Preview";
    private const string ClassesRoot = @"Software\Classes\";

    private const string AddToDockVerb = "Octadock.AddToDock";

    /// <summary>The non-image file extensions Octadock advertises itself as able to preview.</summary>
    private static readonly string[] PreviewExtensions =
    [
        ".csv", ".tsv", ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml",
        ".cs", ".js", ".ts", ".py",
    ];

    /// <summary>The image extensions Octadock can add directly to the dock.</summary>
    private static readonly string[] ImageExtensions =
    [
        ".avif",
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif",
        ".gif", ".bmp", ".dib", ".webp", ".ico",
        ".tif", ".tiff", ".heic", ".heif", ".wdp", ".jxr",
    ];

    /// <summary>The file extensions Octadock advertises itself as able to open.</summary>
    private static readonly string[] Extensions = [.. PreviewExtensions, .. ImageExtensions];

    private readonly ILogger<FileAssociationRegistration> _logger;

    /// <summary>Creates the file-association registration helper.</summary>
    public FileAssociationRegistration(ILogger<FileAssociationRegistration> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string ProgIdKeyPath => ClassesRoot + ProgId;

    private static string CommandKeyPath => $@"{ProgIdKeyPath}\shell\open\command";

    private static string AddToDockShellKeyPath(string extension)
        => $@"{ClassesRoot}SystemFileAssociations\{extension}\shell\{AddToDockVerb}";

    private static string AddToDockCommandKeyPath(string extension)
        => $@"{AddToDockShellKeyPath(extension)}\command";

    /// <inheritdoc cref="ProtocolRegistration.IsRegistered" />
    public bool IsRegistered()
    {
        try
        {
            string executablePath = ExecutablePath();
            if (string.IsNullOrWhiteSpace(executablePath) ||
                !RegisteredCommandContains(CommandKeyPath, executablePath))
            {
                return false;
            }

            foreach (string extension in ImageExtensions)
            {
                if (!RegisteredCommandContains(AddToDockCommandKeyPath(extension), executablePath))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read the file-association registration.");
            return false;
        }
    }

    /// <inheritdoc cref="ProtocolRegistration.Register" />
    public void Register()
    {
        try
        {
            string executablePath = ExecutablePath();
            using (RegistryKey progIdKey = Registry.CurrentUser.CreateSubKey(ProgIdKeyPath, writable: true))
            {
                progIdKey.SetValue(null, "Octadock Preview", RegistryValueKind.String);

                using RegistryKey iconKey = progIdKey.CreateSubKey("DefaultIcon", writable: true);
                iconKey.SetValue(null, $"\"{executablePath}\",0", RegistryValueKind.String);
            }

            using (RegistryKey commandKey = Registry.CurrentUser.CreateSubKey(CommandKeyPath, writable: true))
            {
                commandKey.SetValue(
                    null,
                    $"\"{executablePath}\" open --filepath \"%1\"",
                    RegistryValueKind.String);
            }

            // Advertise Octadock in each extension's "Open with" list without
            // seizing the default association (OpenWithProgids, not the default value).
            foreach (string extension in Extensions)
            {
                using RegistryKey openWith = Registry.CurrentUser.CreateSubKey(
                    $@"{ClassesRoot}{extension}\OpenWithProgids", writable: true);
                openWith.SetValue(ProgId, Array.Empty<byte>(), RegistryValueKind.None);
            }

            foreach (string extension in ImageExtensions)
            {
                using RegistryKey shellKey = Registry.CurrentUser.CreateSubKey(
                    AddToDockShellKeyPath(extension), writable: true);
                shellKey.SetValue("MUIVerb", "Add to Octadock dock", RegistryValueKind.String);
                shellKey.SetValue("Icon", $"\"{executablePath}\",0", RegistryValueKind.String);

                using RegistryKey commandKey = shellKey.CreateSubKey("command", writable: true);
                commandKey.SetValue(
                    null,
                    $"\"{executablePath}\" add-shelf-item --filepath \"%1\"",
                    RegistryValueKind.String);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register the Octadock.Preview file association.");
            throw;
        }
    }

    /// <inheritdoc cref="ProtocolRegistration.Unregister" />
    public void Unregister()
    {
        try
        {
            foreach (string extension in Extensions)
            {
                using RegistryKey? openWith = Registry.CurrentUser.OpenSubKey(
                    $@"{ClassesRoot}{extension}\OpenWithProgids", writable: true);
                openWith?.DeleteValue(ProgId, throwOnMissingValue: false);
            }

            foreach (string extension in ImageExtensions)
            {
                using RegistryKey? shell = Registry.CurrentUser.OpenSubKey(
                    $@"{ClassesRoot}SystemFileAssociations\{extension}\shell", writable: true);
                shell?.DeleteSubKeyTree(AddToDockVerb, throwOnMissingSubKey: false);
            }

            using RegistryKey? classes = Registry.CurrentUser.OpenSubKey(ClassesRoot, writable: true);
            classes?.DeleteSubKeyTree(ProgId, throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister the Octadock.Preview file association.");
            throw;
        }
    }

    private static string ExecutablePath()
        => Environment.ProcessPath
           ?? Environment.GetCommandLineArgs().FirstOrDefault()
           ?? string.Empty;

    private static bool RegisteredCommandContains(string commandKeyPath, string executablePath)
    {
        using RegistryKey? commandKey = Registry.CurrentUser.OpenSubKey(commandKeyPath, writable: false);
        return commandKey?.GetValue(null) is string command &&
               !string.IsNullOrWhiteSpace(command) &&
               command.Contains(executablePath, StringComparison.OrdinalIgnoreCase);
    }
}
