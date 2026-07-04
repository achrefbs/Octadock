using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Octadock.Core.Abstractions;

namespace Octadock.Data.Sqlite;

/// <summary>
/// Opens configured <see cref="SqliteConnection"/> instances against the Octadock
/// database. Each opened connection has WAL journalling, foreign keys and a busy
/// timeout applied so the pragmas are consistent regardless of who opens it.
/// </summary>
public interface ISqliteConnectionFactory
{
    /// <summary>The connection string all connections are opened with.</summary>
    string ConnectionString { get; }

    /// <summary>Opens and configures a new connection synchronously.</summary>
    SqliteConnection OpenConnection();

    /// <summary>Opens and configures a new connection.</summary>
    Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ISqliteConnectionFactory"/>. Resolves the database path from
/// <see cref="IStoragePaths.DatabasePath"/> in production, but also accepts an
/// explicit connection string so tests can target a temp file or a shared-cache
/// in-memory database.
/// </summary>
public sealed partial class SqliteConnectionFactory : ISqliteConnectionFactory
{
    /// <summary>Default SQLite busy timeout applied to every connection.</summary>
    public const int DefaultBusyTimeoutMilliseconds = 5_000;

    private readonly ILogger<SqliteConnectionFactory> _logger;
    private readonly int _busyTimeoutMilliseconds;

    /// <summary>Creates a factory targeting the database described by <paramref name="storagePaths"/>.</summary>
    public SqliteConnectionFactory(IStoragePaths storagePaths, ILogger<SqliteConnectionFactory> logger)
        : this(BuildConnectionString(storagePaths), logger)
    {
    }

    /// <summary>Creates a factory targeting an explicit connection string (used by tests).</summary>
    public SqliteConnectionFactory(
        string connectionString,
        ILogger<SqliteConnectionFactory> logger,
        int busyTimeoutMilliseconds = DefaultBusyTimeoutMilliseconds)
    {
        ConnectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("Connection string must not be null or whitespace.", nameof(connectionString))
            : connectionString;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _busyTimeoutMilliseconds = busyTimeoutMilliseconds < 0
            ? throw new ArgumentOutOfRangeException(nameof(busyTimeoutMilliseconds), busyTimeoutMilliseconds, "Busy timeout must not be negative.")
            : busyTimeoutMilliseconds;
    }

    /// <inheritdoc />
    public string ConnectionString { get; }

    /// <summary>Builds a file-backed connection string for the given database path.</summary>
    public static string BuildConnectionString(IStoragePaths storagePaths)
    {
        ArgumentNullException.ThrowIfNull(storagePaths);
        return BuildFileConnectionString(storagePaths.DatabasePath);
    }

    /// <summary>Builds a file-backed connection string for an explicit database file path.</summary>
    public static string BuildFileConnectionString(string databasePath)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException("Database path must not be null or whitespace.", nameof(databasePath));
        }

        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
        }.ToString();
    }

    /// <inheritdoc />
    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            connection.Open();
            ApplyPragmas(connection);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void ApplyPragmas(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaScript();
        command.ExecuteNonQuery();
        LogAppliedSqlitePragmas(_busyTimeoutMilliseconds);
    }

    private async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BuildPragmaScript();
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        LogAppliedSqlitePragmas(_busyTimeoutMilliseconds);
    }

    private string BuildPragmaScript() =>
        $"""
        PRAGMA journal_mode=WAL;
        PRAGMA foreign_keys=ON;
        PRAGMA busy_timeout={_busyTimeoutMilliseconds};
        """;

    [LoggerMessage(EventId = 1, Level = LogLevel.Debug,
        Message = "Applied SQLite pragmas (busy_timeout={BusyTimeout}ms).")]
    private partial void LogAppliedSqlitePragmas(int busyTimeout);
}
