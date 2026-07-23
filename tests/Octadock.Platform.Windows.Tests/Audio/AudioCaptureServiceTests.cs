using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Abstractions;
using Octadock.Platform.Windows.Audio;
using Xunit;

namespace Octadock.Platform.Windows.Tests.Audio;

/// <summary>
/// Device-loss handling that is deterministic without hardware: the abnormal
/// stop transition is driven through the internal handler the NAudio event
/// calls, so device-unplug behavior is testable without unplugging a real
/// microphone (real-device rows stay in the manual C-04 matrix).
/// </summary>
public sealed class AudioCaptureServiceTests
{
    [Fact]
    public void Stop_without_start_returns_an_empty_buffer()
    {
        using var service = new AudioCaptureService(NullLogger<AudioCaptureService>.Instance);

        AudioBuffer buffer = service.Stop();

        buffer.Samples.Should().BeEmpty();
        buffer.SampleRate.Should().Be(16_000);
        service.WasInterrupted.Should().BeFalse();
    }

    [Fact]
    public void Abnormal_capture_stop_marks_interruption_and_raises_the_event()
    {
        using var service = new AudioCaptureService(NullLogger<AudioCaptureService>.Instance);
        AudioCaptureInterruptedEventArgs? raised = null;
        service.CaptureInterrupted += (_, e) => raised = e;
        var deviceLoss = new InvalidOperationException("AUDCLNT_E_DEVICE_INVALIDATED");

        service.HandleCaptureStopped(deviceLoss);

        service.WasInterrupted.Should().BeTrue("the partial utterance must be honestly marked as truncated");
        raised.Should().NotBeNull();
        raised!.Error.Should().BeSameAs(deviceLoss);
    }

    [Fact]
    public void Normal_capture_stop_does_not_mark_interruption()
    {
        using var service = new AudioCaptureService(NullLogger<AudioCaptureService>.Instance);
        bool raised = false;
        service.CaptureInterrupted += (_, _) => raised = true;

        service.HandleCaptureStopped(null);

        service.WasInterrupted.Should().BeFalse();
        raised.Should().BeFalse();
    }

    [Fact]
    public void Dispose_is_idempotent_without_a_capture()
    {
        var service = new AudioCaptureService(NullLogger<AudioCaptureService>.Instance);

        service.Invoking(s => s.Dispose()).Should().NotThrow();
        service.Invoking(s => s.Dispose()).Should().NotThrow("double dispose must be harmless");
    }
}
