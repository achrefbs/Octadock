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

    [Fact]
    public async Task Launch_at_login_is_on_by_default_for_fresh_profiles()
    {
        var store = new InMemorySettingsStore();
        using var settings = new SettingsService(store);
        await settings.LoadAsync();
        var viewModel = new FirstRunViewModel(
            settings,
            new FakeStartupRegistration(),
            new FakeCaptureExclusion(),
            NullLogger<FirstRunViewModel>.Instance);

        viewModel.LaunchAtLogin.Should().BeTrue(
            "launch-at-login defaults on; first run is the explicit opt-out");
    }

    [Fact]
    public async Task Finish_persists_the_launch_at_login_opt_out()
    {
        var store = new InMemorySettingsStore();
        using var settings = new SettingsService(store);
        await settings.LoadAsync();
        var startup = new FakeStartupRegistration();
        var viewModel = new FirstRunViewModel(
            settings,
            startup,
            new FakeCaptureExclusion(),
            NullLogger<FirstRunViewModel>.Instance)
        {
            LaunchAtLogin = false,
        };

        await viewModel.FinishCommand.ExecuteAsync(null);

        using var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        reloaded.Current.General.LaunchAtLogin.Should().BeFalse();
        startup.DisableCalled.Should().BeTrue();
        startup.EnableCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Finish_applies_launch_at_login_when_left_on()
    {
        var store = new InMemorySettingsStore();
        using var settings = new SettingsService(store);
        await settings.LoadAsync();
        var startup = new FakeStartupRegistration();
        var viewModel = new FirstRunViewModel(
            settings,
            startup,
            new FakeCaptureExclusion(),
            NullLogger<FirstRunViewModel>.Instance);

        await viewModel.FinishCommand.ExecuteAsync(null);

        using var reloaded = new SettingsService(store);
        await reloaded.LoadAsync();
        reloaded.Current.General.LaunchAtLogin.Should().BeTrue();
        startup.EnableCalled.Should().BeTrue();
    }
    private sealed class FakeStartupRegistration : IStartupRegistration
    {
        public bool EnableCalled { get; private set; }

        public bool DisableCalled { get; private set; }

        public bool IsEnabled() => false;

        public void Enable() => EnableCalled = true;

        public void Disable() => DisableCalled = true;
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
