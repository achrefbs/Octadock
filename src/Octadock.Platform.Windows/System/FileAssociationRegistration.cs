using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Minimal registry write seam used by <see cref="FileAssociationRegistration"/>
/// so the upgrade cleanup can be verified without touching a real hive.
/// </summary>
internal interface IRegistryWrites
{
    /// <summary>Deletes a value from a key when both exist; a no-op otherwise.</summary>
    void DeleteValue(string keyPath, string valueName);

    /// <summary>Deletes a subkey tree when it exists; a no-op otherwise.</summary>
    void DeleteSubKeyTree(string keyPath, string subKey);
}

/// <summary>HKCU-backed <see cref="IRegistryWrites"/>.</summary>
[SupportedOSPlatform("windows")]
internal sealed class CurrentUserRegistryWrites : IRegistryWrites
{
    public void DeleteValue(string keyPath, string valueName)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }

    public void DeleteSubKeyTree(string keyPath, string subKey)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
        key?.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
    }
}

/// <summary>
/// Upgrade cleanup for the legacy per-user Explorer associations. Versions that
/// shipped the generic file preview and the "Add to Octadock dock" image verb
/// registered an <c>Octadock.Preview</c> ProgID, "Open with" entries, and image
/// shell verbs under <c>HKCU\Software\Classes</c>. Those features were removed,
/// so app startup calls <see cref="Unregister"/> to actively uninstall every
/// legacy entry — no dead Explorer commands may remain installed. There is no
/// registration path left: the product no longer opens arbitrary files.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class FileAssociationRegistration
{
    private const string ProgId = "Octadock.Preview";
    private const string ClassesRoot = @"Software\Classes\";

    private const string AddToDockVerb = "Octadock.AddToDock";

    /// <summary>The non-image extensions legacy versions advertised for preview.</summary>
    private static readonly string[] PreviewExtensions =
    [
        ".csv", ".tsv", ".txt", ".log", ".md", ".json", ".xml", ".yaml", ".yml",
        ".cs", ".js", ".ts", ".py",
    ];

    /// <summary>The image extensions legacy versions could add to the dock.</summary>
    private static readonly string[] ImageExtensions =
    [
        ".avif",
        ".png", ".jpg", ".jpeg", ".jpe", ".jfif",
        ".gif", ".bmp", ".dib", ".webp", ".ico",
        ".tif", ".tiff", ".heic", ".heif", ".wdp", ".jxr",
    ];

    private readonly IRegistryWrites _registry;
    private readonly ILogger<FileAssociationRegistration> _logger;

    /// <summary>Creates the file-association cleanup helper.</summary>
    public FileAssociationRegistration(ILogger<FileAssociationRegistration> logger)
        : this(new CurrentUserRegistryWrites(), logger)
    {
    }

    internal FileAssociationRegistration(IRegistryWrites registry, ILogger<FileAssociationRegistration> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Removes every legacy per-user association: the ProgID, each extension's
    /// "Open with" entry, and each image type's "Add to Octadock dock" shell verb.
    /// Missing entries are skipped; partial leftovers are still cleaned.
    /// </summary>
    public void Unregister()
    {
        try
        {
            foreach (string extension in AllExtensions())
            {
                _registry.DeleteValue($@"{ClassesRoot}{extension}\OpenWithProgids", ProgId);
            }

            foreach (string extension in ImageExtensions)
            {
                _registry.DeleteSubKeyTree(
                    $@"{ClassesRoot}SystemFileAssociations\{extension}\shell",
                    AddToDockVerb);
            }

            _registry.DeleteSubKeyTree(ClassesRoot.TrimEnd('\\'), ProgId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister the legacy Octadock.Preview file association.");
            throw;
        }
    }

    private static IEnumerable<string> AllExtensions()
    {
        foreach (string extension in PreviewExtensions)
        {
            yield return extension;
        }

        foreach (string extension in ImageExtensions)
        {
            yield return extension;
        }
    }
}
