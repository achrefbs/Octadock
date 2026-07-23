using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Platform.Windows.System;
using Xunit;

namespace Octadock.Platform.Windows.Tests.System;

/// <summary>
/// Upgrade cleanup for the legacy per-user Explorer associations ("Open with
/// Octadock" preview entries and the "Add to Octadock dock" image verbs). The
/// features are gone, so startup unregisters every legacy location — no dead
/// Explorer commands may remain installed.
/// </summary>
public sealed class FileAssociationCleanupTests
{
    [Fact]
    public void Unregister_removes_the_prog_id_tree()
    {
        var registry = new FakeRegistryWrites();
        var cleanup = new FileAssociationRegistration(registry, NullLogger<FileAssociationRegistration>.Instance);

        cleanup.Unregister();

        registry.SubKeyTreeDeletions.Should().Contain((@"Software\Classes", "Octadock.Preview"));
    }

    [Fact]
    public void Unregister_clears_open_with_entries_for_every_legacy_preview_extension()
    {
        var registry = new FakeRegistryWrites();
        var cleanup = new FileAssociationRegistration(registry, NullLogger<FileAssociationRegistration>.Instance);

        cleanup.Unregister();

        registry.ValueDeletions.Should().Contain((@"Software\Classes\.csv\OpenWithProgids", "Octadock.Preview"));
        registry.ValueDeletions.Should().Contain((@"Software\Classes\.md\OpenWithProgids", "Octadock.Preview"));
        registry.ValueDeletions.Should().Contain((@"Software\Classes\.json\OpenWithProgids", "Octadock.Preview"));
        registry.ValueDeletions.Should().Contain((@"Software\Classes\.png\OpenWithProgids", "Octadock.Preview"));
        registry.ValueDeletions.Should().Contain((@"Software\Classes\.heic\OpenWithProgids", "Octadock.Preview"));
    }

    [Fact]
    public void Unregister_removes_the_add_to_dock_shell_verb_for_every_image_extension()
    {
        var registry = new FakeRegistryWrites();
        var cleanup = new FileAssociationRegistration(registry, NullLogger<FileAssociationRegistration>.Instance);

        cleanup.Unregister();

        registry.SubKeyTreeDeletions.Should().Contain(
            (@"Software\Classes\SystemFileAssociations\.png\shell", "Octadock.AddToDock"));
        registry.SubKeyTreeDeletions.Should().Contain(
            (@"Software\Classes\SystemFileAssociations\.jpg\shell", "Octadock.AddToDock"));
        registry.SubKeyTreeDeletions.Should().Contain(
            (@"Software\Classes\SystemFileAssociations\.webp\shell", "Octadock.AddToDock"));
        // Text extensions never had the dock verb; cleaning them would be wrong.
        registry.SubKeyTreeDeletions.Should().NotContain(d =>
            d.KeyPath.Contains(".csv", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Unregister_is_a_no_op_when_nothing_is_installed()
    {
        var registry = new FakeRegistryWrites();
        var cleanup = new FileAssociationRegistration(registry, NullLogger<FileAssociationRegistration>.Instance);

        Action act = () => cleanup.Unregister();

        act.Should().NotThrow("missing legacy entries must not break startup on fresh machines");
    }

    private sealed class FakeRegistryWrites : IRegistryWrites
    {
        public List<(string KeyPath, string ValueName)> ValueDeletions { get; } = [];

        public List<(string KeyPath, string SubKey)> SubKeyTreeDeletions { get; } = [];

        public void DeleteValue(string keyPath, string valueName) => ValueDeletions.Add((keyPath, valueName));

        public void DeleteSubKeyTree(string keyPath, string subKey) => SubKeyTreeDeletions.Add((keyPath, subKey));
    }
}
