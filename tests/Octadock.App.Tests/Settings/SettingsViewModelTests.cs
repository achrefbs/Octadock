using FluentAssertions;
using Octadock.App.Settings;
using Xunit;

namespace Octadock.App.Tests.Settings;

public sealed class SettingsViewModelTests
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Positive_capture_visibility_maps_to_the_stable_exclusion_value(
        bool includeOctadock,
        bool expectedExclusion)
    {
        SettingsViewModel.InvertOctadockCaptureVisibility(includeOctadock)
            .Should().Be(expectedExclusion);
    }
}
