using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Octadock.LicenseService.Data;

namespace Octadock.LicenseService.Tests;

/// <summary>A controllable clock for deterministic signature/expiry tests.</summary>
internal sealed class FixedTimeProvider : TimeProvider
{
    private DateTimeOffset _now;

    public FixedTimeProvider(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>Builds a valid <c>Stripe-Signature</c> header exactly as Stripe does.</summary>
internal static class StripeSignatureFactory
{
    public static string Make(string body, string secret, long timestamp)
    {
        byte[] hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{body}"));
        string hex = Convert.ToHexString(hash).ToLowerInvariant();
        return $"t={timestamp},v1={hex}";
    }
}

/// <summary>A throwaway file-backed license database that cleans up after itself.</summary>
internal sealed class TempLicenseDatabase : IDisposable
{
    private readonly string _path;

    public TempLicenseDatabase()
    {
        _path = Path.Combine(Path.GetTempPath(), $"octadock-lic-{Guid.NewGuid():N}.db");
        Database = new LicenseDatabase($"Data Source={_path}");
        Database.Migrate();
        Repository = new LicenseRepository(Database);
    }

    public LicenseDatabase Database { get; }

    public LicenseRepository Repository { get; }

    public string ConnectionString => $"Data Source={_path}";

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { _path, _path + "-wal", _path + "-shm" })
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best effort in tests.
            }
        }
    }
}

/// <summary>Canonical Stripe event bodies used by the webhook tests.</summary>
internal static class Events
{
    public const string ExpectedPriceId = "price_1TqH3lKNDvjYLyJhld4yYPIZ";
    public const string ExpectedLookupKey = "octadock_local_beta_usd_49";

    public static string CheckoutCompleted(
        string eventId,
        string sessionId,
        string paymentIntent,
        string email = "buyer@example.com",
        string paymentStatus = "paid",
        long amountTotal = 4900,
        string currency = "usd",
        string? priceId = ExpectedPriceId,
        string? priceLookupKey = ExpectedLookupKey,
        bool includeMetadata = true,
        bool includeExpandedLineItems = false)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "checkout.session.completed",
            data = new
            {
                @object = new
                {
                    id = sessionId,
                    payment_status = paymentStatus,
                    customer = "cus_test",
                    customer_email = email,
                    payment_intent = paymentIntent,
                    amount_total = amountTotal,
                    currency,
                    metadata = includeMetadata
                        ? new
                        {
                            stripe_price_id = priceId,
                            price_key = priceLookupKey,
                        }
                        : null,
                    line_items = includeExpandedLineItems
                        ? new
                        {
                            data = new[]
                            {
                                new
                                {
                                    price = new
                                    {
                                        id = priceId,
                                        lookup_key = priceLookupKey,
                                    },
                                },
                            },
                        }
                        : null,
                },
            },
        });

    public static string PayloadHash(string body)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

    public static string ChargeRefunded(string eventId, string paymentIntent)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "charge.refunded",
            data = new { @object = new { id = "ch_test", payment_intent = paymentIntent, refunded = true } },
        });

    public static string DisputeCreated(string eventId, string paymentIntent)
        => JsonSerializer.Serialize(new
        {
            id = eventId,
            type = "charge.dispute.created",
            data = new { @object = new { id = "dp_test", payment_intent = paymentIntent } },
        });
}
