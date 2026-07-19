using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;
using Octadock.Core.Ipc;

namespace Octadock.Platform.Windows.System;

/// <summary>
/// Enforces a single running instance with a per-user named <see cref="Mutex"/>
/// and forwards subsequent launch arguments to the primary instance over a named
/// pipe (<see cref="IpcProtocol"/>). The primary calls <see cref="StartListening"/>;
/// secondary launches call <see cref="ForwardArgumentsAsync"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SingleInstanceGuard : ISingleInstanceGuard
{
    // A stable, per-user mutex name. "Local\" scopes it to the current session.
    private static readonly string MutexName = @"Local\Octadock.SingleInstance." + StableUserSuffix();

    private readonly ILogger<SingleInstanceGuard> _logger;
    private readonly string _pipeName;
    private readonly object _gate = new();

    private Mutex? _mutex;
    private bool _ownsMutex;
    private CancellationTokenSource? _listenCts;
    private Task? _listenTask;
    private readonly TaskCompletionSource _handlerReady =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private SecondInstanceLaunchHandler? _secondInstanceLaunched;
    private bool _disposed;

    /// <summary>Creates the guard for the current user.</summary>
    public SingleInstanceGuard(ILogger<SingleInstanceGuard> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeName = IpcProtocol.PipeName(Environment.UserName);
    }

    /// <inheritdoc />
    public event SecondInstanceLaunchHandler? SecondInstanceLaunched
    {
        add
        {
            lock (_gate)
            {
                _secondInstanceLaunched += value;
                if (_secondInstanceLaunched is not null)
                {
                    _handlerReady.TrySetResult();
                }
            }
        }

        remove
        {
            lock (_gate)
            {
                _secondInstanceLaunched -= value;
            }
        }
    }

    /// <inheritdoc />
    public bool TryAcquire()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_mutex is not null)
            {
                return _ownsMutex;
            }

            try
            {
                _mutex = new Mutex(initiallyOwned: true, MutexName, out bool createdNew);
                _ownsMutex = createdNew;

                if (!createdNew)
                {
                    // Another instance owns it; release our handle's ownership attempt.
                    _mutex.Dispose();
                    _mutex = null;
                }

                return _ownsMutex;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to acquire the single-instance mutex.");
                _mutex = null;
                _ownsMutex = false;

                // Fail open: allow this instance to run rather than blocking the app.
                return true;
            }
        }
    }

    /// <inheritdoc />
    public void StartListening()
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            if (_listenTask is not null)
            {
                return;
            }

            _listenCts = new CancellationTokenSource();
            _listenTask = Task.Run(() => ListenLoopAsync(_listenCts.Token));
        }
    }

    /// <inheritdoc />
    public async Task<IpcResponse> ForwardArgumentsAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ThrowIfDisposed();

        var request = new IpcRequest
        {
            Arguments = arguments,
            WorkingDirectory = Environment.CurrentDirectory,
        };
        string payload = IpcProtocol.SerializeRequest(request);

        try
        {
            // CurrentUserOnly restricts the pipe ACL to this user's SID on both
            // ends, so another local user or a lower-integrity process cannot
            // connect and inject commands into this session.
            using var client = new NamedPipeClientStream(
                ".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await client.ConnectAsync(connectCts.Token).ConfigureAwait(false);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
            using var reader = new StreamReader(client, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            // One JSON line request, one JSON line response.
            await writer.WriteLineAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);

            string? responseLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (responseLine is not null)
            {
                IpcResponse? response = IpcProtocol.DeserializeResponse(responseLine);
                if (response is null)
                {
                    return IpcResponse.Fail("Primary instance returned a malformed response.");
                }

                if (!response.Success)
                {
                    _logger.LogDebug("Primary instance reported failure: {Message}", response.Message);
                }

                return response;
            }

            return IpcResponse.Fail("Primary instance closed the connection without replying.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to forward arguments to the primary instance.");
            throw;
        }
    }

    private async Task ListenLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                // CurrentUserOnly restricts the pipe's ACL to the current user's
                // SID, closing the local cross-user / low-integrity command
                // injection vector on this deterministically-named pipe.
                server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                _ = HandleConnectedServerAsync(server, cancellationToken);
                server = null;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                server?.Dispose();
                _logger.LogWarning(ex, "IPC listener error; continuing.");

                // Avoid a hot loop if the pipe fails repeatedly.
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectedServerAsync(
        NamedPipeServerStream server,
        CancellationToken cancellationToken)
    {
        try
        {
            await HandleConnectionAsync(server, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown path.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC connection error.");
        }
        finally
        {
            server.Dispose();
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream server, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(server, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        using var writer = new StreamWriter(server, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };

        // A client that connects but never sends a line must not pin a server
        // instance forever; bound the read so idle/abusive connections drop.
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCts.CancelAfter(TimeSpan.FromSeconds(5));
        string? line = await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
        IpcResponse response;

        if (string.IsNullOrWhiteSpace(line))
        {
            response = IpcResponse.Fail("Empty request.");
        }
        else
        {
            try
            {
                IpcRequest? request = IpcProtocol.DeserializeRequest(line);
                if (request is null)
                {
                    response = IpcResponse.Fail("Malformed request.");
                }
                else
                {
                    CommandResult result = await RaiseSecondInstanceAsync(
                        request.Arguments,
                        request.WorkingDirectory,
                        cancellationToken).ConfigureAwait(false);
                    response = result.Success
                        ? IpcResponse.Ok(result.Message, result.CaptureId)
                        : IpcResponse.Fail(result.Message ?? "Command failed.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to process a forwarded launch.");
                response = IpcResponse.Fail("Internal error processing the request.");
            }
        }

        try
        {
            await writer.WriteLineAsync(IpcProtocol.SerializeResponse(response).AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // Client may have disconnected before reading the reply; ignore.
        }
    }

    private async Task<CommandResult> RaiseSecondInstanceAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        CancellationToken cancellationToken)
    {
        try
        {
            SecondInstanceLaunchHandler? handler = _secondInstanceLaunched;
            if (handler is null)
            {
                await _handlerReady.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                handler = _secondInstanceLaunched;
            }

            if (handler is null)
            {
                return CommandResult.Fail("The primary instance is not ready to process forwarded commands.");
            }

            return await handler(
                this,
                new SecondInstanceEventArgs(arguments, workingDirectory),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "A SecondInstanceLaunched handler threw.");
            return CommandResult.Fail("Internal error processing the request.");
        }
    }

    private static string StableUserSuffix()
    {
        // Derive a stable, filesystem/registry-safe suffix from the pipe name,
        // which already hashes the user name.
        string pipe = IpcProtocol.PipeName(Environment.UserName);
        int lastDot = pipe.LastIndexOf('.');
        return lastDot >= 0 && lastDot < pipe.Length - 1 ? pipe[(lastDot + 1)..] : "default";
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

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

        try
        {
            _listenCts?.Cancel();
            _listenTask?.Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error stopping the IPC listener.");
        }
        finally
        {
            _listenCts?.Dispose();

            if (_mutex is not null)
            {
                try
                {
                    if (_ownsMutex)
                    {
                        _mutex.ReleaseMutex();
                    }
                }
                catch (ApplicationException)
                {
                    // Not owned on this thread; safe to ignore during teardown.
                }
                finally
                {
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
        }
    }
}
