using FluentAssertions;
using Octadock.App.CaptureUx;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class ShelfCaptureActionCatalogTests
{
    [Fact]
    public void Compact_strip_contains_only_the_five_approved_secondary_capture_modes()
    {
        ShelfCaptureActionCatalog.CompactActions
            .Select(descriptor => descriptor.Action)
            .Should()
            .Equal(
                ShelfCaptureAction.AllMonitors,
                ShelfCaptureAction.PreviousArea,
                ShelfCaptureAction.Timer,
                ShelfCaptureAction.Scrolling,
                ShelfCaptureAction.Ocr);
    }

    [Fact]
    public void Every_action_has_a_distinct_accessible_label_and_tooltip()
    {
        IReadOnlyList<ShelfCaptureActionDescriptor> actions = ShelfCaptureActionCatalog.CompactActions;

        actions.Should().OnlyContain(descriptor =>
            !string.IsNullOrWhiteSpace(descriptor.Label) &&
            !string.IsNullOrWhiteSpace(descriptor.ToolTip));
        actions.Select(descriptor => descriptor.Label).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Scrolling_and_ocr_copy_remains_truthful()
    {
        ShelfCaptureActionCatalog.Get(ShelfCaptureAction.Scrolling).ToolTip
            .Should().Contain("manual vertical").And.Contain("Beta");
        ShelfCaptureActionCatalog.Get(ShelfCaptureAction.Ocr).ToolTip
            .Should().Contain("local OCR");
    }
}
