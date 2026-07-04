using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.CaptureUx;
using Octadock.Core.Abstractions;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.CaptureUx;

public sealed class ShelfViewModelTests
{
    [Theory]
    [InlineData(ShelfSize.Small, 196, 112)]
    [InlineData(ShelfSize.Medium, 240, 140)]
    [InlineData(ShelfSize.Large, 300, 176)]
    public void GetLayoutMetrics_maps_setting_to_card_dimensions(
        ShelfSize size,
        double expectedCardWidth,
        double expectedThumbnailHeight)
    {
        ShelfLayoutMetrics metrics = ShelfViewModel.GetLayoutMetrics(size);

        metrics.CardWidth.Should().Be(expectedCardWidth);
        metrics.ThumbnailHeight.Should().Be(expectedThumbnailHeight);
    }

    [Fact]
    public async Task Constructor_and_settings_change_apply_shelf_size_metrics()
    {
        var settings = new TestSettingsService
        {
            Current = OctadockSettings.Defaults with
            {
                Shelf = OctadockSettings.Defaults.Shelf with { Size = ShelfSize.Small },
            },
        };

        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new ShelfViewModel(services, settings, NullLoggerFactory.Instance);

        viewModel.CardWidth.Should().Be(196);
        viewModel.ThumbnailHeight.Should().Be(112);

        await settings.SaveAsync(OctadockSettings.Defaults with
        {
            Shelf = OctadockSettings.Defaults.Shelf with { Size = ShelfSize.Large },
        });

        viewModel.CardWidth.Should().Be(300);
        viewModel.ThumbnailHeight.Should().Be(176);
    }

    private sealed class TestSettingsService : ISettingsService
    {
        public OctadockSettings Current { get; set; } = OctadockSettings.Defaults;

        public event EventHandler<SettingsChangedEventArgs>? Changed;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(OctadockSettings settings, CancellationToken cancellationToken = default)
        {
            Current = settings;
            Changed?.Invoke(this, new SettingsChangedEventArgs(Current));
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Func<OctadockSettings, OctadockSettings> mutate, CancellationToken cancellationToken = default)
            => SaveAsync(mutate(Current), cancellationToken);
    }
}
