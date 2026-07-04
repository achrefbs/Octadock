using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Octadock.Core.Common;
using Octadock.Data.Repositories;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Tests.Infrastructure;

/// <summary>
/// Spins up a real, isolated SQLite database backed by a temp file for a single
/// test, exposes ready-to-use repositories over it, and deletes the file (plus
/// WAL/SHM side files) on dispose.
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly string _databasePath;

    private TestDatabase(string databasePath, MutableClock clock)
    {
        _databasePath = databasePath;
        Clock = clock;

        var connectionString = SqliteConnectionFactory.BuildFileConnectionString(databasePath);
        ConnectionFactory = new SqliteConnectionFactory(connectionString, NullLogger<SqliteConnectionFactory>.Instance);

        Database = new OctadockDatabase(ConnectionFactory, NullLogger<OctadockDatabase>.Instance);
        Captures = new CaptureRepository(ConnectionFactory, NullLogger<CaptureRepository>.Instance);
        Actions = new ActionRepository(ConnectionFactory, NullLogger<ActionRepository>.Instance);
        Pins = new PinRepository(ConnectionFactory, NullLogger<PinRepository>.Instance);
        ClipboardClips = new ClipboardClipRepository(ConnectionFactory, NullLogger<ClipboardClipRepository>.Instance);
        AiSessions = new AiSessionRepository(ConnectionFactory, NullLogger<AiSessionRepository>.Instance);
        Settings = new SettingsStore(ConnectionFactory, clock, NullLogger<SettingsStore>.Instance);
    }

    /// <summary>The database file path used for this instance.</summary>
    public string DatabasePath => _databasePath;

    /// <summary>A clock whose value tests can advance to exercise timestamp behavior.</summary>
    public MutableClock Clock { get; }

    public SqliteConnectionFactory ConnectionFactory { get; }

    public OctadockDatabase Database { get; }

    public CaptureRepository Captures { get; }

    public ActionRepository Actions { get; }

    public PinRepository Pins { get; }

    public ClipboardClipRepository ClipboardClips { get; }

    public AiSessionRepository AiSessions { get; }

    public SettingsStore Settings { get; }

    /// <summary>Creates and initializes a fresh temp-file database.</summary>
    public static async Task<TestDatabase> CreateAsync(MutableClock? clock = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"octadock-test-{Guid.NewGuid():N}.db");
        var database = new TestDatabase(path, clock ?? new MutableClock());
        await database.Database.InitializeAsync();
        return database;
    }

    /// <summary>Opens a raw connection (used by schema-introspection assertions).</summary>
    public SqliteConnection OpenConnection() => ConnectionFactory.OpenConnection();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Database.Dispose();

        // Pooled connections keep the file handle open on some platforms; clear the
        // pool so the temp files can be removed cleanly.
        SqliteConnection.ClearAllPools();

        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            var file = _databasePath + suffix;
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (IOException)
            {
                // Best-effort cleanup; the OS temp directory is reclaimed regardless.
            }
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>An <see cref="IClock"/> whose current time is settable, for deterministic tests.</summary>
public sealed class MutableClock : IClock
{
    /// <summary>Creates a clock fixed at the given instant (defaults to a stable epoch).</summary>
    public MutableClock(DateTimeOffset? now = null)
    {
        UtcNow = (now ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)).ToUniversalTime();
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow { get; set; }

    /// <inheritdoc />
    public DateTimeOffset LocalNow => UtcNow.ToLocalTime();

    /// <summary>Advances the clock by <paramref name="delta"/> and returns the new value.</summary>
    public DateTimeOffset Advance(TimeSpan delta) => UtcNow += delta;
}
