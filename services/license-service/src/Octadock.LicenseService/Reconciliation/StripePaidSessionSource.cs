using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Octadock.LicenseService.Configuration;

namespace Octadock.LicenseService.Reconciliation;

/// <summary>
/// LIVE paid-session source (WS3, R3): lists PAID Checkout sessions from the Stripe
/// REST API created at/after a timestamp, filtered to the configured launch price,
/// and returns their session ids for reconciliation against the license DB.
///
/// Uses a restricted, read-only Stripe API key from configuration (never source) and
/// an injectable <see cref="HttpMessageHandler"/> so tests can supply canned JSON —
/// including pagination via <c>has_more</c>/<c>starting_after</c>. The live restricted
/// key is founder-gated (an external blocker); until it is provisioned, Program.cs
/// keeps <see cref="NullPaidSessionSource"/> and <see cref="IsConfigured"/> is false.
/// </summary>
public sealed class StripePaidSessionSource : IPaidSessionSource, IDisposable
{
    // Stripe caps list page size at 100.
    private const int PageSize = 100;

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly LicenseServiceOptions _options;
    private readonly ILogger<StripePaidSessionSource> _logger;

    /// <summary>
    /// Production constructor: builds an <see cref="HttpClient"/> over the supplied
    /// handler (injectable so tests can fake Stripe) targeting the configured base URL.
    /// </summary>
    public StripePaidSessionSource(
        LicenseServiceOptions options,
        HttpMessageHandler handler,
        ILogger<StripePaidSessionSource> logger)
        : this(BuildClient(options, handler), ownsHttpClient: true, options, logger)
    {
    }

    /// <summary>Test/advanced constructor: takes a preconfigured <see cref="HttpClient"/>.</summary>
    public StripePaidSessionSource(
        HttpClient httpClient,
        bool ownsHttpClient,
        LicenseServiceOptions options,
        ILogger<StripePaidSessionSource> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        _httpClient = httpClient;
        _ownsHttpClient = ownsHttpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>True when a restricted Stripe API key is configured.</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.StripeApiKey);

    public async Task<IReadOnlyList<string>> GetPaidSessionIdsAsync(
        DateTimeOffset since, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning(
                "StripePaidSessionSource has no API key; returning no sessions. " +
                "Set LicenseService:StripeApiKey (founder-gated restricted key) to enable live reconciliation.");
            return Array.Empty<string>();
        }

        var paidSessionIds = new List<string>();
        string? startingAfter = null;
        long sinceUnix = since.ToUnixTimeSeconds();

        do
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, BuildPageUri(sinceUnix, startingAfter));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.StripeApiKey);

            using HttpResponseMessage response =
                await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            startingAfter = ExtractPaidSessionIds(root, paidSessionIds);
        }
        while (startingAfter is not null && !cancellationToken.IsCancellationRequested);

        return paidSessionIds;
    }

    /// <summary>
    /// Appends the PAID, launch-price session ids from one list page to
    /// <paramref name="into"/>. Returns the <c>starting_after</c> cursor for the next
    /// page (the last id on this page) when <c>has_more</c> is true, else null.
    /// </summary>
    private string? ExtractPaidSessionIds(JsonElement page, List<string> into)
    {
        if (!page.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        string? lastId = null;
        foreach (JsonElement session in data.EnumerateArray())
        {
            if (session.ValueKind != JsonValueKind.Object ||
                !session.TryGetProperty("id", out JsonElement idElement) ||
                idElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            lastId = idElement.GetString();

            if (IsPaid(session) && MatchesLaunchPrice(session) && lastId is not null)
            {
                into.Add(lastId);
            }
        }

        bool hasMore = page.TryGetProperty("has_more", out JsonElement more) &&
                       more.ValueKind == JsonValueKind.True;
        return hasMore ? lastId : null;
    }

    private static bool IsPaid(JsonElement session)
        => session.TryGetProperty("payment_status", out JsonElement status) &&
           status.ValueKind == JsonValueKind.String &&
           string.Equals(status.GetString(), "paid", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Filters to the configured launch price. Prefers an expanded line-item price id
    /// / lookup key; falls back to the expected amount + currency when line items are
    /// not expanded on the list response.
    /// </summary>
    private bool MatchesLaunchPrice(JsonElement session)
    {
        if (session.TryGetProperty("line_items", out JsonElement lineItems) &&
            lineItems.ValueKind == JsonValueKind.Object &&
            lineItems.TryGetProperty("data", out JsonElement items) &&
            items.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in items.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object &&
                    item.TryGetProperty("price", out JsonElement price) &&
                    price.ValueKind == JsonValueKind.Object &&
                    (StringPropertyEquals(price, "id", _options.ExpectedStripePriceId) ||
                     StringPropertyEquals(price, "lookup_key", _options.ExpectedStripePriceLookupKey)))
                {
                    return true;
                }
            }
        }

        // Fallback: amount + currency when line items are absent/unexpanded.
        bool amountMatches = session.TryGetProperty("amount_total", out JsonElement amount) &&
                             amount.ValueKind == JsonValueKind.Number &&
                             amount.TryGetInt64(out long amountValue) &&
                             amountValue == _options.ExpectedAmountCents;
        bool currencyMatches = session.TryGetProperty("currency", out JsonElement currency) &&
                               currency.ValueKind == JsonValueKind.String &&
                               string.Equals(
                                   currency.GetString(), _options.ExpectedCurrency, StringComparison.OrdinalIgnoreCase);
        return amountMatches && currencyMatches;
    }

    private static bool StringPropertyEquals(JsonElement element, string property, string expected)
        => !string.IsNullOrWhiteSpace(expected) &&
           element.TryGetProperty(property, out JsonElement value) &&
           value.ValueKind == JsonValueKind.String &&
           string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static Uri BuildPageUri(long sinceUnix, string? startingAfter)
    {
        // Expand line item prices so the exact launch price can be matched; also request
        // paid sessions created at/after the window start.
        string query =
            $"/v1/checkout/sessions?limit={PageSize.ToString(CultureInfo.InvariantCulture)}" +
            $"&created[gte]={sinceUnix.ToString(CultureInfo.InvariantCulture)}" +
            "&expand[]=data.line_items";
        if (!string.IsNullOrEmpty(startingAfter))
        {
            query += $"&starting_after={Uri.EscapeDataString(startingAfter)}";
        }

        return new Uri(query, UriKind.Relative);
    }

    private static HttpClient BuildClient(LicenseServiceOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        return new HttpClient(handler, disposeHandler: false)
        {
            BaseAddress = new Uri(options.StripeApiBaseUrl, UriKind.Absolute),
        };
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
