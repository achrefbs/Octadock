using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Diagnostics;
using Octadock.Core.Abstractions;
using Octadock.Core.Common;
using Octadock.Core.Services;
using Octadock.Core.Settings;
using Xunit;

namespace Octadock.App.Tests.Diagnostics;

public sealed class CrashReportServiceTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 3, 10, 15, 30, 123, TimeSpan.Zero);

    private readonly string _root;
    private readonly StoragePaths _paths;
    private readonly TestClock _clock = new(Now);
    private readonly TestSettingsService _settings = new();

    public CrashReportServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "OctadockCrashReportTests", Guid.NewGuid().ToString("N"));
        _paths = new StoragePaths(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void TryWrite_returns_null_and_writes_nothing_when_setting_is_disabled()
    {
        var service = CreateService();

        string? filePath = service.TryWrite(new InvalidOperationException("boom"), "Dispatcher");

        filePath.Should().BeNull();
        Directory.Exists(Path.Combine(_root, CrashReportService.CrashReportsFolder)).Should().BeFalse();
    }

    [Fact]
    public void TryWrite_saves_local_json_when_setting_is_enabled()
    {
        _settings.Current = OctadockSettings.Defaults with
        {
            General = OctadockSettings.Defaults.General with { CrashReportingEnabled = true },
        };
        var service = CreateService();

        string? filePath = service.TryWrite(
            new InvalidOperationException("boom"),
            "Dispatcher Unhandled",
            isTerminating: true);

        filePath.Should().NotBeNull();
        File.Exists(filePath!).Should().BeTrue();
        Path.GetFileName(filePath).Should().Be("octadock-crash-20260703-101530-1230000-dispatcher-unhandled.json");

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath!));
        JsonElement root = document.RootElement;
        root.GetProperty("application").GetString().Should().Be("Octadock");
        root.GetProperty("source").GetString().Should().Be("Dispatcher Unhandled");
        root.GetProperty("timestampUtc").GetDateTimeOffset().Should().Be(Now);
        root.GetProperty("isTerminating").GetBoolean().Should().BeTrue();
        root.GetProperty("exception").GetProperty("type").GetString()
            .Should().Be(typeof(InvalidOperationException).FullName);
        root.GetProperty("exception").GetProperty("message").GetString().Should().Be("boom");
    }

    [Fact]
    public void TryWrite_redacts_data_root_from_exception_fields()
    {
        _settings.Current = OctadockSettings.Defaults with
        {
            General = OctadockSettings.Defaults.General with { CrashReportingEnabled = true },
        };
        var service = CreateService();
        string capturePath = Path.Combine(_root, "Captures", "private.png");

        string? filePath = service.TryWrite(
            new InvalidOperationException($"Could not read {capturePath}"),
            "Dispatcher");

        string json = File.ReadAllText(filePath!);
        json.Should().Contain("%OCTADOCK_DATA%");
        json.Should().NotContain(_root);
    }

    [Theory]
    [InlineData("", "unknown")]
    [InlineData("Dispatcher Unhandled", "dispatcher-unhandled")]
    [InlineData("App:Domain/Unhandled", "app-domain-unhandled")]
    public void SanitizeFileToken_creates_filename_safe_tokens(string value, string expected)
    {
        CrashReportService.SanitizeFileToken(value).Should().Be(expected);
    }

    private CrashReportService CreateService()
        => new(
            _settings,
            _paths,
            _clock,
            NullLogger<CrashReportService>.Instance);

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public DateTimeOffset LocalNow => UtcNow;
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
