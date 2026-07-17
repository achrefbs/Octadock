using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.FirstRun;
using Octadock.Core.Abstractions;
using Octadock.Core.Capture;
using Octadock.Core.Persistence;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.FirstRun;

public sealed class FirstRunViewModelTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Finish_persists_the_explicit_clipboard_choice(bool enabled)
    {
        var store = new InMemorySettingsStore();
        using var settings = new SettingsService(store);
        await settings.LoadAsync();
        var viewModel = new FirstRunViewModel(
            settings,
            new FakeStartupRegistration(),
            new FakeCaptureExclusion(),
            NullLogger<FirstRunViewModel>.Instance)
        {
            ClipboardHistoryEnabled = enabled,
            LaunchAtLogin = false,
        };

        await viewModel.FinishCommand.ExecuteAsync(null);

        using var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        reloaded.Current.General.FirstRunCompleted.Should().BeTrue();
        reloaded.Current.Clipboard.MonitorEnabled.Should().Be(enabled);
    }

    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool IsEnabled() => false;

        public void Enable()
        {
        }

        public void Disable()
        {
        }
    }

    private sealed class FakeCaptureExclusion : ICaptureExclusion
    {
        public bool IsSupported => true;

        public bool SetExcluded(WindowHandle window, bool excluded) => true;
    }

    private sealed class InMemorySettingsStore : ISettingsStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>(_values, StringComparer.Ordinal));

        public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default)
            => Task.FromResult(_values.TryGetValue(key, out string? value) ? value : null);

        public Task SetAsync(string key, string value, CancellationToken cancellationToken = default)
        {
            _values[key] = value;
            return Task.CompletedTask;
        }

        public Task SetManyAsync(
            IReadOnlyDictionary<string, string> values,
            CancellationToken cancellationToken = default)
        {
            foreach ((string key, string value) in values)
            {
                _values[key] = value;
            }

            return Task.CompletedTask;
        }

        public Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            _values.Remove(key);
            return Task.CompletedTask;
        }
    }
}
