using FluentAssertions;
using Microsoft.Data.Sqlite;
using Octadock.LicenseService.Data;
using Xunit;

namespace Octadock.LicenseService.Tests;

public class LicenseDatabaseTests
{
    [Fact]
    public void Migrate_creates_the_frozen_schema_including_dormant_columns()
    {
        using var db = new TempLicenseDatabase();
        using SqliteConnection connection = db.Database.OpenConnection();

        HashSet<string> tables = ReadStrings(connection,
            "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;");
        tables.Should().Contain(new[]
        {
            "webhook_events", "licenses", "activations", "identity_links", "usage_events", "audit_log",
        });

        HashSet<string> licenseColumns = ReadStrings(connection, "PRAGMA table_info(licenses);", ordinal: 1);
        licenseColumns.Should().Contain(new[] { "seats", "owner_email", "org_id", "policy_json", "subscription_state" },
            "dormant Pro/Team fields must ship in the initial migration (WS13, R35)");

        HashSet<string> activationColumns = ReadStrings(connection, "PRAGMA table_info(activations);", ordinal: 1);
        activationColumns.Should().Contain(new[] { "machine_hash", "device_hash_v", "seat_index" });

        // usage_events ships empty.
        ScalarLong(connection, "SELECT COUNT(1) FROM usage_events;").Should().Be(0);
    }

    [Fact]
    public void Migrate_refuses_a_future_schema_version()
    {
        using var db = new TempLicenseDatabase();
        using (SqliteConnection connection = db.Database.OpenConnection())
        using (SqliteCommand bump = connection.CreateCommand())
        {
            bump.CommandText = $"PRAGMA user_version = {LicenseDatabase.LatestVersion + 5};";
            bump.ExecuteNonQuery();
        }

        Action reopen = () => db.Database.Migrate();

        reopen.Should().Throw<InvalidOperationException>()
            .WithMessage("*newer than this binary*", "a downgrade guard must refuse a future schema");
    }

    private static HashSet<string> ReadStrings(SqliteConnection connection, string sql, int ordinal = 0)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new HashSet<string>(StringComparer.Ordinal);
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            values.Add(reader.GetString(ordinal));
        }

        return values;
    }

    private static long ScalarLong(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
