using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using Octadock.Core.Ipc;

namespace Octadock.Cli;

/// <summary>
/// The outcome of forwarding a request to the Octadock tray app.
/// </summary>
/// <param name="Response">The deserialized reply, when one was received.</param>
/// <param name="ErrorMessage">A human-readable transport error, when the round-trip failed.</param>
/// <param name="ExitCode">The exit code to return to the shell.</param>
internal readonly record struct ForwardOutcome(IpcResponse? Response, string? ErrorMessage, int ExitCode)
{
    public static ForwardOutcome FromResponse(IpcResponse response) =>
        new(response, null, response.ExitCode);

    public static ForwardOutcome Error(string message) =>
        new(null, message, ExitCodes.RuntimeError);
}

/// <summary>
/// Connects <c>octadock.exe</c> to the running Octadock tray instance over the
/// per-user named pipe defined by <see cref="IpcProtocol"/>, launching the app if
/// necessary, then exchanges a single request/response line. All operations honour
/// the supplied <see cref="CancellationToken"/> (driven by the CLI's global timeout).
/// </summary>
internal sealed class PipeClient
{
    /// <summary>The tray application's assembly/executable name (sits next to octadock.exe after publish).</summary>
    private const string AppExecutableName = "Octadock.exe";

    /// <summary>How long to wait for a freshly launched app to open its pipe before giving up.</summary>
    private static readonly TimeSpan LaunchWaitBudget = TimeSpan.FromSeconds(10);

    /// <summary>Poll interval while waiting for a just-launched app to start listening.</summary>
    private static readonly TimeSpan LaunchPollInterval = TimeSpan.FromMilliseconds(200);

    private readonly string _pipeName;
    private readonly bool _allowLaunch;

    public PipeClient(bool allowLaunch)
        : this(IpcProtocol.PipeName(Environment.UserName), allowLaunch)
    {
    }

    public PipeClient(string pipeName, bool allowLaunch)
    {
        _pipeName = pipeName;
        _allowLaunch = allowLaunch;
    }

    /// <summary>
    /// Forwards <paramref name="request"/> to the tray app and returns its reply.
    /// If no server is listening the app is launched (unless disabled) and the
    /// connection is retried until it succeeds or the token is cancelled.
    /// </summary>
    public async Task<ForwardOutcome> ForwardAsync(IpcRequest request, CancellationToken cancellationToken)
    {
        NamedPipeClientStream? pipe = await TryConnectAsync(cancellationToken).ConfigureAwait(false);

        if (pipe is null)
        {
            if (!_allowLaunch)
            {
                return ForwardOutcome.Error(
                    "Octadock is not running and --no-launch was set. Start Octadock and try again.");
            }

            if (!TryLaunchApp(out string? launchError))
            {
                return ForwardOutcome.Error(launchError!);
            }

            pipe = await WaitForServerAsync(cancellationToken).ConfigureAwait(false);
            if (pipe is null)
            {
                return ForwardOutcome.Error(
                    "Octadock was started but did not accept a connection in time. Please try again.");
            }
        }

        await using (pipe.ConfigureAwait(false))
        {
            return await ExchangeAsync(pipe, request, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Attempts a single, immediate connection. Returns <c>null</c> when no server is listening.</summary>
    private async Task<NamedPipeClientStream?> TryConnectAsync(CancellationToken cancellationToken)
    {
        var pipe = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            // A near-zero client-side wait: either the server is up now or it is not.
            await pipe.ConnectAsync(50, cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch (TimeoutException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
        catch (IOException)
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            return null;
        }
    }

    /// <summary>Polls for the server to come up after a launch, until connected or the token trips.</summary>
    private async Task<NamedPipeClientStream?> WaitForServerAsync(CancellationToken cancellationToken)
    {
        using var launchTimeout = new CancellationTokenSource(LaunchWaitBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, launchTimeout.Token);

        while (!linked.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe;
            try
            {
                pipe = await TryConnectAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (pipe is not null)
            {
                return pipe;
            }

            try
            {
                await Task.Delay(LaunchPollInterval, linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    /// <summary>Writes one request line and reads one response line, deserializing via <see cref="IpcProtocol"/>.</summary>
    private static async Task<ForwardOutcome> ExchangeAsync(
        NamedPipeClientStream pipe,
        IpcRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            // One JSON object per line, matching the tray app's line-based reader.
            var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8);

            string payload = IpcProtocol.SerializeRequest(request);
            await writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            string? line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(line))
            {
                return ForwardOutcome.Error("Octadock closed the connection without replying.");
            }

            IpcResponse? response = IpcProtocol.DeserializeResponse(line);
            return response is null
                ? ForwardOutcome.Error("Octadock returned a malformed response.")
                : ForwardOutcome.FromResponse(response);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return ForwardOutcome.Error($"The connection to Octadock was interrupted: {ex.Message}");
        }
    }

    /// <summary>Locates <c>Octadock.exe</c> next to the CLI and starts it.</summary>
    private static bool TryLaunchApp(out string? error)
    {
        error = null;
        string? directory = Path.GetDirectoryName(Environment.ProcessPath)
            ?? AppContext.BaseDirectory;

        if (string.IsNullOrEmpty(directory))
        {
            error = "Could not determine the Octadock install directory.";
            return false;
        }

        string? appPath = FindAppExecutable(directory);
        if (appPath is null)
        {
            error = $"Could not find '{AppExecutableName}' next to the CLI or in its parent directory ({directory}). " +
                    "Is Octadock installed?";
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = appPath,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(appPath) ?? directory,
            };
            using Process? process = Process.Start(startInfo);
            if (process is null)
            {
                error = "Failed to start Octadock.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            error = $"Failed to start Octadock: {ex.Message}";
            return false;
        }
    }

    private static string? FindAppExecutable(string cliDirectory)
    {
        string local = Path.Combine(cliDirectory, AppExecutableName);
        if (File.Exists(local))
        {
            return local;
        }

        DirectoryInfo? parent = Directory.GetParent(cliDirectory);
        if (parent is null)
        {
            return null;
        }

        string parentLocal = Path.Combine(parent.FullName, AppExecutableName);
        return File.Exists(parentLocal) ? parentLocal : null;
    }
}
