using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Octadock.Core.Ipc;

/// <summary>
/// The tiny line-based protocol the <c>octadock.exe</c> CLI and the protocol
/// handler use to forward a launch to the already-running tray instance over a
/// named pipe. Keeping the contract in Core means the CLI (which references only
/// Core) and the WPF app agree on names and shapes.
/// </summary>
public static class IpcProtocol
{
    /// <summary>Protocol version, bumped on breaking changes to the message shapes.</summary>
    public const int Version = 1;

    private const string PipePrefix = "Octadock.Ipc.v1.";

    /// <summary>
    /// The per-user pipe name. Derived from the current user so multiple users on
    /// one machine (or fast-user-switching) do not collide. Pass the value of
    /// <c>Environment.UserName</c>.
    /// </summary>
    public static string PipeName(string userName)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(userName ?? string.Empty));
        string suffix = Convert.ToHexString(hash, 0, 6);
        return PipePrefix + suffix;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string SerializeRequest(IpcRequest request) => JsonSerializer.Serialize(request, JsonOptions);

    public static IpcRequest? DeserializeRequest(string json) => JsonSerializer.Deserialize<IpcRequest>(json, JsonOptions);

    public static string SerializeResponse(IpcResponse response) => JsonSerializer.Serialize(response, JsonOptions);

    public static IpcResponse? DeserializeResponse(string json) => JsonSerializer.Deserialize<IpcResponse>(json, JsonOptions);
}

/// <summary>A forwarded launch: the raw CLI arguments (a single element holds a <c>octadock://</c> URI).</summary>
public sealed record IpcRequest
{
    public int Version { get; init; } = IpcProtocol.Version;

    /// <summary>The argument vector, exactly as received by the second instance.</summary>
    public IReadOnlyList<string> Arguments { get; init; } = [];

    /// <summary>The caller's working directory, when known (used by watched run commands).</summary>
    public string? WorkingDirectory { get; init; }
}

/// <summary>The primary instance's reply to a forwarded launch.</summary>
public sealed record IpcResponse
{
    public bool Success { get; init; }

    public string? Message { get; init; }

    public int ExitCode { get; init; }

    public static IpcResponse Ok(string? message = null) => new() { Success = true, Message = message, ExitCode = 0 };

    public static IpcResponse Fail(string message, int exitCode = 1) => new() { Success = false, Message = message, ExitCode = exitCode };
}
