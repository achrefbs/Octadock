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
    [InlineData(ShelfSize.Small, 176, 99, 99)]
    [InlineData(ShelfSize.Medium, 192, 108, 108)]
    [InlineData(ShelfSize.Large, 224, 126, 126)]
    public void GetLayoutMetrics_maps_setting_to_card_dimensions(
        ShelfSize size,
        double expectedCardWidth,
        double expectedThumbnailHeight,
        double expectedRowHeight)
    {
        ShelfLayoutMetrics metrics = ShelfViewModel.GetLayoutMetrics(size);

        metrics.CardWidth.Should().Be(expectedCardWidth);
        metrics.ThumbnailHeight.Should().Be(expectedThumbnailHeight);
        metrics.RowHeight.Should().Be(expectedRowHeight);
        (metrics.CardWidth / metrics.ThumbnailHeight)
            .Should().BeApproximately(16d / 9d, 0.0001);
    }

    [Fact]
    public async Task Constructor_and_settings_change_apply_shelf_size_metrics()
    {
        var settings = new TestSettingsService
        {
            Current = OctadockSettings.Defaults with
            {
                Shelf = OctadockSettings.Defaults.Shelf with
                {
                    Size = ShelfSize.Small,
                    ShowChrome = false,
                },
            },
        };

        using ServiceProvider services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new ShelfViewModel(services, settings, NullLoggerFactory.Instance);

        viewModel.CardWidth.Should().Be(176);
        viewModel.ThumbnailHeight.Should().Be(99);
        viewModel.RowHeight.Should().Be(99);
        viewModel.ThumbnailWidth.Should().Be(176);
        viewModel.ShellWidth.Should().Be(188);

        await settings.SaveAsync(settings.Current with
        {
            Shelf = settings.Current.Shelf with { ShowChrome = true },
        });

        viewModel.ThumbnailWidth.Should().Be(176,
            "the legacy presentation value cannot change the fixed tile canvas");
        viewModel.ShellWidth.Should().Be(188,
            "the compact unified shell is always six DIPs wider on each side");

        await settings.SaveAsync(OctadockSettings.Defaults with
        {
            Shelf = OctadockSettings.Defaults.Shelf with
            {
                Size = ShelfSize.Large,
                ShowChrome = true,
            },
        });

        viewModel.CardWidth.Should().Be(224);
        viewModel.ThumbnailHeight.Should().Be(126);
        viewModel.RowHeight.Should().Be(126);
        viewModel.ThumbnailWidth.Should().Be(224);
        viewModel.ShellWidth.Should().Be(236);
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
