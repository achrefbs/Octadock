using Octadock.Core.Ipc;

namespace Octadock.Core.Abstractions;

/// <summary>Manages the "launch at login" registration (current-user Run key).</summary>
public interface IStartupRegistration
{
    bool IsEnabled();

    void Enable();

    void Disable();
}

/// <summary>Registers/unregisters the <c>octadock://</c> URL protocol for the current user.</summary>
public interface IProtocolRegistration
{
    bool IsRegistered();

    void Register();

    void Unregister();
}

/// <summary>Arguments forwarded from a second launch to the primary instance.</summary>
public sealed class SecondInstanceEventArgs(IReadOnlyList<string> arguments, string? workingDirectory = null) : EventArgs
{
    public IReadOnlyList<string> Arguments { get; } = arguments;

    /// <summary>The caller's working directory, when known.</summary>
    public string? WorkingDirectory { get; } = workingDirectory;
}

/// <summary>Handles forwarded launch arguments and returns the result to the waiting client.</summary>
public delegate Task<CommandResult> SecondInstanceLaunchHandler(
    object? sender,
    SecondInstanceEventArgs args,
    CancellationToken cancellationToken);

/// <summary>
/// Enforces a single running instance and forwards subsequent launch arguments
/// (protocol activations, CLI commands) to the primary instance over a named
/// pipe.
/// </summary>
public interface ISingleInstanceGuard : IDisposable
{
    /// <summary>Attempts to become the primary instance. Returns false if one is already running.</summary>
    bool TryAcquire();

    /// <summary>Sends this launch's arguments to the already-running primary instance.</summary>
    Task<IpcResponse> ForwardArgumentsAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);

    /// <summary>Begins listening for forwarded arguments (primary instance only).</summary>
    void StartListening();

    /// <summary>Raised on the primary instance when another launch forwards arguments.</summary>
    event SecondInstanceLaunchHandler? SecondInstanceLaunched;
}
