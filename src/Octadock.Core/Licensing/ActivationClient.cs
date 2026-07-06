using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Octadock.Core.Licensing;

/// <summary>The transport-level outcome of calling the license service <c>/activate</c> endpoint.</summary>
public enum ActivationTransport
{
    /// <summary>The device was activated (or was already active) and an entitlement was returned.</summary>
    Activated,

    /// <summary>The license has no free device slots.</summary>
    DeviceLimitReached,

    /// <summary>No license matched the supplied key.</summary>
    LicenseNotFound,

    /// <summary>The license exists but is not active (revoked / refunded / disputed).</summary>
    LicenseNotActive,

    /// <summary>The license service could not be reached (offline, DNS, host not yet provisioned).</summary>
    EndpointUnavailable,

    /// <summary>The service replied but not in a shape the client understands.</summary>
    ProtocolError,
}

/// <summary>The raw result of an activation HTTP call.</summary>
public sealed record ActivationClientResponse(
    ActivationTransport Transport,
    string? EntitlementJson,
    int? ActiveDevices,
    int? DeviceLimit,
    string? Message);

/// <summary>
/// Calls the license service <c>/activate</c> endpoint: a license key + this device's
/// machine hash yields a signed, device-bound entitlement. The live host
/// (<c>api.octadock.com</c>) is founder-gated (DNS/hosting), so an unreachable endpoint
/// is a first-class, clearly-reported outcome rather than a crash.
/// </summary>
public interface IActivationClient
{
    Task<ActivationClientResponse> ActivateAsync(
        string licenseKey, string machineHash, int deviceHashVersion, CancellationToken cancellationToken);
}

/// <summary>Where the license service lives. The live base URL is founder-gated.</summary>
public sealed class ActivationOptions
{
    /// <summary>Base URL of the license service. Overridable via <c>OCTADOCK_LICENSE_API_BASEURL</c>.</summary>
    public string BaseUrl { get; init; } = "https://api.octadock.com";

    /// <summary>Per-request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// <see cref="IActivationClient"/> over HTTP. Thin: it maps HTTP status → transport
/// outcome and extracts the entitlement JSON; all verification and storage happen in
/// <see cref="ActivationService"/>, so a hostile server can never unlock the app just
/// by returning 200.
/// </summary>
public sealed class HttpActivationClient : IActivationClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly ILogger<HttpActivationClient>? _logger;

    public HttpActivationClient(HttpClient http, ILogger<HttpActivationClient>? logger = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger;
    }

    public async Task<ActivationClientResponse> ActivateAsync(
        string licenseKey, string machineHash, int deviceHashVersion, CancellationToken cancellationToken)
    {
        var request = new ActivateRequestBody(licenseKey, machineHash, deviceHashVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("activate", request, Json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            _logger?.LogWarning(ex, "License service is unreachable at {BaseAddress}.", _http.BaseAddress);
            return new ActivationClientResponse(
                ActivationTransport.EndpointUnavailable, null, null, null,
                "The Octadock license service could not be reached.");
        }

        ActivateResponseBody? body = await TryReadAsync(response, cancellationToken).ConfigureAwait(false);

        return response.StatusCode switch
        {
            HttpStatusCode.OK => new ActivationClientResponse(
                ActivationTransport.Activated, body?.Entitlement, body?.ActiveDevices, body?.DeviceLimit, body?.Outcome),
            HttpStatusCode.Conflict => new ActivationClientResponse(
                ActivationTransport.DeviceLimitReached, null, body?.ActiveDevices, body?.DeviceLimit, body?.Outcome),
            HttpStatusCode.Forbidden => new ActivationClientResponse(
                ActivationTransport.LicenseNotActive, null, null, null, body?.Outcome),
            HttpStatusCode.NotFound => new ActivationClientResponse(
                ActivationTransport.LicenseNotFound, null, null, null, body?.Outcome),
            _ => new ActivationClientResponse(
                ActivationTransport.ProtocolError, null, null, null,
                $"Unexpected response {(int)response.StatusCode} from the license service."),
        };
    }

    private async Task<ActivateResponseBody?> TryReadAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadFromJsonAsync<ActivateResponseBody>(Json, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or HttpRequestException or NotSupportedException)
        {
            _logger?.LogDebug(ex, "Activation response body could not be parsed.");
            return null;
        }
    }

    private sealed record ActivateRequestBody(
        [property: JsonPropertyName("licenseKey")] string LicenseKey,
        [property: JsonPropertyName("machineHash")] string MachineHash,
        [property: JsonPropertyName("deviceHashV")] int DeviceHashV);

    private sealed record ActivateResponseBody(
        [property: JsonPropertyName("outcome")] string? Outcome,
        [property: JsonPropertyName("entitlement")] string? Entitlement,
        [property: JsonPropertyName("activeDevices")] int? ActiveDevices,
        [property: JsonPropertyName("deviceLimit")] int? DeviceLimit);
}
