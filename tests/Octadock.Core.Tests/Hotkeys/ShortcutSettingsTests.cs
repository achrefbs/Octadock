using FluentAssertions;
using Octadock.Core.Hotkeys;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.Core.Tests.Hotkeys;

public sealed class ShortcutSettingsTests
{
    [Fact]
    public void Defaults_include_toggle_dictation_hotkey()
    {
        ShortcutSettings shortcuts = OctadockSettings.Defaults.Shortcuts;

        shortcuts.Dictation.ToString().Should().Be("Ctrl+Shift+2");
    }

    [Fact]
    public void Enumerate_includes_every_configured_hotkey_action()
    {
        IReadOnlyList<HotkeyAction> actions = OctadockSettings.Defaults.Shortcuts
            .Enumerate()
            .Select(item => item.Action)
            .ToArray();

        actions.Should().Equal(
            HotkeyAction.CaptureArea,
            HotkeyAction.CaptureWindow,
            HotkeyAction.CaptureFullscreen,
            HotkeyAction.CapturePreviousArea,
            HotkeyAction.Dictation,
            HotkeyAction.Ocr,
            HotkeyAction.Record,
            HotkeyAction.ClipboardHistory,
            HotkeyAction.ReadAloud);
    }

    [Fact]
    public void Fresh_profiles_default_only_area_fullscreen_and_dictation()
    {
        ShortcutSettings shortcuts = OctadockSettings.Defaults.Shortcuts;

        shortcuts.CaptureArea.ToString().Should().Be("Ctrl+Shift+4");
        shortcuts.CaptureFullscreen.ToString().Should().Be("Ctrl+Shift+3");
        shortcuts.Dictation.ToString().Should().Be("Ctrl+Shift+2");
    }

    [Theory]
    [InlineData(nameof(ShortcutSettings.CaptureWindow))]
    [InlineData(nameof(ShortcutSettings.CapturePreviousArea))]
    [InlineData(nameof(ShortcutSettings.Ocr))]
    [InlineData(nameof(ShortcutSettings.Record))]
    [InlineData(nameof(ShortcutSettings.ClipboardHistory))]
    [InlineData(nameof(ShortcutSettings.ReadAloud))]
    public void Every_other_shortcut_ships_unassigned_but_editable(string property)
    {
        ShortcutSettings shortcuts = OctadockSettings.Defaults.Shortcuts;

        HotkeyGesture gesture = (HotkeyGesture)typeof(ShortcutSettings).GetProperty(property)!.GetValue(shortcuts)!;

        gesture.Should().Be(HotkeyGesture.None);
    }
}
