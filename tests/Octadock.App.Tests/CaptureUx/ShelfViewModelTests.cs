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
    [InlineData(ShelfSize.Small, 196, 104, 104)]
    [InlineData(ShelfSize.Medium, 228, 124, 124)]
    [InlineData(ShelfSize.Large, 260, 148, 148)]
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

        viewModel.CardWidth.Should().Be(196);
        viewModel.ThumbnailHeight.Should().Be(104);
        viewModel.RowHeight.Should().Be(104);
        viewModel.ShowChrome.Should().BeFalse();
        viewModel.ThumbnailWidth.Should().Be(196);

        await settings.SaveAsync(OctadockSettings.Defaults with
        {
            Shelf = OctadockSettings.Defaults.Shelf with
            {
                Size = ShelfSize.Large,
                ShowChrome = true,
            },
        });

        viewModel.CardWidth.Should().Be(260);
        viewModel.ThumbnailHeight.Should().Be(148);
        viewModel.RowHeight.Should().Be(148);
        viewModel.ShowChrome.Should().BeTrue();
        viewModel.ThumbnailWidth.Should().Be(248, "the optional frame owns a six-pixel inset on each side");
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
