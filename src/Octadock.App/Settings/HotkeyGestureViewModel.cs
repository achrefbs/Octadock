using CommunityToolkit.Mvvm.ComponentModel;
using Octadock.Core.Hotkeys;

namespace Octadock.App.Settings;

/// <summary>
/// Editable view of a single shortcut row on the Shortcuts tab: the action label,
/// the currently captured <see cref="HotkeyGesture"/> and whether validation or
/// saved registration reported a conflict.
/// </summary>
public sealed partial class HotkeyGestureViewModel : ObservableObject
{
    [ObservableProperty]
    private HotkeyGesture _gesture;

    [ObservableProperty]
    private bool _isConflict;

    /// <summary>Creates a shortcut row.</summary>
    public HotkeyGestureViewModel(HotkeyAction action, string label, HotkeyGesture gesture)
    {
        Action = action;
        Label = label;
        _gesture = gesture;
    }

    /// <summary>The logical action this gesture triggers.</summary>
    public HotkeyAction Action { get; }

    /// <summary>Human-readable action name.</summary>
    public string Label { get; }

    /// <summary>Display string for the current gesture (empty when unset).</summary>
    public string DisplayText => Gesture.IsEmpty ? "Not set" : Gesture.ToString();

    partial void OnGestureChanged(HotkeyGesture value) => OnPropertyChanged(nameof(DisplayText));
}
