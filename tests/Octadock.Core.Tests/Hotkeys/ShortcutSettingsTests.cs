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
            HotkeyAction.AllInOne,
            HotkeyAction.Dictation,
            HotkeyAction.Ocr,
            HotkeyAction.Record,
            HotkeyAction.ClipboardHistory);
    }

    [Fact]
    public void Defaults_include_clipboard_history_hotkey()
    {
        ShortcutSettings shortcuts = OctadockSettings.Defaults.Shortcuts;

        shortcuts.ClipboardHistory.ToString().Should().Be("Ctrl+Shift+9");
    }
}
