using System.Collections.Frozen;

namespace Octadock.Core.Hotkeys;

/// <summary>
/// A global hotkey chord: zero or more modifiers plus a single key identified by
/// its Windows virtual-key code. Provides display formatting ("Ctrl+Shift+4")
/// and parsing so shortcuts round-trip through settings and the UI. Virtual-key
/// codes are plain integer constants and introduce no platform dependency.
/// </summary>
public readonly record struct HotkeyGesture(ModifierKeys Modifiers, uint VirtualKey)
{
    /// <summary>An unset gesture.</summary>
    public static readonly HotkeyGesture None = new(ModifierKeys.None, 0);

    public bool IsEmpty => VirtualKey == 0;

    /// <summary>Formats the chord as "Ctrl+Alt+Shift+Win+Key".</summary>
    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(ModifierKeys.Win))
        {
            parts.Add("Win");
        }

        parts.Add(KeyName(VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>
    /// Parses a chord such as "Ctrl+Shift+4" or "Alt+PrintScreen". Modifier order
    /// is irrelevant and matching is case-insensitive.
    /// </summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = None;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        ModifierKeys mods = ModifierKeys.None;
        uint vk = 0;
        bool keySeen = false;
        foreach (string raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control":
                    mods |= ModifierKeys.Control;
                    break;
                case "alt":
                    mods |= ModifierKeys.Alt;
                    break;
                case "shift":
                    mods |= ModifierKeys.Shift;
                    break;
                case "win" or "cmd" or "meta" or "super":
                    mods |= ModifierKeys.Win;
                    break;
                default:
                    if (keySeen)
                    {
                        return false;
                    }

                    if (!TryResolveKey(raw, out vk))
                    {
                        return false;
                    }

                    keySeen = true;
                    break;
            }
        }

        if (vk == 0)
        {
            return false;
        }

        gesture = new HotkeyGesture(mods, vk);
        return true;
    }

    private static string KeyName(uint vk)
        => VkToName.TryGetValue(vk, out string? name) ? name : $"0x{vk:X2}";

    private static bool TryResolveKey(string token, out uint vk)
    {
        if (NameToVk.TryGetValue(token, out vk))
        {
            return true;
        }

        // Single letter A-Z or digit 0-9 map to their ASCII value (== VK code).
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        // Hex virtual-key tokens ("0xBA") so any key the recorder accepts —
        // OEM punctuation, numpad, media keys — round-trips through KeyName's
        // "0x{vk:X2}" fallback instead of silently reverting to the default chord.
        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            uint.TryParse(token.AsSpan(2), System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out uint hex) &&
            hex is > 0 and <= 0xFF)
        {
            vk = hex;
            return true;
        }

        vk = 0;
        return false;
    }

    // A pragmatic subset of the Win32 virtual-key set covering the keys used in
    // capture shortcuts. Letters/digits are handled arithmetically above.
    private static readonly FrozenDictionary<uint, string> VkToName = BuildVkToName();
    private static readonly FrozenDictionary<string, uint> NameToVk = BuildNameToVk();

    private static FrozenDictionary<uint, string> BuildVkToName()
    {
        var map = new Dictionary<uint, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x1B] = "Esc",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
        };

        for (uint c = 'A'; c <= 'Z'; c++)
        {
            map[c] = ((char)c).ToString();
        }

        for (uint d = '0'; d <= '9'; d++)
        {
            map[d] = ((char)d).ToString();
        }

        for (uint f = 1; f <= 24; f++)
        {
            map[0x70 + (f - 1)] = $"F{f}";
        }

        return map.ToFrozenDictionary();
    }

    private static FrozenDictionary<string, uint> BuildNameToVk()
    {
        var map = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach ((uint vk, string name) in BuildVkToName())
        {
            map[name] = vk;
        }

        // Common aliases.
        map["Escape"] = 0x1B;
        map["Return"] = 0x0D;
        map["PrtSc"] = 0x2C;
        map["PrintScrn"] = 0x2C;
        map["Del"] = 0x2E;
        map["Ins"] = 0x2D;
        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
