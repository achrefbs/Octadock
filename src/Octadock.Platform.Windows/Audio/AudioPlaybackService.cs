using Microsoft.Extensions.Logging;
using NAudio.Wave;
using Octadock.Core.Abstractions;

namespace Octadock.Platform.Windows.Audio;

/// <summary>NAudio-backed local playback for generated speech files.</summary>
public sealed partial class AudioPlaybackService : IAudioPlaybackService, IDisposable
{
    private readonly object _gate = new();
    private readonly ILogger<AudioPlaybackService> _logger;
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;
    private TaskCompletionSource? _playbackCompletion;
    private bool _disposed;

    /// <summary>Creates the audio playback service.</summary>
    public AudioPlaybackService(ILogger<AudioPlaybackService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public bool IsPlaying
    {
        get
        {
            lock (_gate)
            {
                return _output?.PlaybackState == PlaybackState.Playing;
            }
        }
    }

    /// <inheritdoc />
    public async Task PlayAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            throw new FileNotFoundException("The generated speech file could not be found.", filePath);
        }

        Stop();

        WaveOutEvent output;
        AudioFileReader reader;
        TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration cancellationRegistration;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            reader = new AudioFileReader(filePath);
            output = new WaveOutEvent();
            output.Init(reader);
            output.PlaybackStopped += OnPlaybackStopped;

            _reader = reader;
            _output = output;
            _playbackCompletion = completion;
            cancellationRegistration = cancellationToken.Register(Stop);
        }

        try
        {
            output.Play();
            await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            cancellationRegistration.Dispose();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        WaveOutEvent? output;
        AudioFileReader? reader;
        TaskCompletionSource? completion;
        lock (_gate)
        {
            output = _output;
            reader = _reader;
            completion = _playbackCompletion;
            _output = null;
            _reader = null;
            _playbackCompletion = null;
        }

        try
        {
            if (output is not null)
            {
                output.PlaybackStopped -= OnPlaybackStopped;
                output.Stop();
            }
        }
        catch (Exception ex)
        {
            LogFailedToStopAudioPlayback(_logger, ex);
        }
        finally
        {
            output?.Dispose();
            reader?.Dispose();
            completion?.TrySetResult();
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        TaskCompletionSource? completion;
        WaveOutEvent? output;
        AudioFileReader? reader;
        lock (_gate)
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            completion = _playbackCompletion;
            output = _output;
            reader = _reader;
            _playbackCompletion = null;
            _output = null;
            _reader = null;
        }

        if (output is not null)
        {
            output.PlaybackStopped -= OnPlaybackStopped;
        }

        output?.Dispose();
        reader?.Dispose();

        if (e.Exception is not null)
        {
            completion?.TrySetException(e.Exception);
        }
        else
        {
            completion?.TrySetResult();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        Stop();
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug, Message = "Failed to stop audio playback.")]
    private static partial void LogFailedToStopAudioPlayback(ILogger logger, Exception exception);
}
