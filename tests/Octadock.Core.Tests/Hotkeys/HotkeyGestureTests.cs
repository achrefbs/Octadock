using FluentAssertions;
using Octadock.Core.Hotkeys;
using Xunit;

namespace Octadock.Core.Tests.Hotkeys;

public class HotkeyGestureTests
{
    [Fact]
    public void Parse_modifiers_and_digit()
    {
        HotkeyGesture.TryParse("Ctrl+Shift+4", out HotkeyGesture g).Should().BeTrue();
        g.Modifiers.Should().Be(ModifierKeys.Control | ModifierKeys.Shift);
        g.VirtualKey.Should().Be('4');
        g.ToString().Should().Be("Ctrl+Shift+4");
    }

    [Fact]
    public void Parse_is_modifier_order_insensitive()
    {
        HotkeyGesture.TryParse("Shift+Ctrl+A", out HotkeyGesture a).Should().BeTrue();
        HotkeyGesture.TryParse("Ctrl+Shift+A", out HotkeyGesture b).Should().BeTrue();
        a.Should().Be(b);

        // Canonical formatting is always Ctrl+Alt+Shift+Win order.
        a.ToString().Should().Be("Ctrl+Shift+A");
    }

    [Fact]
    public void Parse_is_case_insensitive()
    {
        HotkeyGesture.TryParse("ctrl+shift+f5", out HotkeyGesture g).Should().BeTrue();
        g.Modifiers.Should().Be(ModifierKeys.Control | ModifierKeys.Shift);
        g.VirtualKey.Should().Be(0x74); // F5
        g.ToString().Should().Be("Ctrl+Shift+F5");
    }

    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F24", 0x87)]
    public void Parse_function_keys(string text, uint expectedVk)
    {
        HotkeyGesture.TryParse(text, out HotkeyGesture g).Should().BeTrue();
        g.VirtualKey.Should().Be(expectedVk);
        g.ToString().Should().Be(text);
    }

    [Fact]
    public void Parse_printscreen_and_aliases()
    {
        HotkeyGesture.TryParse("Alt+PrintScreen", out HotkeyGesture g1).Should().BeTrue();
        g1.VirtualKey.Should().Be(0x2C);
        g1.Modifiers.Should().Be(ModifierKeys.Alt);
        g1.ToString().Should().Be("Alt+PrintScreen");

        HotkeyGesture.TryParse("PrtSc", out HotkeyGesture g2).Should().BeTrue();
        g2.VirtualKey.Should().Be(0x2C);
    }

    [Fact]
    public void Parse_win_modifier_aliases()
    {
        foreach (string alias in new[] { "Win", "Cmd", "Meta", "Super" })
        {
            HotkeyGesture.TryParse($"{alias}+A", out HotkeyGesture g).Should().BeTrue();
            g.Modifiers.Should().Be(ModifierKeys.Win);
        }
    }

    [Fact]
    public void All_four_modifiers_format_in_canonical_order()
    {
        HotkeyGesture.TryParse("Win+Shift+Alt+Ctrl+K", out HotkeyGesture g).Should().BeTrue();
        g.ToString().Should().Be("Ctrl+Alt+Shift+Win+K");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+Shift")] // modifiers only, no key
    [InlineData("Ctrl+")]
    [InlineData("NotAKey")]
    [InlineData(null)]
    public void Parse_returns_false_for_invalid(string? text)
    {
        HotkeyGesture.TryParse(text, out HotkeyGesture g).Should().BeFalse();
        g.Should().Be(HotkeyGesture.None);
    }

    [Theory]
    [InlineData("Ctrl+A+B")]
    [InlineData("A+B")]
    [InlineData("Ctrl+F1+F2")]
    public void Parse_rejects_multiple_non_modifier_keys(string text)
    {
        HotkeyGesture.TryParse(text, out HotkeyGesture g).Should().BeFalse();
        g.Should().Be(HotkeyGesture.None);
    }

    [Fact]
    public void Empty_gesture_formats_as_empty_string()
    {
        HotkeyGesture.None.ToString().Should().BeEmpty();
        HotkeyGesture.None.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void Letter_key_round_trips()
    {
        HotkeyGesture.TryParse("Ctrl+Q", out HotkeyGesture g).Should().BeTrue();
        g.VirtualKey.Should().Be('Q');
        HotkeyGesture.TryParse(g.ToString(), out HotkeyGesture g2).Should().BeTrue();
        g2.Should().Be(g);
    }

    [Theory]
    [InlineData("Ctrl+Shift+4")]
    [InlineData("Alt+PrintScreen")]
    [InlineData("Ctrl+Alt+Shift+Win+Home")]
    [InlineData("F9")]
    [InlineData("Ctrl+Delete")]
    public void ToString_round_trips_through_TryParse(string chord)
    {
        HotkeyGesture.TryParse(chord, out HotkeyGesture g).Should().BeTrue();
        HotkeyGesture.TryParse(g.ToString(), out HotkeyGesture again).Should().BeTrue();
        again.Should().Be(g);
    }

    [Theory]
    [InlineData(0xBA)] // VK_OEM_1 (';:') — accepted by the recorder, not in the name map
    [InlineData(0x60)] // VK_NUMPAD0
    [InlineData(0x6A)] // VK_MULTIPLY
    [InlineData(0xAD)] // VK_VOLUME_MUTE
    public void Unmapped_virtual_key_round_trips_via_hex_token(uint vk)
    {
        // Regression for D-1: KeyName emits "0xNN" for unmapped keys, so TryParse
        // must accept that hex form or the chord silently reverts to the default
        // after the settings round-trip through ToString().
        var gesture = new HotkeyGesture(ModifierKeys.Control | ModifierKeys.Shift, vk);
        string text = gesture.ToString();

        HotkeyGesture.TryParse(text, out HotkeyGesture again).Should().BeTrue();
        again.Should().Be(gesture);
    }

    [Theory]
    [InlineData("0x00")] // null VK is not a real key
    [InlineData("0x1FF")] // out of single-byte VK range
    public void Parse_rejects_out_of_range_hex_key(string text)
    {
        HotkeyGesture.TryParse($"Ctrl+{text}", out _).Should().BeFalse();
    }
}
