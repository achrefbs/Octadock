using FluentAssertions;
using Microsoft.Data.Sqlite;
using Octadock.Data.Tests.Infrastructure;

namespace Octadock.Data.Tests;

public sealed class DatabaseIsolationTests
{
    [Fact]
    public async Task Disposing_database_preserves_unrelated_pooled_connection_state()
    {
        await using TestDatabase active = await TestDatabase.CreateAsync();
        await using (TestDatabase retiring = await TestDatabase.CreateAsync())
        {
            await using SqliteConnection connection = active.OpenConnection();
            await using SqliteCommand command = connection.CreateCommand();
            // TEMP tables belong to the native connection, so a process-wide
            // pool flush loses this marker even though its database is still in use.
            command.CommandText = "CREATE TEMP TABLE isolation_marker (value INTEGER); INSERT INTO isolation_marker VALUES (42);";
            await command.ExecuteNonQueryAsync();
        }

        await using SqliteConnection reopened = active.OpenConnection();
        await using SqliteCommand read = reopened.CreateCommand();
        read.CommandText = "SELECT value FROM isolation_marker;";
        (await read.ExecuteScalarAsync()).Should().Be(42L);
    }
}
