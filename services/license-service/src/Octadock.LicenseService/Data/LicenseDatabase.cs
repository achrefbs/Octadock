using Microsoft.Data.Sqlite;

namespace Octadock.LicenseService.Data;

/// <summary>
/// Owns the license-service SQLite database: connection creation, PRAGMA setup,
/// and forward-only schema migrations keyed on <c>PRAGMA user_version</c>. The
/// initial migration (v1) ships the full commercial schema INCLUDING the dormant
/// Pro/Team fields, the key↔customer↔email linking table, and the empty
/// append-only <c>usage_events</c> ledger, so Pro launch never forces a live
/// migration (WS13, R35). A downgrade guard refuses to open a database written by
/// a newer binary.
/// </summary>
public sealed class LicenseDatabase
{
    /// <summary>Highest schema version this binary understands.</summary>
    public const long LatestVersion = 1;

    private readonly string _connectionString;

    public LicenseDatabase(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    /// <summary>Opens a new connection with foreign keys enabled.</summary>
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;";
            pragma.ExecuteNonQuery();
        }

        return connection;
    }

    /// <summary>
    /// Applies any pending migrations. Throws if the database was written by a
    /// newer binary (<c>user_version</c> &gt; <see cref="LatestVersion"/>) rather
    /// than silently corrupting a future schema.
    /// </summary>
    public void Migrate()
    {
        using SqliteConnection connection = OpenConnection();
        using (SqliteCommand walCmd = connection.CreateCommand())
        {
            walCmd.CommandText = "PRAGMA journal_mode=WAL;";
            walCmd.ExecuteNonQuery();
        }

        long version = ReadUserVersion(connection);
        if (version > LatestVersion)
        {
            throw new InvalidOperationException(
                $"License database schema is v{version}, newer than this binary supports (v{LatestVersion}). " +
                "Deploy the matching or newer service build; refusing to run against a future schema.");
        }

        if (version < 1)
        {
            ApplyMigration(connection, MigrationV1, targetVersion: 1);
        }
    }

    private static long ReadUserVersion(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ApplyMigration(SqliteConnection connection, string sql, long targetVersion)
    {
        using SqliteTransaction transaction = connection.BeginTransaction();
        using (SqliteCommand command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        using (SqliteCommand setVersion = connection.CreateCommand())
        {
            setVersion.Transaction = transaction;
            // PRAGMA user_version does not accept parameters; targetVersion is an
            // internal constant, never user input.
            setVersion.CommandText = $"PRAGMA user_version = {targetVersion};";
            setVersion.ExecuteNonQuery();
        }

        transaction.Commit();
    }

    // NOTE: This is the FROZEN initial schema. Never edit v1 in place once a real
    // database exists — add a v2 migration instead. Dormant columns are shipped
    // now precisely so adding Pro/Team later needs no destructive migration.
    private const string MigrationV1 = """
        -- Webhook event ledger: idempotency + replay safety (WS3, R12).
        CREATE TABLE webhook_events (
            event_id     TEXT PRIMARY KEY,
            type         TEXT NOT NULL,
            payload_hash TEXT NOT NULL,
            received_at  TEXT NOT NULL,
            processed_at TEXT
        );

        -- One row per issued license (WS4).
        CREATE TABLE licenses (
            id                         INTEGER PRIMARY KEY AUTOINCREMENT,
            license_key                TEXT NOT NULL UNIQUE,
            status                     TEXT NOT NULL DEFAULT 'active',   -- active | revoked | refunded
            product                    TEXT NOT NULL,
            purchase_email             TEXT,
            stripe_customer_id         TEXT,
            stripe_checkout_session_id TEXT UNIQUE,                      -- business-level idempotency
            stripe_payment_intent_id   TEXT,
            amount_cents               INTEGER,
            currency                   TEXT,
            device_limit               INTEGER NOT NULL DEFAULT 3,
            updates_until              TEXT,
            includes_1_0               INTEGER NOT NULL DEFAULT 1,
            created_at                 TEXT NOT NULL,
            updated_at                 TEXT NOT NULL,
            revoked_at                 TEXT,
            revoked_reason             TEXT,
            -- Dormant Pro/Team fields (WS13, R35) — shipped now to avoid a live migration.
            seats                      INTEGER NOT NULL DEFAULT 1,
            owner_email                TEXT,
            org_id                     TEXT,
            policy_json                TEXT,
            subscription_state         TEXT
        );

        CREATE INDEX ix_licenses_email     ON licenses(purchase_email);
        CREATE INDEX ix_licenses_customer  ON licenses(stripe_customer_id);
        CREATE INDEX ix_licenses_pi        ON licenses(stripe_payment_intent_id);

        -- Device registry: the "3 devices" policy (WS4, R13). seat_index is dormant
        -- multi-seat support; device_hash_v versions the machine-hash algorithm.
        CREATE TABLE activations (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            license_id         INTEGER NOT NULL REFERENCES licenses(id) ON DELETE CASCADE,
            machine_hash       TEXT NOT NULL,
            device_hash_v      INTEGER NOT NULL DEFAULT 1,
            seat_index         INTEGER NOT NULL DEFAULT 0,
            first_activated_at TEXT NOT NULL,
            last_seen_at       TEXT NOT NULL,
            deactivated_at     TEXT,
            UNIQUE(license_id, machine_hash)
        );

        -- key ↔ customer ↔ email linking (WS13): lets a future first-party account
        -- adopt existing keys without re-issuing them.
        CREATE TABLE identity_links (
            id                 INTEGER PRIMARY KEY AUTOINCREMENT,
            license_id         INTEGER NOT NULL REFERENCES licenses(id) ON DELETE CASCADE,
            stripe_customer_id TEXT,
            email              TEXT,
            created_at         TEXT NOT NULL
        );

        CREATE INDEX ix_identity_links_email    ON identity_links(email);
        CREATE INDEX ix_identity_links_customer ON identity_links(stripe_customer_id);

        -- Append-only metering ledger (WS13) — shipped EMPTY and dormant.
        CREATE TABLE usage_events (
            id          INTEGER PRIMARY KEY AUTOINCREMENT,
            license_id  INTEGER NOT NULL REFERENCES licenses(id) ON DELETE CASCADE,
            meter       TEXT NOT NULL,
            scope       TEXT,
            qty         INTEGER NOT NULL,
            device_hash TEXT,
            request_id  TEXT,
            created_at  TEXT NOT NULL
        );

        -- Append-only audit of every entitlement-changing action (WS6).
        CREATE TABLE audit_log (
            id         INTEGER PRIMARY KEY AUTOINCREMENT,
            license_id INTEGER,
            action     TEXT NOT NULL,
            actor      TEXT NOT NULL,
            detail     TEXT,
            created_at TEXT NOT NULL
        );
        """;
}
