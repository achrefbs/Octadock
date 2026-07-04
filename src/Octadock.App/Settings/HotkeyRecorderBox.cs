using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HotkeyGesture = Octadock.Core.Hotkeys.HotkeyGesture;
using WpfModifierKeys = System.Windows.Input.ModifierKeys;
using CoreModifierKeys = Octadock.Core.Hotkeys.ModifierKeys;

namespace Octadock.App.Settings;

/// <summary>
/// A focusable box that records the next key chord the user presses into a
/// <see cref="HotkeyGesture"/>. Modifier-only presses are ignored until a
/// non-modifier key completes the chord. Escape clears the gesture. The recorded
/// gesture is exposed via the <see cref="Gesture"/> dependency property (two-way
/// bindable).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HotkeyRecorderBox : Control
{
    /// <summary>The recorded gesture (two-way bindable).</summary>
    public static readonly DependencyProperty GestureProperty = DependencyProperty.Register(
        nameof(Gesture),
        typeof(HotkeyGesture),
        typeof(HotkeyRecorderBox),
        new FrameworkPropertyMetadata(HotkeyGesture.None, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnGestureChanged));

    /// <summary>Display text for the current gesture.</summary>
    public static readonly DependencyPropertyKey DisplayTextPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(DisplayText),
        typeof(string),
        typeof(HotkeyRecorderBox),
        new PropertyMetadata("Not set"));

    /// <summary>Read-only display-text property.</summary>
    public static readonly DependencyProperty DisplayTextProperty = DisplayTextPropertyKey.DependencyProperty;

    static HotkeyRecorderBox()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(HotkeyRecorderBox),
            new FrameworkPropertyMetadata(typeof(HotkeyRecorderBox)));
    }

    /// <summary>Creates the recorder box.</summary>
    public HotkeyRecorderBox()
    {
        Focusable = true;
        IsTabStop = true;
        Cursor = Cursors.Hand;
        UpdateDisplay(Gesture);
    }

    /// <summary>Raised when a new gesture is captured so hosts can update the pending edit.</summary>
    public event EventHandler<HotkeyGesture>? GestureCaptured;

    /// <summary>The recorded gesture.</summary>
    public HotkeyGesture Gesture
    {
        get => (HotkeyGesture)GetValue(GestureProperty);
        set => SetValue(GestureProperty, value);
    }

    /// <summary>Display text for the current gesture.</summary>
    public string DisplayText => (string)GetValue(DisplayTextProperty);

    /// <inheritdoc />
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        Key key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (key == Key.Escape)
        {
            SetCurrentValue(GestureProperty, HotkeyGesture.None);
            GestureCaptured?.Invoke(this, HotkeyGesture.None);
            e.Handled = true;
            return;
        }

        if (IsModifierKey(key))
        {
            // Wait for a non-modifier to complete the chord.
            e.Handled = true;
            return;
        }

        CaptureKey(key);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);

        // Windows delivers PrintScreen (Key.Snapshot) only as a key-up event, so
        // the most common screenshot key can only be recorded here.
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Snapshot)
        {
            CaptureKey(key);
            e.Handled = true;
        }
    }

    private void CaptureKey(Key key)
    {
        CoreModifierKeys mods = MapModifiers(Keyboard.Modifiers);
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (vk == 0)
        {
            return;
        }

        // Reject bare keys that would hijack normal typing system-wide. Function
        // keys, PrintScreen/Pause and media keys are legitimate standalone hotkeys;
        // everything else must carry at least one modifier.
        if (mods == CoreModifierKeys.None && !IsStandaloneAllowed(key))
        {
            return;
        }

        var gesture = new HotkeyGesture(mods, vk);
        SetCurrentValue(GestureProperty, gesture);
        GestureCaptured?.Invoke(this, gesture);
    }

    private static bool IsStandaloneAllowed(Key key) => key is
        (>= Key.F1 and <= Key.F24) or Key.Snapshot or Key.Pause or
        Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop or
        Key.VolumeMute or Key.VolumeUp or Key.VolumeDown;

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin or
        Key.System;

    private static CoreModifierKeys MapModifiers(WpfModifierKeys wpf)
    {
        CoreModifierKeys mods = CoreModifierKeys.None;
        if (wpf.HasFlag(WpfModifierKeys.Control))
        {
            mods |= CoreModifierKeys.Control;
        }

        if (wpf.HasFlag(WpfModifierKeys.Alt))
        {
            mods |= CoreModifierKeys.Alt;
        }

        if (wpf.HasFlag(WpfModifierKeys.Shift))
        {
            mods |= CoreModifierKeys.Shift;
        }

        if (wpf.HasFlag(WpfModifierKeys.Windows))
        {
            mods |= CoreModifierKeys.Win;
        }

        return mods;
    }

    private static void OnGestureChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HotkeyRecorderBox box)
        {
            box.UpdateDisplay((HotkeyGesture)e.NewValue);
        }
    }

    private void UpdateDisplay(HotkeyGesture gesture)
        => SetValue(DisplayTextPropertyKey, gesture.IsEmpty ? "Not set" : gesture.ToString());
}
