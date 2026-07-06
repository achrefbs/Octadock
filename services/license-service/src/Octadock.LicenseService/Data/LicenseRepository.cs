using System.Globalization;
using Microsoft.Data.Sqlite;
using Octadock.LicenseService.Licensing;

namespace Octadock.LicenseService.Data;

/// <summary>A pending license issuance derived from a verified Stripe event.</summary>
public sealed record LicenseIssuance(
    string Product,
    string? PurchaseEmail,
    string? StripeCustomerId,
    string StripeCheckoutSessionId,
    string? StripePaymentIntentId,
    long? AmountCents,
    string? Currency,
    int DeviceLimit,
    DateTimeOffset? UpdatesUntil);

/// <summary>Result of an issuance: the key, whether it was newly created, and its row id.</summary>
public sealed record LicenseIssuanceResult(string LicenseKey, bool WasNewlyIssued, long LicenseId);

/// <summary>
/// Launch-health counters for the admin surface (WS6). Every number is either from
/// a LIVE source or explicitly <c>null</c> when the source is founder-gated (not
/// built yet) — we do not fabricate. Reconciliation and email fields are filled in
/// by the caller (<see cref="LaunchHealthSnapshot.With"/>) because they live outside
/// the DB.
/// </summary>
public sealed record LaunchHealthSnapshot(
    long LicensesTotal,
    long LicensesActive,
    long LicensesRevoked,
    long LicensesIssuedLast24h,
    long WebhookEventsTotal,
    DateTimeOffset? MostRecentWebhookReceivedAt,
    long ActivationsSucceeded,
    long ActivationsFailed,
    long IssuedNotActivated)
{
    /// <summary>Activation success rate over the sampled window, or null when there were no attempts.</summary>
    public double? ActivationSuccessRate
    {
        get
        {
            long attempts = ActivationsSucceeded + ActivationsFailed;
            return attempts == 0 ? null : (double)ActivationsSucceeded / attempts;
        }
    }

    /// <summary>Total activation attempts (success + failure) in the sampled window.</summary>
    public long ActivationAttempts => ActivationsSucceeded + ActivationsFailed;

    /// <summary>
    /// Percentage of active licenses that have never been activated on any device.
    /// Null when there are no active licenses (nothing to divide by).
    /// </summary>
    public double? IssuedNotActivatedRate
    {
        get
        {
            long activeLicensesForDenominator = LicensesActive;
            return activeLicensesForDenominator == 0
                ? null
                : (double)IssuedNotActivated / activeLicensesForDenominator;
        }
    }

    // ---- Reconciliation (source: ReconciliationService, not the DB) ----

    /// <summary>Paid-but-no-key session count from the last reconciliation pass.</summary>
    public int ReconciliationDiff { get; init; }

    /// <summary>Whether a real (founder-gated) Stripe paid-session source is wired.</summary>
    public bool PaidSessionSourceConfigured { get; init; }

    // ---- Email / resend (NOT BUILT — founder-gated; always "no data by design") ----

    /// <summary>Emails delivered. Always null: the email pipeline is founder-gated (not built).</summary>
    public long? EmailsDelivered { get; init; }

    /// <summary>Emails bounced. Always null: the email pipeline is founder-gated (not built).</summary>
    public long? EmailsBounced { get; init; }

    /// <summary>License-key resend count. Always null: the resend endpoint is founder-gated (not built).</summary>
    public long? ResendCount { get; init; }

    /// <summary>Returns a copy with the out-of-DB reconciliation numbers filled in.</summary>
    public LaunchHealthSnapshot WithReconciliation(int diff, bool sourceConfigured)
        => this with { ReconciliationDiff = diff, PaidSessionSourceConfigured = sourceConfigured };
}

/// <summary>Whether a webhook event should be processed, retried, or skipped.</summary>
public enum WebhookEventBeginStatus
{
    Started,
    RetryUnprocessed,
    AlreadyProcessed,
    PayloadMismatch,
}

/// <summary>The fields an activation / admin lookup needs about a license.</summary>
public sealed record LicenseLookup(
    long Id,
    string LicenseKey,
    string Status,
    string Product,
    string? PurchaseEmail,
    int DeviceLimit,
    DateTimeOffset? UpdatesUntil,
    int Seats);

