using System.Globalization;
using Microsoft.Data.Sqlite;
using Octadock.Core.Context;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// SQLite-backed <see cref="IContextRepository"/> over the <c>context_packages</c> /
/// <c>context_items</c> / <c>context_item_derivatives</c> tables (WS10). Additive and
/// independent of captures: an item snapshots its bytes into managed storage and only
/// references its source capture for provenance (<c>ON DELETE SET NULL</c>), so items
/// survive the capture being discarded.
/// </summary>
public sealed class ContextRepository : IContextRepository
{
    private readonly ISqliteConnectionFactory _connectionFactory;

    public ContextRepository(ISqliteConnectionFactory connectionFactory)
        => _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));

    /// <inheritdoc />
    public async Task<ContextPackage> CreatePackageAsync(string name, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var package = new ContextPackage { Id = Guid.NewGuid(), Name = name.Trim(), CreatedAt = now };

        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO context_packages (id, name, created_at, updated_at) VALUES ($id, $name, $now, $now);";
        SqliteValues.AddParameter(command, "$id", package.Id.ToString());
        SqliteValues.AddParameter(command, "$name", package.Name);
        SqliteValues.AddParameter(command, "$now", SqliteValues.ToStorage(now));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return package;
    }

    /// <inheritdoc />
    public async Task RenamePackageAsync(Guid packageId, string name, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE context_packages SET name = $name, updated_at = $now WHERE id = $id;";
        SqliteValues.AddParameter(command, "$name", name.Trim());
        SqliteValues.AddParameter(command, "$now", SqliteValues.ToStorage(now));
        SqliteValues.AddParameter(command, "$id", packageId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task UpdatePackageNotesAsync(
        Guid packageId,
        string notes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notes);
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "UPDATE context_packages SET notes = $notes, updated_at = $now WHERE id = $id;";
        SqliteValues.AddParameter(command, "$notes", notes);
        SqliteValues.AddParameter(command, "$now", SqliteValues.ToStorage(now));
        SqliteValues.AddParameter(command, "$id", packageId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DeletePackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM context_packages WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", packageId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RemoveItemAsync(Guid itemId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM context_items WHERE id = $id;";
        SqliteValues.AddParameter(command, "$id", itemId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task AddItemAsync(Guid packageId, ContextItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);

        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        long sortOrder;
        await using (SqliteCommand next = connection.CreateCommand())
        {
            next.Transaction = transaction;
            next.CommandText = "SELECT COALESCE(MAX(sort_order), -1) + 1 FROM context_items WHERE package_id = $pid;";
            SqliteValues.AddParameter(next, "$pid", packageId.ToString());
            sortOrder = Convert.ToInt64(await next.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
        }

        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText =
                """
                INSERT INTO context_items (
                    id, package_id, display_name, ownership, storage_path, reference_source,
                    reference_sha256, size_bytes, source_capture_id, added_at, sort_order)
                VALUES (
                    $id, $pid, $name, $ownership, $storage, $refsrc,
                    $refhash, $size, $capture, $added, $sort);
                """;
            SqliteValues.AddParameter(insert, "$id", item.Id.ToString());
            SqliteValues.AddParameter(insert, "$pid", packageId.ToString());
            SqliteValues.AddParameter(insert, "$name", item.DisplayName);
            SqliteValues.AddParameter(insert, "$ownership", item.Ownership.ToString().ToLowerInvariant());
            SqliteValues.AddParameter(insert, "$storage", item.StorageRelativePath);
            SqliteValues.AddParameter(insert, "$refsrc", item.ReferenceSourcePath);
            SqliteValues.AddParameter(insert, "$refhash", item.ReferenceSha256);
            SqliteValues.AddParameter(insert, "$size", item.SizeBytes);
            SqliteValues.AddParameter(insert, "$capture", item.SourceCaptureId?.ToString());
            SqliteValues.AddParameter(insert, "$added", SqliteValues.ToStorage(item.AddedAt));
            SqliteValues.AddParameter(insert, "$sort", sortOrder);
            await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (ContextDerivative derivative in item.Derivatives)
        {
            await using SqliteCommand insertDerivative = connection.CreateCommand();
            insertDerivative.Transaction = transaction;
            insertDerivative.CommandText =
                "INSERT INTO context_item_derivatives (id, item_id, kind, storage_path) VALUES ($id, $item, $kind, $path);";
            SqliteValues.AddParameter(insertDerivative, "$id", Guid.NewGuid().ToString());
            SqliteValues.AddParameter(insertDerivative, "$item", item.Id.ToString());
            SqliteValues.AddParameter(insertDerivative, "$kind", derivative.Kind.ToString().ToLowerInvariant());
            SqliteValues.AddParameter(insertDerivative, "$path", derivative.StorageRelativePath);
            await insertDerivative.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReorderItemsAsync(
        Guid packageId,
        IReadOnlyList<Guid> orderedItemIds,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(orderedItemIds);
        if (orderedItemIds.Count != orderedItemIds.Distinct().Count())
        {
            throw new ArgumentException("The Context item order cannot contain duplicate ids.", nameof(orderedItemIds));
        }

        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using SqliteTransaction transaction =
            (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var currentIds = new List<Guid>();
        await using (SqliteCommand current = connection.CreateCommand())
        {
            current.Transaction = transaction;
            current.CommandText = "SELECT id FROM context_items WHERE package_id = $pid ORDER BY sort_order, id;";
            SqliteValues.AddParameter(current, "$pid", packageId.ToString());
            await using SqliteDataReader reader = await current.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                currentIds.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        if (currentIds.Count != orderedItemIds.Count ||
            !currentIds.ToHashSet().SetEquals(orderedItemIds))
        {
            throw new InvalidOperationException(
                "The Context changed before its item order could be saved. Reload it and try again.");
        }

        for (int index = 0; index < orderedItemIds.Count; index++)
        {
            await using SqliteCommand update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText =
                "UPDATE context_items SET sort_order = $sort WHERE id = $id AND package_id = $pid;";
            SqliteValues.AddParameter(update, "$sort", index);
            SqliteValues.AddParameter(update, "$id", orderedItemIds[index].ToString());
            SqliteValues.AddParameter(update, "$pid", packageId.ToString());
            await update.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (SqliteCommand touch = connection.CreateCommand())
        {
            touch.Transaction = transaction;
            touch.CommandText = "UPDATE context_packages SET updated_at = $now WHERE id = $id;";
            SqliteValues.AddParameter(touch, "$now", SqliteValues.ToStorage(now));
            SqliteValues.AddParameter(touch, "$id", packageId.ToString());
            int touched = await touch.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            if (touched != 1)
            {
                throw new InvalidOperationException("The Context package no longer exists.");
            }
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ContextPackage?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        return await LoadPackageAsync(connection, packageId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ContextPackage>> GetPackagesAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        var ids = new List<Guid>();
        await using (SqliteCommand list = connection.CreateCommand())
        {
            list.CommandText = "SELECT id FROM context_packages ORDER BY created_at DESC;";
            await using SqliteDataReader reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var packages = new List<ContextPackage>(ids.Count);
        foreach (Guid id in ids)
        {
            ContextPackage? package = await LoadPackageAsync(connection, id, cancellationToken).ConfigureAwait(false);
            if (package is not null)
            {
                packages.Add(package);
            }
        }

        return packages;
    }

    private static async Task<ContextPackage?> LoadPackageAsync(
        SqliteConnection connection, Guid packageId, CancellationToken cancellationToken)
    {
        string name;
        string notes;
        DateTimeOffset createdAt;
        await using (SqliteCommand head = connection.CreateCommand())
        {
            head.CommandText = "SELECT name, notes, created_at FROM context_packages WHERE id = $id;";
            SqliteValues.AddParameter(head, "$id", packageId.ToString());
            await using SqliteDataReader reader = await head.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                return null;
            }

            name = reader.GetString(0);
            notes = reader.GetString(1);
            createdAt = SqliteValues.ParseTimestamp(reader.GetString(2));
        }

        var items = new List<ContextItem>();
        await using (SqliteCommand itemCommand = connection.CreateCommand())
        {
            itemCommand.CommandText =
                """
                SELECT id, display_name, ownership, storage_path, reference_source, reference_sha256,
                       size_bytes, source_capture_id, added_at
                  FROM context_items WHERE package_id = $pid ORDER BY sort_order;
                """;
            SqliteValues.AddParameter(itemCommand, "$pid", packageId.ToString());
            await using SqliteDataReader reader = await itemCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                items.Add(new ContextItem
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    DisplayName = reader.GetString(1),
                    Ownership = ParseOwnership(reader.GetString(2)),
                    StorageRelativePath = reader.IsDBNull(3) ? null : reader.GetString(3),
                    ReferenceSourcePath = reader.IsDBNull(4) ? null : reader.GetString(4),
                    ReferenceSha256 = reader.IsDBNull(5) ? null : reader.GetString(5),
                    SizeBytes = reader.GetInt64(6),
                    SourceCaptureId = reader.IsDBNull(7) ? null : Guid.Parse(reader.GetString(7)),
                    AddedAt = SqliteValues.ParseTimestamp(reader.GetString(8)),
                });
            }
        }

        var withDerivatives = new List<ContextItem>(items.Count);
        foreach (ContextItem item in items)
        {
            withDerivatives.Add(item with { Derivatives = await LoadDerivativesAsync(connection, item.Id, cancellationToken).ConfigureAwait(false) });
        }

        return new ContextPackage
        {
            Id = packageId,
            Name = name,
            Notes = notes,
            CreatedAt = createdAt,
            Items = withDerivatives,
        };
    }

    private static async Task<IReadOnlyList<ContextDerivative>> LoadDerivativesAsync(
        SqliteConnection connection, Guid itemId, CancellationToken cancellationToken)
    {
        var derivatives = new List<ContextDerivative>();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT kind, storage_path FROM context_item_derivatives WHERE item_id = $id ORDER BY kind;";
        SqliteValues.AddParameter(command, "$id", itemId.ToString());
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            derivatives.Add(new ContextDerivative(ParseDerivativeKind(reader.GetString(0)), reader.GetString(1)));
        }

        return derivatives;
    }

    private static ContextOwnership ParseOwnership(string value)
        => string.Equals(value, "reference", StringComparison.OrdinalIgnoreCase)
            ? ContextOwnership.Reference
            : ContextOwnership.Snapshot;

    private static ContextDerivativeKind ParseDerivativeKind(string value) => value.ToLowerInvariant() switch
    {
        "ocr" => ContextDerivativeKind.Ocr,
        "annotation" => ContextDerivativeKind.Annotation,
        _ => ContextDerivativeKind.Thumbnail,
    };
}
