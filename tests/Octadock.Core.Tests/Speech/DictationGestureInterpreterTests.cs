using FluentAssertions;
using Octadock.Core.Speech;
using Xunit;

namespace Octadock.Core.Tests.Speech;

public sealed class DictationGestureInterpreterTests
{
    private static readonly TimeSpan Hold = TimeSpan.FromMilliseconds(600);
    private static readonly TimeSpan Tap = TimeSpan.FromMilliseconds(120);

    [Fact]
    public void Press_starts_only_when_idle()
    {
        DictationGestureInterpreter.OnPress(isListening: false)
            .Should().Be(DictationGestureAction.Start);
        DictationGestureInterpreter.OnPress(isListening: true)
            .Should().Be(DictationGestureAction.None);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("both")]
    public void Hold_release_stops_and_inserts(string mode)
    {
        DictationGestureInterpreter.OnRelease(mode, startedByThisPress: true, Hold, isListening: true)
            .Should().Be(DictationGestureAction.StopAndInsert);
    }

    [Fact]
    public void Tap_in_hold_mode_discards_the_accidental_press()
    {
        DictationGestureInterpreter.OnRelease("hold", startedByThisPress: true, Tap, isListening: true)
            .Should().Be(DictationGestureAction.Discard);
    }

    [Fact]
    public void Tap_in_both_mode_toggle_starts_and_keeps_listening()
    {
        DictationGestureInterpreter.OnRelease("both", startedByThisPress: true, Tap, isListening: true)
            .Should().Be(DictationGestureAction.None);
    }

    [Theory]
    [InlineData("hold")]
    [InlineData("both")]
    public void Tap_while_already_listening_stops_and_inserts(string mode)
    {
        // The press did not start capture (a previous tap or the dock did), so
        // this tap is the "stop" half of the cycle.
        DictationGestureInterpreter.OnRelease(mode, startedByThisPress: false, Tap, isListening: true)
            .Should().Be(DictationGestureAction.StopAndInsert);
    }

    [Fact]
    public void Release_when_not_listening_does_nothing()
    {
        DictationGestureInterpreter.OnRelease("hold", startedByThisPress: true, Hold, isListening: false)
            .Should().Be(DictationGestureAction.None);
    }

    [Fact]
    public void Threshold_boundary_counts_as_hold()
    {
        DictationGestureInterpreter.OnRelease(
                "hold", startedByThisPress: true, DictationGestureInterpreter.TapThreshold, isListening: true)
            .Should().Be(DictationGestureAction.StopAndInsert);
    }
}
