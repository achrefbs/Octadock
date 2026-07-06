using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.LicenseService.Configuration;
using Octadock.LicenseService.Reconciliation;
using Xunit;

namespace Octadock.LicenseService.Tests;

/// <summary>
/// Live Stripe reconciliation source (WS3, R3): against a FAKE handler it extracts
/// PAID launch-price session ids, follows pagination via has_more/starting_after, and
/// reports empty + not-configured when no API key is set.
/// </summary>
public class StripePaidSessionSourceTests
{
    private static readonly DateTimeOffset Since = new(2026, 7, 6, 0, 0, 0, TimeSpan.Zero);

    private static LicenseServiceOptions Options(string apiKey = "rk_test_restricted")
        => new()
        {
            StripeApiKey = apiKey,
            StripeApiBaseUrl = "https://api.stripe.test",
            ExpectedStripePriceId = "price_1TqH3lKNDvjYLyJhld4yYPIZ",
            ExpectedStripePriceLookupKey = "octadock_local_beta_usd_49",
            ExpectedAmountCents = 4900,
            ExpectedCurrency = "usd",
        };

    private static StripePaidSessionSource Build(LicenseServiceOptions options, FakeHandler handler)
        => new(options, handler, NullLogger<StripePaidSessionSource>.Instance);

    [Fact]
    public async Task Unconfigured_source_returns_empty_and_is_not_configured()
    {
        var handler = new FakeHandler(); // never asked
        var source = Build(Options(apiKey: string.Empty), handler);

        source.IsConfigured.Should().BeFalse();
        (await source.GetPaidSessionIdsAsync(Since, CancellationToken.None)).Should().BeEmpty();
        handler.CallCount.Should().Be(0, "an unconfigured source must not hit the network");
    }

    [Fact]
    public async Task Extracts_only_paid_launch_price_session_ids()
    {
        string page = Page(
            hasMore: false,
            Session("cs_paid_match", "paid", priceId: "price_1TqH3lKNDvjYLyJhld4yYPIZ"),
            Session("cs_unpaid", "unpaid", priceId: "price_1TqH3lKNDvjYLyJhld4yYPIZ"),
            Session("cs_wrong_price", "paid", priceId: "price_other"));
        var handler = new FakeHandler(page);
        var source = Build(Options(), handler);

        IReadOnlyList<string> ids = await source.GetPaidSessionIdsAsync(Since, CancellationToken.None);

        source.IsConfigured.Should().BeTrue();
        ids.Should().ContainSingle().Which.Should().Be("cs_paid_match");
    }

    [Fact]
    public async Task Matches_launch_price_by_amount_and_currency_when_line_items_absent()
    {
        string page = Page(
            hasMore: false,
            SessionByAmount("cs_amount_match", "paid", amount: 4900, currency: "usd"),
            SessionByAmount("cs_amount_wrong", "paid", amount: 9900, currency: "usd"));
        var source = Build(Options(), new FakeHandler(page));

        IReadOnlyList<string> ids = await source.GetPaidSessionIdsAsync(Since, CancellationToken.None);

        ids.Should().ContainSingle().Which.Should().Be("cs_amount_match");
    }

    [Fact]
    public async Task Follows_pagination_via_has_more_and_starting_after()
    {
        string first = Page(
            hasMore: true,
            Session("cs_p1", "paid", priceId: "price_1TqH3lKNDvjYLyJhld4yYPIZ"));
        string second = Page(
            hasMore: false,
            Session("cs_p2", "paid", priceId: "price_1TqH3lKNDvjYLyJhld4yYPIZ"));
        var handler = new FakeHandler(first, second);
        var source = Build(Options(), handler);

        IReadOnlyList<string> ids = await source.GetPaidSessionIdsAsync(Since, CancellationToken.None);

        ids.Should().BeEquivalentTo(new[] { "cs_p1", "cs_p2" });
        handler.CallCount.Should().Be(2, "two pages must be fetched");
        handler.LastRequestUri.Should().Contain("starting_after=cs_p1",
            "the second page must use the last id of the first as the cursor");
    }

    [Fact]
    public async Task Sends_the_restricted_key_as_a_bearer_token()
    {
        var handler = new FakeHandler(Page(hasMore: false));
        var source = Build(Options(apiKey: "rk_test_abc"), handler);

        await source.GetPaidSessionIdsAsync(Since, CancellationToken.None);

        handler.LastAuthorization.Should().Be("Bearer rk_test_abc");
    }

    // ---- fakes / JSON builders (plain concatenation to avoid brace-escaping noise) ----

    private static string Session(string id, string paymentStatus, string priceId)
        => "{\"id\":\"" + id + "\",\"payment_status\":\"" + paymentStatus + "\"," +
           "\"line_items\":{\"data\":[{\"price\":{\"id\":\"" + priceId + "\",\"lookup_key\":\"other\"}}]}}";

    private static string SessionByAmount(string id, string paymentStatus, long amount, string currency)
        => "{\"id\":\"" + id + "\",\"payment_status\":\"" + paymentStatus + "\"," +
           "\"amount_total\":" + amount.ToString(System.Globalization.CultureInfo.InvariantCulture) +
           ",\"currency\":\"" + currency + "\"}";

    private static string Page(bool hasMore, params string[] sessions)
        => "{\"object\":\"list\",\"has_more\":" + (hasMore ? "true" : "false") +
           ",\"data\":[" + string.Join(",", sessions) + "]}";

    /// <summary>A canned-response HttpMessageHandler that returns queued page bodies in order.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public FakeHandler(params string[] responses) => _responses = new Queue<string>(responses);

        public int CallCount { get; private set; }

        public string? LastRequestUri { get; private set; }

        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            LastRequestUri = request.RequestUri?.ToString();
            LastAuthorization = request.Headers.Authorization is null
                ? null
                : $"{request.Headers.Authorization.Scheme} {request.Headers.Authorization.Parameter}";

            string body = _responses.Count > 0
                ? _responses.Dequeue()
                : """{"object":"list","has_more":false,"data":[]}""";

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
