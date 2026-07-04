using Octadock.Core.Hotkeys;
using Octadock.Core.Settings;

namespace Octadock.Core.Abstractions;

/// <summary>Event args carrying the action a pressed hotkey maps to.</summary>
public sealed class HotkeyPressedEventArgs(HotkeyAction action) : EventArgs
{
    public HotkeyAction Action { get; } = action;
}

/// <summary>
/// Registers global hotkeys via Win32 <c>RegisterHotKey</c> and raises
/// <see cref="HotkeyPressed"/> when one fires. Registration reports per-chord
/// conflicts rather than throwing so the UI can surface them.
/// </summary>
public interface IHotkeyService
{
    /// <summary>Registers all shortcuts from settings, returning per-action results.</summary>
    IReadOnlyList<HotkeyRegistration> RegisterAll(ShortcutSettings shortcuts);

    /// <summary>Registers or replaces a single action's hotkey.</summary>
    HotkeyRegistration Register(HotkeyAction action, HotkeyGesture gesture);

    /// <summary>Removes the registration for an action.</summary>
    void Unregister(HotkeyAction action);

    /// <summary>Removes all registrations.</summary>
    void UnregisterAll();

    /// <summary>Raised on the UI thread when a registered hotkey is pressed.</summary>
    event EventHandler<HotkeyPressedEventArgs>? HotkeyPressed;
}
