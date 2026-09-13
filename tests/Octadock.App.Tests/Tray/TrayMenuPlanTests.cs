using FluentAssertions;
using Octadock.App.Tray;
using Xunit;

namespace Octadock.App.Tests.Tray;

/// <summary>
/// The streamlined tray menu contract (Phase 1 shell simplification): one Capture
/// submenu with every capture verb, Dictate, a small library group and a small app
/// group. Pause/Resume and the all-in-one HUD must not reappear.
/// </summary>
public sealed class TrayMenuPlanTests
{
    [Fact]
    public void Capture_submenu_offers_every_capture_verb_in_order()
    {
        TrayIconController.CaptureSubmenuPlan.Should().Equal(
            TrayIconController.TrayMenuEntry.CaptureArea,
            TrayIconController.TrayMenuEntry.CaptureWindow,
            TrayIconController.TrayMenuEntry.CaptureFullScreen,
            TrayIconController.TrayMenuEntry.CaptureAllMonitors,
            TrayIconController.TrayMenuEntry.CapturePreviousArea,
            TrayIconController.TrayMenuEntry.CaptureTimer,
            TrayIconController.TrayMenuEntry.CaptureScrolling,
            TrayIconController.TrayMenuEntry.OcrRegion,
            TrayIconController.TrayMenuEntry.RecordToggle);
    }

    [Fact]
    public void Library_and_app_groups_stay_small_and_ordered()
    {
        TrayIconController.LibraryGroupPlan.Should().Equal(
            TrayIconController.TrayMenuEntry.Shelf,
            TrayIconController.TrayMenuEntry.History,
            TrayIconController.TrayMenuEntry.Clipboard,
            TrayIconController.TrayMenuEntry.Context);

        TrayIconController.AppGroupPlan.Should().Equal(
            TrayIconController.TrayMenuEntry.UseWithAi,
            TrayIconController.TrayMenuEntry.Settings,
            TrayIconController.TrayMenuEntry.About,
            TrayIconController.TrayMenuEntry.Exit);
    }

    [Fact]
    public void Scrolling_and_recording_keep_their_honest_beta_labels()
    {
        TrayIconController.LabelFor(TrayIconController.TrayMenuEntry.CaptureScrolling)
            .Should().Be("Scrolling — manual vertical (Beta)");
        TrayIconController.LabelFor(TrayIconController.TrayMenuEntry.RecordToggle)
            .Should().Be("Record (Beta)");
    }

    [Fact]
    public void No_entry_revives_pause_or_the_all_in_one_hud()
    {
        string[] labels = TrayIconController.CaptureSubmenuPlan
            .Concat(TrayIconController.LibraryGroupPlan)
            .Concat(TrayIconController.AppGroupPlan)
            .Append(TrayIconController.TrayMenuEntry.Dictate)
            .Select(TrayIconController.LabelFor)
            .ToArray();

        labels.Should().NotContain(label =>
            label.Contains("Pause", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("Resume", StringComparison.OrdinalIgnoreCase) ||
            label.Contains("All-in-One", StringComparison.OrdinalIgnoreCase));
    }
}
