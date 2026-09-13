using FluentAssertions;
using Octadock.Core.Geometry;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Octadock.Core.Tests.Fakes;
using Xunit;

namespace Octadock.Core.Tests.Geometry;

public sealed class WindowPlacementTests
{
    [Fact]
    public void Far_edge_anchor_stays_inside_its_own_monitor()
    {
        var area = new PixelRect(-1920, 0, 1920, 1040);
        var display = new DisplayInfo(new MonitorId("LEFT"), 0, area, area, 1, false, "Left");
        var settings = new DockSettings { MonitorAnchors = new()
        {
            ["LEFT"] = WindowPlacement.SaveAnchor(new PixelPoint(0, 1040), area),
        }};
        var restored = WindowPlacement.RestoreAnchor(settings, display);
        restored.Should().Be(new PixelPoint(-1, 1039));
        area.Contains(restored!.Value).Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 0, 800, 560)]
    [InlineData(-1920, -240, 1920, 1040)]
    [InlineData(3840, -1440, 1080, 1840)]
    [InlineData(0, 0, 320, 200)]
    public void Oversized_or_offscreen_window_is_fitted_to_work_area(int x, int y, int width, int height)
    {
        var area = new PixelRect(x, y, width, height);
        var fitted = WindowPlacement.Fit(new PixelRect(40000, -30000, 2200, 1500), area, 12);
        fitted.Left.Should().BeGreaterThanOrEqualTo(area.Left + 12);
        fitted.Top.Should().BeGreaterThanOrEqualTo(area.Top + 12);
        fitted.Right.Should().BeLessThanOrEqualTo(area.Right - 12);
        fitted.Bottom.Should().BeLessThanOrEqualTo(area.Bottom - 12);
        WindowPlacement.Fit(fitted, area, 12).Should().Be(fitted);
    }

    [Fact]
    public async Task Each_monitor_remembers_its_anchor_after_reload_and_resolution_change()
    {
        var store = new InMemorySettingsStore();
        var service = new SettingsService(store);
        await service.LoadAsync();
        await service.UpdateAsync(s => s with { Dock = s.Dock with
        {
            MonitorAnchors = new()
            {
                ["LEFT"] = WindowPlacement.SaveAnchor(new(-1800, 400), new(-1920, 0, 1920, 1040)),
                ["RIGHT"] = WindowPlacement.SaveAnchor(new(1440, 780), new(0, 0, 1920, 1040)),
            },
        }});
        var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        var left = new DisplayInfo(new MonitorId("left"), 0, new(-1920, 0, 1920, 1080), new(-1920, 0, 1920, 1040), 1, false, "Left");
        var right = new DisplayInfo(new MonitorId("RIGHT"), 1, new(0, 0, 3840, 2160), new(0, 0, 3840, 2080), 2, true, "Right");
        WindowPlacement.RestoreAnchor(reloaded.Current.Dock, left).Should().Be(new PixelPoint(-1800, 400));
        WindowPlacement.RestoreAnchor(reloaded.Current.Dock, right).Should().Be(new PixelPoint(2880, 1560));
        WindowPlacement.RestoreAnchor(reloaded.Current.Dock, left).Should().Be(new PixelPoint(-1800, 400));
    }

    [Fact]
    public async Task Legacy_cloud_preferences_migrate_to_local_engines()
    {
        var store = new InMemorySettingsStore(new Dictionary<string, string>
        {
            [SettingKeys.SpeechProvider] = "openai",
            [SettingKeys.ReadTtsProvider] = "elevenlabs",
            [SettingKeys.DockMonitorAnchors] = "invalid json",
        });
        var service = new SettingsService(store);
        await service.LoadAsync();
        service.Current.Speech.Provider.Should().Be("parakeet");
        service.Current.Read.TtsProvider.Should().Be("windows");
        service.Current.Dock.MonitorAnchors.Should().BeEmpty();
    }
}
