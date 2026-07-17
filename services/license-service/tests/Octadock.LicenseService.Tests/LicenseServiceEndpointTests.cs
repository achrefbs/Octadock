using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Octadock.LicenseService.Data;
using Xunit;

namespace Octadock.LicenseService.Tests;

public sealed class LicenseServiceEndpointTests
{
    private const string AdminToken = "endpoint-admin-token";
    private const string WebhookSecret = "whsec_endpoint_test";

    [Fact]
    public async Task PublicHealthIsMinimal()
    {
        using var factory = new LicenseServiceFactory(adminToken: null);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/health");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(body);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("status");
        json.RootElement.GetProperty("status").GetString().Should().Be("ok");
        body.Should().NotContain("license");
        body.Should().NotContain("webhook");
        body.Should().NotContain("activation");
        body.Should().NotContain("reconciliation");
    }

    [Fact]
    public async Task AdminHealthFailsClosedWhenTokenIsNotConfigured()
    {
        using var factory = new LicenseServiceFactory(adminToken: null);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync("/admin/health");
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        AssertNoStore(response);
        body.Should().NotContain("Licenses total");
        body.Should().NotContain("Launch Health");
    }

    [Fact]
    public async Task AdminHealthRejectsMissingWrongAndQueryOnlyTokens()
    {
        using var factory = new LicenseServiceFactory(AdminToken);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage missing = await client.GetAsync("/admin/health");
        using HttpResponseMessage wrong = await SendAdminRequest(client, headerToken: "wrong-token");
        using HttpResponseMessage queryOnly =
            await client.GetAsync($"/admin/health?token={AdminToken}");

        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        queryOnly.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        AssertNoStore(missing);
        AssertNoStore(wrong);
        AssertNoStore(queryOnly);
    }

    [Fact]
    public async Task AdminHealthPreservesValidHeaderAccess()
    {
        using var factory = new LicenseServiceFactory(AdminToken);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await SendAdminRequest(client, AdminToken);
        string body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");
        AssertNoStore(response);
        body.Should().Contain("Octadock License Service — Launch Health");
        body.Should().Contain("Licenses total");
    }

    [Fact]
    public async Task WebhookResponseOmitsLicenseMaterialWhileIssuancePersists()
    {
        using var factory = new LicenseServiceFactory(adminToken: null);
        using HttpClient client = factory.CreateClient();
        string body = Events.CheckoutCompleted(
            "evt_endpoint_response",
            "cs_endpoint_response",
            "pi_endpoint_response");
        string signature = StripeSignatureFactory.Make(
            body,
            WebhookSecret,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        using HttpResponseMessage response = await PostWebhook(client, body, signature);
        string responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument json = JsonDocument.Parse(responseBody);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().BeEquivalentTo("outcome", "message");
        json.RootElement.GetProperty("outcome").GetString().Should().Be("LicenseIssued");
        responseBody.Should().NotContain("licenseKey");
        responseBody.Should().NotContain("OCTA-");

        LicenseRepository repository = factory.Services.GetRequiredService<LicenseRepository>();
        repository.GetLaunchHealth().LicensesTotal.Should().Be(1);

        using HttpResponseMessage replay = await PostWebhook(client, body, signature);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        repository.GetLaunchHealth().LicensesTotal.Should().Be(
            1,
            "a replay must not issue another license");
    }

    private static void AssertNoStore(HttpResponseMessage response)
    {
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    private static async Task<HttpResponseMessage> SendAdminRequest(
        HttpClient client,
        string headerToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/admin/health");
        request.Headers.Add("X-Admin-Token", headerToken);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostWebhook(
        HttpClient client,
        string body,
        string signature)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/stripe")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Stripe-Signature", signature);
        return await client.SendAsync(request);
    }

    private sealed class LicenseServiceFactory : WebApplicationFactory<Program>
    {
        private readonly string? _adminToken;
        private readonly string _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"octadock-endpoints-{Guid.NewGuid():N}.db");

        public LicenseServiceFactory(string? adminToken) => _adminToken = adminToken;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(FindServiceContentRoot());
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["LicenseService:ConnectionString"] = $"Data Source={_databasePath}",
                    ["LicenseService:AdminToken"] = _adminToken ?? string.Empty,
                    ["LicenseService:StripeWebhookSecrets:0"] = WebhookSecret,
                    ["LicenseService:StripeApiKey"] = string.Empty,
                });
            });
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (!disposing)
            {
                return;
            }

            SqliteConnection.ClearAllPools();
            foreach (string path in new[]
                     {
                         _databasePath,
                         _databasePath + "-wal",
                         _databasePath + "-shm",
                     })
            {
                try
                {
                    File.Delete(path);
                }
                catch (IOException)
                {
                    // Best-effort cleanup of the unique test database.
                }
            }
        }

        private static string FindServiceContentRoot()
        {
            DirectoryInfo? current = new(AppContext.BaseDirectory);
            while (current is not null)
            {
                string candidate = Path.Combine(
                    current.FullName,
                    "services",
                    "license-service",
                    "src",
                    "Octadock.LicenseService");
                if (File.Exists(Path.Combine(candidate, "Octadock.LicenseService.csproj")))
                {
                    return candidate;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the license-service content root.");
        }
    }
}