/// <summary>What happened on an activation attempt.</summary>
public enum ActivationOutcome
{
    Activated,
    AlreadyActive,
    DeviceLimitReached,
    LicenseNotFound,
    LicenseNotActive,
}

/// <summary>Result of an activation attempt.</summary>
public sealed record ActivationResult(ActivationOutcome Outcome, LicenseLookup? License, int ActiveDeviceCount);

/// <summary>
/// Data access for the license service. Enforces the "exactly one entitlement per
/// verified paid event" invariant (WS3/WS4): webhook events are deduped by id, and
/// issuance is idempotent on the Stripe Checkout session id even under concurrent
/// duplicate delivery. Every entitlement-changing action writes an audit row.
/// </summary>
public sealed class LicenseRepository
{
    private readonly LicenseDatabase _database;

    public LicenseRepository(LicenseDatabase database) => _database = database;

    /// <summary>
    /// Records that a webhook event id has been seen. Returns <c>true</c> only the
    /// FIRST time — a duplicate/replayed event returns <c>false</c> so its side
    /// effects run at most once (WS3, R12).
    /// </summary>
    public bool TryBeginWebhookEvent(string eventId, string type, string payloadHash, DateTimeOffset now)
        => BeginWebhookEvent(eventId, type, payloadHash, now) == WebhookEventBeginStatus.Started;

