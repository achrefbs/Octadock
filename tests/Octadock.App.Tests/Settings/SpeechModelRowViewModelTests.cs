using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.App.Settings;
using Octadock.Core.Abstractions;
using Xunit;

namespace Octadock.App.Tests.Settings;

public sealed class SpeechModelRowViewModelTests
{
    [Fact]
    public async Task Cancel_stops_the_download_and_keeps_retry_available()
    {
        var provider = new BlockingModelProvider();
        var row = new SpeechModelRowViewModel(provider, "small", "Whisper small", NullLogger.Instance);

        Task download = row.DownloadCommand.ExecuteAsync(null);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        row.CanCancel.Should().BeTrue("a download in flight can be cancelled");
        row.CancelDownloadCommand.Execute(null);

        await download.WaitAsync(TimeSpan.FromSeconds(2));
        row.IsBusy.Should().BeFalse();
        row.CanCancel.Should().BeFalse();
        row.StatusText.Should().Contain("cancelled");
        row.CanDownload.Should().BeTrue("retry stays available after a cancel");
        provider.OnDisk.Should().BeFalse("a cancelled download never claims the model is usable");
    }

    [Fact]
    public async Task Failed_download_reports_the_failure_and_keeps_retry_available()
    {
        var provider = new BlockingModelProvider { FailWith = "network unreachable" };
        var row = new SpeechModelRowViewModel(provider, "small", "Whisper small", NullLogger.Instance);

        await row.DownloadCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(2));

        row.StatusText.Should().Contain("network unreachable");
        row.StatusText.Should().Contain("retry");
        row.CanDownload.Should().BeTrue();
    }

    [Fact]
    public void Delete_removes_the_model_and_updates_the_row()
    {
        var provider = new BlockingModelProvider { OnDisk = true };
        var row = new SpeechModelRowViewModel(provider, "small", "Whisper small", NullLogger.Instance);

        row.CanDelete.Should().BeTrue();
        row.DeleteCommand.Execute(null);

        provider.OnDisk.Should().BeFalse();
        row.CanDownload.Should().BeTrue();
        row.CanDelete.Should().BeFalse();
        row.StatusText.Should().Contain("Not downloaded");
    }

    private sealed class BlockingModelProvider : IModelBackedSpeechProvider
    {
        public bool OnDisk { get; set; }

        public string? FailWith { get; set; }

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "whisper";

        public bool IsAvailable => true;

        public bool IsModelAvailable(string? model) => OnDisk;

        public long ModelDownloadBytes(string? model) => 100 * 1024 * 1024;

        public async Task EnsureModelAsync(
            string? model,
            IProgress<double>? progress,
            CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            if (FailWith is not null)
            {
                throw new InvalidOperationException(FailWith);
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            OnDisk = true;
        }

        public void DeleteModel(string? model) => OnDisk = false;

        public Task<SttResult> TranscribeAsync(
            AudioBuffer audio, SttOptions options, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
