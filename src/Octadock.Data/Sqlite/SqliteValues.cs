using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Octadock.Data.Sqlite;

/// <summary>
/// Low-level helpers for binding parameters and reading columns with consistent
/// conventions: timestamps as round-trippable ISO-8601 UTC strings, enums by
/// name, and <c>NULL</c> handling for optional columns. Values are always bound
/// through parameters, never string-concatenated, to prevent SQL injection.
/// </summary>
internal static class SqliteValues
{
    /// <summary>ISO-8601 round-trip format used for all persisted timestamps (UTC, 'Z' suffix).</summary>
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffffffZ";

    /// <summary>Serialises a <see cref="DateTimeOffset"/> to a normalized UTC ISO-8601 string.</summary>
    public static string ToStorage(DateTimeOffset value) =>
        value.ToUniversalTime().ToString(TimestampFormat, CultureInfo.InvariantCulture);

    /// <summary>Parses a stored timestamp string back into a UTC <see cref="DateTimeOffset"/>.</summary>
    public static DateTimeOffset ParseTimestamp(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind).ToUniversalTime();

    /// <summary>Adds a parameter, mapping a <see langword="null"/> CLR value to SQL <c>NULL</c>.</summary>
    public static SqliteParameter AddParameter(SqliteCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
        return parameter;
    }

    /// <summary>Adds an optional timestamp parameter (UTC ISO-8601 or <c>NULL</c>).</summary>
    public static void AddTimestamp(SqliteCommand command, string name, DateTimeOffset? value) =>
        AddParameter(command, name, value is { } instant ? ToStorage(instant) : null);

    /// <summary>Reads a required string column.</summary>
    public static string GetString(SqliteDataReader reader, int ordinal) => reader.GetString(ordinal);

    /// <summary>Reads an optional string column, returning <see langword="null"/> for <c>NULL</c>.</summary>
    public static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    /// <summary>Reads a required 32-bit integer column.</summary>
    public static int GetInt32(SqliteDataReader reader, int ordinal) => reader.GetInt32(ordinal);

    /// <summary>Reads a required 64-bit integer column.</summary>
    public static long GetInt64(SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal);

    /// <summary>Reads an optional 64-bit integer column.</summary>
    public static long? GetNullableInt64(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetInt64(ordinal);

    /// <summary>Reads a required double column.</summary>
    public static double GetDouble(SqliteDataReader reader, int ordinal) => reader.GetDouble(ordinal);

    /// <summary>Reads a boolean stored as 0/1.</summary>
    public static bool GetBoolean(SqliteDataReader reader, int ordinal) => reader.GetInt64(ordinal) != 0;

    /// <summary>Reads a required timestamp column.</summary>
    public static DateTimeOffset GetTimestamp(SqliteDataReader reader, int ordinal) =>
        ParseTimestamp(reader.GetString(ordinal));

    /// <summary>Reads an optional timestamp column.</summary>
    public static DateTimeOffset? GetNullableTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ParseTimestamp(reader.GetString(ordinal));

    /// <summary>Parses an enum column stored by name (case-insensitive).</summary>
    public static TEnum GetEnum<TEnum>(SqliteDataReader reader, int ordinal)
        where TEnum : struct, Enum =>
        Enum.TryParse(reader.GetString(ordinal), ignoreCase: true, out TEnum value) &&
        Enum.IsDefined(value)
            ? value
            : default;

    /// <summary>Reads an optional <see cref="Guid"/> column stored as text.</summary>
    public static Guid? GetNullableGuid(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
}