    /// <summary>
    /// Records or inspects webhook event state. A duplicate event is skipped only
    /// after <c>processed_at</c> is set; an unprocessed duplicate is retryable, so
    /// Stripe can recover from a process crash after receipt but before issuance.
    /// </summary>
    public WebhookEventBeginStatus BeginWebhookEvent(
        string eventId, string type, string payloadHash, DateTimeOffset now)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
            INSERT OR IGNORE INTO webhook_events (event_id, type, payload_hash, received_at)
            VALUES ($id, $type, $hash, $now);
            """;
            Bind(command, "$id", eventId);
            Bind(command, "$type", type);
            Bind(command, "$hash", payloadHash);
            Bind(command, "$now", Iso(now));
            if (command.ExecuteNonQuery() == 1)
            {
                return WebhookEventBeginStatus.Started;
            }
        }

        using SqliteCommand inspect = connection.CreateCommand();
        inspect.CommandText = """
            SELECT payload_hash, processed_at FROM webhook_events WHERE event_id = $id;
            """;
        Bind(inspect, "$id", eventId);
        using SqliteDataReader reader = inspect.ExecuteReader();
        if (!reader.Read())
        {
            return WebhookEventBeginStatus.RetryUnprocessed;
        }

        string existingHash = reader.GetString(0);
        if (!string.Equals(existingHash, payloadHash, StringComparison.OrdinalIgnoreCase))
        {
            return WebhookEventBeginStatus.PayloadMismatch;
        }

        return reader.IsDBNull(1)
            ? WebhookEventBeginStatus.RetryUnprocessed
            : WebhookEventBeginStatus.AlreadyProcessed;
    }

    /// <summary>Marks a webhook event fully processed (for observability/reconciliation).</summary>
    public void MarkWebhookProcessed(string eventId, DateTimeOffset now)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE webhook_events SET processed_at = $now WHERE event_id = $id;";
        Bind(command, "$now", Iso(now));
        Bind(command, "$id", eventId);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Issues exactly one license for a Checkout session. Idempotent: a repeat call
    /// for the same session returns the already-issued key rather than minting a
    /// second license, even if two duplicate events race.
    /// </summary>
    public LicenseIssuanceResult IssueLicense(LicenseIssuance issuance, DateTimeOffset now)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        LicenseIssuanceResult? existing = FindBySession(connection, transaction, issuance.StripeCheckoutSessionId);
        if (existing is not null)
        {
            transaction.Commit();
            return existing;
        }

        string key = LicenseKeyGenerator.New();
        try
        {
            long id = InsertLicense(connection, transaction, key, issuance, now);
            InsertIdentityLink(connection, transaction, id, issuance, now);
            InsertAudit(connection, transaction, id, "license.issued", "webhook",
                $"session={issuance.StripeCheckoutSessionId}", now);
            transaction.Commit();
            return new LicenseIssuanceResult(key, WasNewlyIssued: true, id);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT (unique)
        {
            // A concurrent duplicate won the race; adopt its already-issued row.
            transaction.Rollback();
            using SqliteConnection retry = _database.OpenConnection();
            LicenseIssuanceResult? raced = FindBySession(retry, transaction: null, issuance.StripeCheckoutSessionId);
            if (raced is not null)
            {
                return raced;
            }

            throw;
        }
    }

    /// <summary>
    /// Revokes every license tied to a Stripe payment intent (refund / dispute →
    /// revoke, WS3, R21). Returns the number of licenses affected.
    /// </summary>
    public int RevokeByPaymentIntent(
        string paymentIntentId, string status, string reason, string actor, DateTimeOffset now)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        var affectedIds = new List<long>();
        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                "SELECT id FROM licenses WHERE stripe_payment_intent_id = $pi AND status != $status;";
            Bind(select, "$pi", paymentIntentId);
            Bind(select, "$status", status);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                affectedIds.Add(reader.GetInt64(0));
            }
        }

        foreach (long id in affectedIds)
        {
            using (SqliteCommand update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = """
                    UPDATE licenses
                       SET status = $status, revoked_at = $now, revoked_reason = $reason, updated_at = $now
                     WHERE id = $id;
                    """;
                Bind(update, "$status", status);
                Bind(update, "$now", Iso(now));
                Bind(update, "$reason", reason);
                Bind(update, "$id", id);
                update.ExecuteNonQuery();
            }

            InsertAudit(connection, transaction, id, $"license.{status}", actor, reason, now);
        }

        transaction.Commit();
        return affectedIds.Count;
    }

    /// <summary>
    /// Reconciliation (WS3, R3): given the set of PAID Checkout session ids known to
    /// Stripe, returns those with no license in the database — i.e. paid-but-no-key.
    /// </summary>
    public IReadOnlyList<string> FindPaidSessionsWithoutLicense(IEnumerable<string> paidSessionIds)
    {
        using SqliteConnection connection = _database.OpenConnection();
        var missing = new List<string>();
        foreach (string sessionId in paidSessionIds)
        {
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT COUNT(1) FROM licenses WHERE stripe_checkout_session_id = $sid;";
            Bind(command, "$sid", sessionId);
            long count = Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (count == 0)
            {
                missing.Add(sessionId);
            }
        }

        return missing;
    }

    /// <summary>
    /// Launch-health counters (WS6). Each number is computed from a LIVE source:
    /// licenses table, webhook_events table, and the audit_log (which records both
    /// <c>device.activated</c> successes and <c>device.activation_failed</c> failures,
    /// so the activation success rate is real, not fabricated). The activation window
    /// samples the most recent <paramref name="activationSampleSize"/> attempts. The
    /// out-of-DB reconciliation and (founder-gated) email numbers are filled in by the
    /// caller via <see cref="LaunchHealthSnapshot.WithReconciliation"/>.
    /// </summary>
    public LaunchHealthSnapshot GetLaunchHealth()
        => GetLaunchHealth(DateTimeOffset.UtcNow);

    /// <inheritdoc cref="GetLaunchHealth()"/>
    public LaunchHealthSnapshot GetLaunchHealth(DateTimeOffset now, int activationSampleSize = 20)
    {
        using SqliteConnection connection = _database.OpenConnection();
        long total = ScalarCount(connection, "SELECT COUNT(1) FROM licenses;");
        long active = ScalarCount(connection, "SELECT COUNT(1) FROM licenses WHERE status = 'active';");
        long revoked = ScalarCount(connection, "SELECT COUNT(1) FROM licenses WHERE status != 'active';");
        long events = ScalarCount(connection, "SELECT COUNT(1) FROM webhook_events;");

        string dayAgo = Iso(now - TimeSpan.FromHours(24));
        long issued24h = ScalarCount(
            connection, "SELECT COUNT(1) FROM licenses WHERE created_at >= $since;", ("$since", dayAgo));

        DateTimeOffset? lastWebhook = ScalarDateTime(
            connection, "SELECT MAX(received_at) FROM webhook_events;");

        // Activation success rate over the last N attempts (success + failure audit
        // rows), newest first. Both branches are LIVE — failures are audited in Activate.
        (long succeeded, long failed) = ActivationOutcomeCounts(connection, activationSampleSize);

        // Issued-not-activated: active licenses with zero non-deactivated activations.
        long issuedNotActivated = ScalarCount(connection, """
            SELECT COUNT(1) FROM licenses l
             WHERE l.status = 'active'
               AND NOT EXISTS (
                   SELECT 1 FROM activations a
                    WHERE a.license_id = l.id AND a.deactivated_at IS NULL);
            """);

        return new LaunchHealthSnapshot(
            LicensesTotal: total,
            LicensesActive: active,
            LicensesRevoked: revoked,
            LicensesIssuedLast24h: issued24h,
            WebhookEventsTotal: events,
            MostRecentWebhookReceivedAt: lastWebhook,
            ActivationsSucceeded: succeeded,
            ActivationsFailed: failed,
            IssuedNotActivated: issuedNotActivated);
    }

    private static (long Succeeded, long Failed) ActivationOutcomeCounts(
        SqliteConnection connection, int sampleSize)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT action FROM audit_log
             WHERE action IN ('device.activated', 'device.activation_failed')
             ORDER BY id DESC
             LIMIT $limit;
            """;
        Bind(command, "$limit", sampleSize);
        long succeeded = 0;
        long failed = 0;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetString(0) == "device.activated")
            {
                succeeded++;
            }
            else
            {
                failed++;
            }
        }

        return (succeeded, failed);
    }

    /// <summary>Looks up a license by its key (activation / admin).</summary>
    public LicenseLookup? FindByKey(string licenseKey)
    {
        using SqliteConnection connection = _database.OpenConnection();
        return ReadLookup(connection, transaction: null, licenseKey);
    }

    /// <summary>
    /// Activates a license on a device (WS4, R13). Idempotent for an already-active
    /// device; enforces the device limit; refuses inactive/revoked licenses. Every
    /// new activation writes an audit row.
    /// </summary>
    public ActivationResult Activate(string licenseKey, string machineHash, int deviceHashVersion, DateTimeOffset now)
    {
        using SqliteConnection connection = _database.OpenConnection();
        using SqliteTransaction transaction = connection.BeginTransaction();

        LicenseLookup? license = ReadLookup(connection, transaction, licenseKey);
        if (license is null)
        {
            // Record the failure so the launch-health activation success rate is a
            // LIVE number (WS6). No license_id to attach — this is a bad-key attempt.
            InsertAudit(connection, transaction, null, "device.activation_failed", "activate",
                "reason=LicenseNotFound", now);
            transaction.Commit();
            return new ActivationResult(ActivationOutcome.LicenseNotFound, null, 0);
        }

        if (!string.Equals(license.Status, "active", StringComparison.OrdinalIgnoreCase))
        {
            InsertAudit(connection, transaction, license.Id, "device.activation_failed", "activate",
                "reason=LicenseNotActive", now);
            transaction.Commit();
            return new ActivationResult(ActivationOutcome.LicenseNotActive, license, 0);
        }

        long? existingActivationId = null;
        int activeCount = 0;
        using (SqliteCommand select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText =
                "SELECT id, machine_hash FROM activations WHERE license_id = $lid AND deactivated_at IS NULL;";
            Bind(select, "$lid", license.Id);
            using SqliteDataReader reader = select.ExecuteReader();
            while (reader.Read())
            {
                activeCount++;
                if (string.Equals(reader.GetString(1), machineHash, StringComparison.OrdinalIgnoreCase))
                {
                    existingActivationId = reader.GetInt64(0);
                }
            }
        }

        if (existingActivationId is not null)
        {
            using SqliteCommand touch = connection.CreateCommand();
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE activations SET last_seen_at = $now WHERE id = $id;";
            Bind(touch, "$now", Iso(now));
            Bind(touch, "$id", existingActivationId.Value);
            touch.ExecuteNonQuery();
            transaction.Commit();
            return new ActivationResult(ActivationOutcome.AlreadyActive, license, activeCount);
        }

        if (activeCount >= license.DeviceLimit)
        {
            InsertAudit(connection, transaction, license.Id, "device.activation_failed", "activate",
                $"reason=DeviceLimitReached devices={activeCount}", now);
            transaction.Commit();
            return new ActivationResult(ActivationOutcome.DeviceLimitReached, license, activeCount);
        }

        using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO activations
                    (license_id, machine_hash, device_hash_v, seat_index, first_activated_at, last_seen_at)
                VALUES ($lid, $hash, $v, $seat, $now, $now);
                """;
            Bind(insert, "$lid", license.Id);
            Bind(insert, "$hash", machineHash);
            Bind(insert, "$v", deviceHashVersion);
            Bind(insert, "$seat", activeCount); // dormant multi-seat index
            Bind(insert, "$now", Iso(now));
            insert.ExecuteNonQuery();
        }

        InsertAudit(connection, transaction, license.Id, "device.activated", "activate",
            $"machine={machineHash[..Math.Min(8, machineHash.Length)]}…", now);
        transaction.Commit();
        return new ActivationResult(ActivationOutcome.Activated, license, activeCount + 1);
    }

    private static LicenseLookup? ReadLookup(
        SqliteConnection connection, SqliteTransaction? transaction, string licenseKey)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT id, license_key, status, product, purchase_email, device_limit, updates_until, seats
              FROM licenses WHERE license_key = $key;
            """;
        Bind(command, "$key", licenseKey);
        using SqliteDataReader reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new LicenseLookup(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4),
            reader.GetInt32(5),
            reader.IsDBNull(6) ? null : DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
            reader.GetInt32(7));
    }

    private static LicenseIssuanceResult? FindBySession(
        SqliteConnection connection, SqliteTransaction? transaction, string sessionId)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id, license_key FROM licenses WHERE stripe_checkout_session_id = $sid;";
        Bind(command, "$sid", sessionId);
        using SqliteDataReader reader = command.ExecuteReader();
        return reader.Read()
            ? new LicenseIssuanceResult(reader.GetString(1), WasNewlyIssued: false, reader.GetInt64(0))
            : null;
    }

    private static long InsertLicense(
        SqliteConnection connection, SqliteTransaction transaction,
        string key, LicenseIssuance issuance, DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO licenses (
                license_key, status, product, purchase_email, stripe_customer_id,
                stripe_checkout_session_id, stripe_payment_intent_id, amount_cents, currency,
                device_limit, updates_until, includes_1_0, created_at, updated_at)
            VALUES (
                $key, 'active', $product, $email, $customer,
                $session, $pi, $amount, $currency,
                $deviceLimit, $updatesUntil, 1, $now, $now)
            RETURNING id;
            """;
        Bind(command, "$key", key);
        Bind(command, "$product", issuance.Product);
        Bind(command, "$email", issuance.PurchaseEmail);
        Bind(command, "$customer", issuance.StripeCustomerId);
        Bind(command, "$session", issuance.StripeCheckoutSessionId);
        Bind(command, "$pi", issuance.StripePaymentIntentId);
        Bind(command, "$amount", issuance.AmountCents);
        Bind(command, "$currency", issuance.Currency);
        Bind(command, "$deviceLimit", issuance.DeviceLimit);
        Bind(command, "$updatesUntil", issuance.UpdatesUntil.HasValue ? Iso(issuance.UpdatesUntil.Value) : null);
        Bind(command, "$now", Iso(now));
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void InsertIdentityLink(
        SqliteConnection connection, SqliteTransaction transaction,
        long licenseId, LicenseIssuance issuance, DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO identity_links (license_id, stripe_customer_id, email, created_at)
            VALUES ($id, $customer, $email, $now);
            """;
        Bind(command, "$id", licenseId);
        Bind(command, "$customer", issuance.StripeCustomerId);
        Bind(command, "$email", issuance.PurchaseEmail);
        Bind(command, "$now", Iso(now));
        command.ExecuteNonQuery();
    }

    private static void InsertAudit(
        SqliteConnection connection, SqliteTransaction transaction,
        long? licenseId, string action, string actor, string? detail, DateTimeOffset now)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO audit_log (license_id, action, actor, detail, created_at)
            VALUES ($id, $action, $actor, $detail, $now);
            """;
        Bind(command, "$id", licenseId);
        Bind(command, "$action", action);
        Bind(command, "$actor", actor);
        Bind(command, "$detail", detail);
        Bind(command, "$now", Iso(now));
        command.ExecuteNonQuery();
    }

    private static long ScalarCount(
        SqliteConnection connection, string sql, params (string Name, object? Value)[] parameters)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        foreach ((string name, object? value) in parameters)
        {
            Bind(command, name, value);
        }

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset? ScalarDateTime(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value is null or DBNull
            ? null
            : DateTimeOffset.Parse((string)value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }

    private static void Bind(SqliteCommand command, string name, object? value)
        => command.Parameters.AddWithValue(name, value ?? DBNull.Value);

    private static string Iso(DateTimeOffset value)
        => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
}
