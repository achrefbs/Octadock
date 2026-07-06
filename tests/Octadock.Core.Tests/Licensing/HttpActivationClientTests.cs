using System.Net;
using System.Text;
using FluentAssertions;
using Octadock.Core.Licensing;
using Xunit;

namespace Octadock.Core.Tests.Licensing;

/// <summary>Maps the license service's /activate HTTP responses to transport outcomes (WS5).</summary>
public class HttpActivationClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? LastBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }

    private static HttpActivationClient Client(StubHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost/") });

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task Posts_the_key_and_machine_hash_to_the_activate_endpoint()
    {
        var handler = new StubHandler(_ => Json(HttpStatusCode.OK,
            """{"outcome":"Activated","activeDevices":1,"deviceLimit":3,"entitlement":"{\"schema\":1}"}"""));
        var client = Client(handler);

        ActivationClientResponse result = await client.ActivateAsync("OCTA-KEY", "machinehash", 1, default);

        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.Should().Be("/activate");
        handler.LastBody.Should().Contain("OCTA-KEY").And.Contain("machinehash");
        result.Transport.Should().Be(ActivationTransport.Activated);
        result.EntitlementJson.Should().Be("""{"schema":1}""");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, ActivationTransport.DeviceLimitReached)]
    [InlineData(HttpStatusCode.Forbidden, ActivationTransport.LicenseNotActive)]
    [InlineData(HttpStatusCode.NotFound, ActivationTransport.LicenseNotFound)]
    [InlineData(HttpStatusCode.InternalServerError, ActivationTransport.ProtocolError)]
    public async Task Http_status_maps_to_the_transport_outcome(HttpStatusCode status, ActivationTransport expected)
    {
        var handler = new StubHandler(_ => Json(status, """{"outcome":"x","activeDevices":3,"deviceLimit":3}"""));
        var client = Client(handler);

        ActivationClientResponse result = await client.ActivateAsync("OCTA-KEY", "m", 1, default);

        result.Transport.Should().Be(expected);
    }

    [Fact]
    public async Task A_connection_failure_reports_the_endpoint_as_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("no route to host"));
        var client = Client(handler);

        ActivationClientResponse result = await client.ActivateAsync("OCTA-KEY", "m", 1, default);

        result.Transport.Should().Be(ActivationTransport.EndpointUnavailable);
    }
}
