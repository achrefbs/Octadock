using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Octadock.App.CaptureUx;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

/// <summary>
/// Product contract for the stable Dock rail and its handoff to the Capture
/// Shelf. These assertions keep secondary tools from creeping back behind a
/// Dock chevron.
/// </summary>
public sealed class DockCaptureActionPlanTests
{
    [Fact]
    public void Dock_exposes_only_the_four_direct_capture_actions()
    {
        CaptureActionCatalog.DockActions.Select(definition => definition.Action).Should().Equal(
            CaptureAction.Area,
            CaptureAction.Window,
            CaptureAction.FullScreen,
            CaptureAction.Record);
    }

    [Fact]
    public void Shelf_owns_every_secondary_capture_action()
    {
        CaptureActionCatalog.ShelfActions.Select(definition => definition.Action).Should().Equal(
            CaptureAction.AllMonitors,
            CaptureAction.PreviousArea,
            CaptureAction.SelfTimer,
            CaptureAction.Scrolling,
            CaptureAction.Ocr);

        CaptureActionCatalog.DockActions.Select(definition => definition.Action)
            .Should().NotIntersectWith(
                CaptureActionCatalog.ShelfActions.Select(definition => definition.Action));
    }

    [Fact]
    public void Every_action_has_truthful_accessible_copy_without_a_more_modes_prompt()
    {
        CaptureActionDefinition[] definitions = CaptureActionCatalog.DockActions
            .Concat(CaptureActionCatalog.ShelfActions)
            .ToArray();

        definitions.Select(definition => definition.Action).Should().OnlyHaveUniqueItems();
        definitions.Should().OnlyContain(definition =>
            !string.IsNullOrWhiteSpace(definition.Label) &&
            !string.IsNullOrWhiteSpace(definition.AutomationName) &&
            !string.IsNullOrWhiteSpace(definition.ToolTip));
        definitions.SelectMany(definition => new[]
            {
                definition.Label,
                definition.AutomationName,
                definition.ToolTip,
            })
            .Should().NotContain(copy =>
                copy.Contains("More capture modes", StringComparison.OrdinalIgnoreCase));

        CaptureActionCatalog.Get(CaptureAction.Record).ToolTip.Should().Contain("(Beta)");
        CaptureActionCatalog.Get(CaptureAction.Scrolling).Label.Should()
            .Contain("manual vertical")
            .And.Contain("(Beta)");
    }

    [Fact]
    public void Capture_action_service_is_a_single_shared_surface_seam()
    {
        var services = new ServiceCollection();

        services.AddCaptureUx();

        ServiceDescriptor registration = services.Should()
            .ContainSingle(descriptor => descriptor.ServiceType == typeof(ICaptureActionService))
            .Subject;
        registration.Lifetime.Should().Be(ServiceLifetime.Singleton);
    }
}
