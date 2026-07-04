using Microsoft.Data.Sqlite;
using Octadock.Core.Geometry;
using Octadock.Core.Models;
using Octadock.Data.Sqlite;

namespace Octadock.Data.Repositories;

/// <summary>
/// Translates between <see cref="CaptureRecord"/> instances and rows of the
/// <c>captures</c> table. The <see cref="Columns"/> projection order is shared by
/// every read query so the ordinals used by <see cref="Map"/> stay in sync.
/// </summary>
internal static class CaptureMapper
{
    /// <summary>The column list, in the exact order <see cref="Map"/> reads them.</summary>
    public const string Columns =
        "id, type, created_at, source_process, source_window, hwnd_hash, monitor_id, " +
        "pixel_width, pixel_height, dpi_scale, original_path, thumbnail_path, project_path, " +
        "duration_ms, deleted_at";

    /// <summary>Materialises a <see cref="CaptureRecord"/> from a reader positioned on a row.</summary>
    public static CaptureRecord Map(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(SqliteValues.GetString(reader, 0)),
        Type = SqliteValues.GetEnum<CaptureType>(reader, 1),
        CreatedAt = SqliteValues.GetTimestamp(reader, 2),
        Source = new CaptureSource(
            SqliteValues.GetNullableString(reader, 3),
            SqliteValues.GetNullableString(reader, 4),
            SqliteValues.GetNullableString(reader, 5)),
        MonitorId = new MonitorId(SqliteValues.GetString(reader, 6)),
        PixelWidth = SqliteValues.GetInt32(reader, 7),
        PixelHeight = SqliteValues.GetInt32(reader, 8),
        DpiScale = SqliteValues.GetDouble(reader, 9),
        OriginalPath = SqliteValues.GetString(reader, 10),
        ThumbnailPath = SqliteValues.GetNullableString(reader, 11),
        ProjectPath = SqliteValues.GetNullableString(reader, 12),
        DurationMs = SqliteValues.GetNullableInt64(reader, 13),
        DeletedAt = SqliteValues.GetNullableTimestamp(reader, 14),
    };

    /// <summary>Binds every capture field to <paramref name="command"/> as named parameters.</summary>
    public static void BindParameters(SqliteCommand command, CaptureRecord record)
    {
        SqliteValues.AddParameter(command, "$id", record.Id.ToString());
        SqliteValues.AddParameter(command, "$type", record.Type.ToString());
        SqliteValues.AddParameter(command, "$created_at", SqliteValues.ToStorage(record.CreatedAt));
        SqliteValues.AddParameter(command, "$source_process", record.Source.ProcessName);
        SqliteValues.AddParameter(command, "$source_window", record.Source.WindowTitle);
        SqliteValues.AddParameter(command, "$hwnd_hash", record.Source.HwndHash);
        SqliteValues.AddParameter(command, "$monitor_id", record.MonitorId.Value);
        SqliteValues.AddParameter(command, "$pixel_width", record.PixelWidth);
        SqliteValues.AddParameter(command, "$pixel_height", record.PixelHeight);
        SqliteValues.AddParameter(command, "$dpi_scale", record.DpiScale);
        SqliteValues.AddParameter(command, "$original_path", record.OriginalPath);
        SqliteValues.AddParameter(command, "$thumbnail_path", record.ThumbnailPath);
        SqliteValues.AddParameter(command, "$project_path", record.ProjectPath);
        SqliteValues.AddParameter(command, "$duration_ms", record.DurationMs);
        SqliteValues.AddTimestamp(command, "$deleted_at", record.DeletedAt);
    }
}
