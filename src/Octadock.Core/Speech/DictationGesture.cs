using Octadock.Core.Settings;

namespace Octadock.Core.Speech;

/// <summary>What the dictation gesture handler should do in response to a key event.</summary>
public enum DictationGestureAction
{
    None,
    Start,
    StopAndInsert,
    Discard,
}

/// <summary>
/// Pure tap-versus-hold policy for the push-to-talk gesture, kept free of any
/// Win32 so every mode/timing combination is unit-testable. A press shorter
/// than <see cref="TapThreshold"/> is a tap; anything longer is a hold.
/// <list type="bullet">
///   <item><c>hold</c>: key down starts, release inserts; a tap is treated as
///   an accidental press and discards.</item>
///   <item><c>both</c>: hold behaves like <c>hold</c>; a tap toggles — first
///   tap starts and listening continues, the next tap stops and inserts.</item>
/// </list>
/// </summary>
public static class DictationGestureInterpreter
{
    /// <summary>Presses shorter than this are taps; longer are holds.</summary>
    public static readonly TimeSpan TapThreshold = TimeSpan.FromMilliseconds(250);

    /// <summary>Decides what a key-down does (only ever starts when idle).</summary>
    public static DictationGestureAction OnPress(bool isListening)
        => isListening ? DictationGestureAction.None : DictationGestureAction.Start;

    /// <summary>Decides what the matching key-up does.</summary>
    /// <param name="activationMode">"hold" or "both" (the monitor never runs in "toggle").</param>
    /// <param name="startedByThisPress">Whether the paired key-down started the capture.</param>
    /// <param name="heldFor">How long the key was physically down.</param>
    /// <param name="isListening">Whether dictation is (still) listening at release time.</param>
    public static DictationGestureAction OnRelease(
        string activationMode, bool startedByThisPress, TimeSpan heldFor, bool isListening)
    {
        if (!isListening)
        {
            return DictationGestureAction.None;
        }

        if (heldFor >= TapThreshold)
        {
            return DictationGestureAction.StopAndInsert;
        }

        if (!startedByThisPress)
        {
            // A tap while already listening is the "stop" side of tap-to-toggle.
            return DictationGestureAction.StopAndInsert;
        }

        return string.Equals(activationMode, SpeechSettings.ActivationModeBoth, StringComparison.OrdinalIgnoreCase)
            ? DictationGestureAction.None // Tap-to-toggle: keep listening until the next action.
            : DictationGestureAction.Discard; // Hold-only: a tap was accidental.
    }
}
