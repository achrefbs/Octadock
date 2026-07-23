using FluentAssertions;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Services;

/// <summary>
/// Persistence coverage for the Phase 1 capture settings: the default-on
/// precision aids and freeze screen, and the advanced size/ratio options.
/// </summary>
public class SettingsServiceCaptureTests
{
    [Fact]
    public void Fresh_defaults_enable_precision_aids_and_freeze_screen()
    {
        CaptureSettings capture = OctadockSettings.Defaults.Capture;

        capture.PrecisionAids.Should().BeTrue("precision aids default ON (dimensions + magnifier)");
        capture.FreezeScreen.Should().BeTrue("freeze screen stays a default-ON setting");
        capture.FixedSizeEnabled.Should().BeFalse("fixed size is an opt-in advanced constraint");
        capture.FixedWidth.Should().Be(0);
        capture.FixedHeight.Should().Be(0);
        capture.LockAspectRatio.Should().BeFalse();
    }

    [Fact]
    public async Task Load_without_the_new_keys_applies_defaults_and_preserves_stored_capture_values()
    {
        // An existing user's store: every key predates the precision-aids wave.
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CaptureSaveDirectory] = @"D:\shots",
            [SettingKeys.CaptureJpegQuality] = "72",
            [SettingKeys.CaptureFreezeScreen] = "false",
            [SettingKeys.CaptureIncludeCursor] = "true",
            [SettingKeys.CaptureFilenameTemplate] = "Shot {counter}",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        CaptureSettings capture = service.Current.Capture;
        capture.PrecisionAids.Should().BeTrue("the missing key falls back to the default-ON precision aids");
        capture.FixedSizeEnabled.Should().BeFalse();
        capture.LockAspectRatio.Should().BeFalse();
        capture.SaveDirectory.Should().Be(@"D:\shots", "stored values must survive the new keys");
        capture.JpegQuality.Should().Be(72);
        capture.FreezeScreen.Should().BeFalse("an explicit freeze-screen choice is preserved");
        capture.IncludeCursor.Should().BeTrue();
        capture.FilenameTemplate.Should().Be("Shot {counter}");
    }

    [Fact]
    public async Task Save_and_load_round_trips_precision_aids_and_size_ratio_settings()
    {
        var store = new InMemorySettingsStore();
        var writer = new SettingsService(store);
        OctadockSettings custom = OctadockSettings.Defaults with
        {
            Capture = OctadockSettings.Defaults.Capture with
            {
                PrecisionAids = false,
                FreezeScreen = false,
                FixedSizeEnabled = true,
                FixedWidth = 1280,
                FixedHeight = 720,
                LockAspectRatio = true,
            },
        };

        await writer.SaveAsync(custom);

        store.Snapshot[SettingKeys.CapturePrecisionAids].Should().Be("false");
        store.Snapshot[SettingKeys.CaptureFreezeScreen].Should().Be("false");
        store.Snapshot[SettingKeys.CaptureFixedSizeEnabled].Should().Be("true");
        store.Snapshot[SettingKeys.CaptureFixedWidth].Should().Be("1280");
        store.Snapshot[SettingKeys.CaptureFixedHeight].Should().Be("720");
        store.Snapshot[SettingKeys.CaptureLockAspectRatio].Should().Be("true");

        var reader = new SettingsService(store);
        await reader.LoadAsync();
        reader.Current.Capture.Should().Be(custom.Capture);
    }

    [Fact]
    public async Task Load_parses_new_bools_robustly_and_clamps_fixed_size()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CapturePrecisionAids] = "off",
            [SettingKeys.CaptureFixedSizeEnabled] = "YES",
            [SettingKeys.CaptureFixedWidth] = "99999",
            [SettingKeys.CaptureFixedHeight] = "-20",
            [SettingKeys.CaptureLockAspectRatio] = "1",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        CaptureSettings capture = service.Current.Capture;
        capture.PrecisionAids.Should().BeFalse();
        capture.FixedSizeEnabled.Should().BeTrue();
        capture.FixedWidth.Should().Be(32767, "fixed size is clamped to a sane pixel bound");
        capture.FixedHeight.Should().Be(0);
        capture.LockAspectRatio.Should().BeTrue();
    }

    [Fact]
    public async Task Load_falls_back_to_defaults_on_unparseable_new_values()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.CapturePrecisionAids] = "maybe",
            [SettingKeys.CaptureFixedWidth] = "wide",
        });
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Capture.PrecisionAids.Should().BeTrue();
        service.Current.Capture.FixedWidth.Should().Be(0);
    }

    [Fact]
    public async Task Save_and_load_round_trips_recording_audio_opt_ins()
    {
        // Regression: a "planned feature" normalizer used to force both flags
        // back to false on every load and save, silently stripping the opt-in.
        var store = new InMemorySettingsStore();
        var writer = new SettingsService(store);
        OctadockSettings custom = OctadockSettings.Defaults with
        {
            Recording = OctadockSettings.Defaults.Recording with
            {
                IncludeMicrophone = true,
                IncludeSystemAudio = true,
            },
        };

        await writer.SaveAsync(custom);

        store.Snapshot[SettingKeys.RecordingIncludeMicrophone].Should().Be("true");
        store.Snapshot[SettingKeys.RecordingIncludeSystemAudio].Should().Be("true");

        var reader = new SettingsService(store);
        await reader.LoadAsync();

        reader.Current.Recording.IncludeMicrophone.Should().BeTrue("mic audio is a real persisted opt-in, never stripped");
        reader.Current.Recording.IncludeSystemAudio.Should().BeTrue();
    }

    [Fact]
    public async Task Fresh_defaults_keep_recording_video_only()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);

        await service.LoadAsync();

        service.Current.Recording.IncludeMicrophone.Should().BeFalse("audio tracks stay explicit opt-ins");
        service.Current.Recording.IncludeSystemAudio.Should().BeFalse();
    }
}
