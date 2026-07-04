namespace Octadock.Core.Hotkeys;

/// <summary>
/// Modifier keys for a global hotkey. Values match the Win32 <c>MOD_*</c> flags
/// used by <c>RegisterHotKey</c> so the platform layer can pass them through
/// directly, but the enum itself carries no Win32 dependency.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Win = 0x0008,
}
