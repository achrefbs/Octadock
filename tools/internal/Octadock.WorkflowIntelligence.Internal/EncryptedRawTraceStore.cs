using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace Octadock.WorkflowIntelligence.Internal;

internal sealed record RawTraceContent(
    Guid Id,
    string SourceKind,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    string ContentSha256,
    string Content);

internal sealed record RawTraceStoreResult(Guid Id, bool Created);

internal sealed class EncryptedRawTraceStore
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly TracePaths _paths;
    private readonly TraceKeyStore _keyStore;

    internal EncryptedRawTraceStore(TracePaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _keyStore = new TraceKeyStore(paths);
    }

    internal async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        InternalConsent.DemandFullDeveloperTrace();
        Directory.CreateDirectory(_paths.Root);
        Directory.CreateDirectory(_paths.TempPath);
        Directory.CreateDirectory(_paths.ExportPath);
        byte[] key = _keyStore.GetOrCreateKey();
        CryptographicOperations.ZeroMemory(key);

        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE IF NOT EXISTS raw_documents (
                id TEXT PRIMARY KEY,
                source_kind TEXT NOT NULL,
                created_at TEXT NOT NULL,
                expires_at TEXT NOT NULL,
                content_sha256 TEXT NOT NULL,
                nonce BLOB NOT NULL,
                tag BLOB NOT NULL,
                ciphertext BLOB NOT NULL,
                aad BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_raw_documents_expires_at
                ON raw_documents(expires_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    internal async Task<Guid> StoreAsync(
        string sourceKind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        RawTraceStoreResult result = await StoreCoreAsync(
            Guid.NewGuid(),
            ignoreDuplicate: false,
            sourceKind,
            content,
            createdAt,
            expiresAt,
            cancellationToken).ConfigureAwait(false);
        return result.Id;
    }

    internal Task<RawTraceStoreResult> StoreOnceAsync(
        Guid stableId,
        string sourceKind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
        => StoreCoreAsync(
            stableId,
            ignoreDuplicate: true,
            sourceKind,
            content,
            createdAt,
            expiresAt,
            cancellationToken);

    private async Task<RawTraceStoreResult> StoreCoreAsync(
        Guid id,
        bool ignoreDuplicate,
        string sourceKind,
        string content,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        InternalConsent.DemandFullDeveloperTrace();
        if (string.IsNullOrWhiteSpace(sourceKind))
        {
            throw new ArgumentException("Source kind must not be empty.", nameof(sourceKind));
        }
        ArgumentNullException.ThrowIfNull(content);
        if (expiresAt <= createdAt)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Expiry must be later than creation time.");
        }

        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        string createdText = ToStorage(createdAt);
        string expiresText = ToStorage(expiresAt);
        byte[] plaintext = Encoding.UTF8.GetBytes(content);
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] tag = new byte[TagSize];
        byte[] aad = BuildAad(id, sourceKind, createdText, expiresText);
        byte[] key = _keyStore.GetOrCreateKey();

        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);

            await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = ignoreDuplicate
                ?
                """
                INSERT OR IGNORE INTO raw_documents
                    (id, source_kind, created_at, expires_at, content_sha256, nonce, tag, ciphertext, aad)
                VALUES
                    ($id, $source_kind, $created_at, $expires_at, $content_sha256, $nonce, $tag, $ciphertext, $aad);
                """
                :
                """
                INSERT INTO raw_documents
                    (id, source_kind, created_at, expires_at, content_sha256, nonce, tag, ciphertext, aad)
                VALUES
                    ($id, $source_kind, $created_at, $expires_at, $content_sha256, $nonce, $tag, $ciphertext, $aad);
                """;
            command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$source_kind", sourceKind);
            command.Parameters.AddWithValue("$created_at", createdText);
            command.Parameters.AddWithValue("$expires_at", expiresText);
            command.Parameters.AddWithValue("$content_sha256", Convert.ToHexString(SHA256.HashData(plaintext)));
            command.Parameters.AddWithValue("$nonce", nonce);
            command.Parameters.AddWithValue("$tag", tag);
            command.Parameters.AddWithValue("$ciphertext", ciphertext);
            command.Parameters.AddWithValue("$aad", aad);
            int affected = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            return new RawTraceStoreResult(id, affected == 1);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal async Task<RawTraceContent?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        InternalConsent.DemandFullDeveloperTrace();
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT source_kind, created_at, expires_at, content_sha256, nonce, tag, ciphertext, aad
            FROM raw_documents
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id.ToString("D", CultureInfo.InvariantCulture));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        string sourceKind = reader.GetString(0);
        string createdText = reader.GetString(1);
        string expiresText = reader.GetString(2);
        string hash = reader.GetString(3);
        byte[] nonce = (byte[])reader[4];
        byte[] tag = (byte[])reader[5];
        byte[] ciphertext = (byte[])reader[6];
        byte[] aad = (byte[])reader[7];
        byte[] expectedAad = BuildAad(id, sourceKind, createdText, expiresText);
        if (!CryptographicOperations.FixedTimeEquals(aad, expectedAad))
        {
            throw new CryptographicException("The encrypted raw trace metadata authentication data did not match.");
        }
        byte[] plaintext = new byte[ciphertext.Length];
        byte[] key = _keyStore.GetOrCreateKey();
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, expectedAad);
            string actualHash = Convert.ToHexString(SHA256.HashData(plaintext));
            if (!string.Equals(actualHash, hash, StringComparison.Ordinal))
            {
                throw new CryptographicException("The decrypted raw trace content hash did not match its manifest.");
            }

            return new RawTraceContent(
                id,
                sourceKind,
                ParseStorage(createdText),
                ParseStorage(expiresText),
                hash,
                Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    internal async Task<int> PurgeExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        InternalConsent.DemandFullDeveloperTrace();
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteConnection connection = await OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM raw_documents WHERE expires_at <= $now;";
        command.Parameters.AddWithValue("$now", ToStorage(now));
        int count = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand checkpoint = connection.CreateCommand();
        checkpoint.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await checkpoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return count;
    }

    internal Task DeleteAllAsync()
    {
        InternalConsent.DemandFullDeveloperTrace();
        SqliteConnection.ClearAllPools();
        DeleteFile(_paths.DatabasePath);
        DeleteFile($"{_paths.DatabasePath}-wal");
        DeleteFile($"{_paths.DatabasePath}-shm");
        DeleteFile(_paths.KeyPath);
        DeleteFile(_paths.SampleManifestPath);

        if (Directory.Exists(_paths.Root))
        {
            foreach (string quarantine in Directory.EnumerateFiles(
                         _paths.Root,
                         "workflow-traces.internal.db.corrupt-*",
                         SearchOption.TopDirectoryOnly))
            {
                DeleteFile(quarantine);
            }
        }

        DeleteManagedDirectory(_paths.TempPath);
        DeleteManagedDirectory(_paths.ExportPath);
        DeleteManagedDirectory(_paths.CursorsPath);
        if (Directory.Exists(_paths.Root) && !Directory.EnumerateFileSystemEntries(_paths.Root).Any())
        {
            Directory.Delete(_paths.Root);
        }
        return Task.CompletedTask;
    }

    private async Task<SqliteConnection> OpenConnectionAsync(CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand pragma = connection.CreateCommand();
        pragma.CommandText =
            """
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA secure_delete=ON;
            PRAGMA busy_timeout=5000;
            """;
        await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static byte[] BuildAad(Guid id, string sourceKind, string createdText, string expiresText)
        => Encoding.UTF8.GetBytes($"v1\n{id:D}\n{sourceKind}\n{createdText}\n{expiresText}");

    private static string ToStorage(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ParseStorage(string value)
        => DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private void DeleteManagedDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string rootWithSeparator = Path.EndsInDirectorySeparator(_paths.Root)
            ? _paths.Root
            : $"{_paths.Root}{Path.DirectorySeparatorChar}";
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Managed trace directory escaped the configured trace root.");
        }
        if (Directory.Exists(fullPath))
        {
            Directory.Delete(fullPath, recursive: true);
        }
    }
}
