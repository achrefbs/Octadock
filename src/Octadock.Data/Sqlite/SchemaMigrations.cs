namespace Octadock.Data.Sqlite;

/// <summary>A single, ordered schema migration identified by its target version.</summary>
/// <param name="Version">The <c>user_version</c> the database has after this migration runs.</param>
/// <param name="Sql">The DDL executed inside a transaction to reach <paramref name="Version"/>.</param>
internal readonly record struct SchemaMigration(int Version, string Sql);

/// <summary>
/// The ordered set of schema migrations for the Octadock database. The database's
/// current version is tracked with SQLite's <c>PRAGMA user_version</c>; the
/// migration runner applies every migration whose <see cref="SchemaMigration.Version"/>
/// exceeds the current version, in order.
/// </summary>
internal static class SchemaMigrations
{
    /// <summary>The schema version a fully-migrated database reports.</summary>
    public static int LatestVersion => All[^1].Version;

    /// <summary>All migrations in ascending version order.</summary>
    public static readonly IReadOnlyList<SchemaMigration> All =
    [
        new SchemaMigration(1, Migration1CreateSchema),
        new SchemaMigration(2, Migration2AddPinCaptureForeignKey),
        new SchemaMigration(3, Migration3AddPinImagePath),
        new SchemaMigration(4, Migration4AddAiSessions),
        new SchemaMigration(5, Migration5AddClipboardClips),
    ];

    /// <summary>Migration 1: creates the captures/actions/pins/settings tables and indexes.</summary>
    private const string Migration1CreateSchema =
        """
        CREATE TABLE captures (
            id             TEXT    NOT NULL PRIMARY KEY,
            type           TEXT    NOT NULL,
            created_at     TEXT    NOT NULL,
            source_process TEXT        NULL,
            source_window  TEXT        NULL,
            hwnd_hash      TEXT        NULL,
            monitor_id     TEXT    NOT NULL,
            pixel_width    INTEGER NOT NULL,
            pixel_height   INTEGER NOT NULL,
            dpi_scale      REAL    NOT NULL,
            original_path  TEXT    NOT NULL,
            thumbnail_path TEXT        NULL,
            project_path   TEXT        NULL,
            duration_ms    INTEGER     NULL,
            deleted_at     TEXT        NULL
        );

        CREATE TABLE actions (
            id            TEXT NOT NULL PRIMARY KEY,
            capture_id    TEXT NOT NULL,
            action_type   TEXT NOT NULL,
            created_at    TEXT NOT NULL,
            destination   TEXT     NULL,
            metadata_json TEXT     NULL,
            FOREIGN KEY (capture_id) REFERENCES captures (id) ON DELETE CASCADE
        );

        CREATE TABLE pins (
            id              TEXT    NOT NULL PRIMARY KEY,
            capture_id      TEXT        NULL,
            x               INTEGER NOT NULL,
            y               INTEGER NOT NULL,
            width           INTEGER NOT NULL,
            height          INTEGER NOT NULL,
            opacity         REAL    NOT NULL,
            click_through   INTEGER NOT NULL,
            monitor_id      TEXT    NOT NULL,
            last_visible_at TEXT        NULL
        );

        CREATE TABLE settings (
            key        TEXT NOT NULL PRIMARY KEY,
            value_json TEXT NOT NULL,
            updated_at TEXT NOT NULL
        );

        CREATE INDEX ix_captures_created_at ON captures (created_at);
        CREATE INDEX ix_captures_type       ON captures (type);
        CREATE INDEX ix_captures_deleted_at ON captures (deleted_at);
        CREATE INDEX ix_actions_capture_id  ON actions (capture_id);
        """;

    /// <summary>Migration 2: pins tied to captures cascade when the capture is hard-deleted.</summary>
    private const string Migration2AddPinCaptureForeignKey =
        """
        CREATE TABLE pins_new (
            id              TEXT    NOT NULL PRIMARY KEY,
            capture_id      TEXT        NULL,
            x               INTEGER NOT NULL,
            y               INTEGER NOT NULL,
            width           INTEGER NOT NULL,
            height          INTEGER NOT NULL,
            opacity         REAL    NOT NULL,
            click_through   INTEGER NOT NULL,
            monitor_id      TEXT    NOT NULL,
            last_visible_at TEXT        NULL,
            FOREIGN KEY (capture_id) REFERENCES captures (id) ON DELETE CASCADE
        );

        INSERT INTO pins_new (
            id, capture_id, x, y, width, height, opacity, click_through, monitor_id, last_visible_at)
        SELECT
            id, capture_id, x, y, width, height, opacity, click_through, monitor_id, last_visible_at
        FROM pins
        WHERE capture_id IS NULL
           OR EXISTS (SELECT 1 FROM captures WHERE captures.id = pins.capture_id);

        DROP TABLE pins;
        ALTER TABLE pins_new RENAME TO pins;

        CREATE INDEX ix_pins_capture_id ON pins (capture_id);
        """;

    /// <summary>Migration 3: non-capture pins keep a managed image source for restore.</summary>
    private const string Migration3AddPinImagePath =
        """
        ALTER TABLE pins ADD COLUMN image_path TEXT NULL;
        """;

    /// <summary>Migration 4: adds provider-neutral AI session tracking tables.</summary>
    private const string Migration4AddAiSessions =
        """
        CREATE TABLE ai_sessions (
            id                TEXT    NOT NULL PRIMARY KEY,
            provider          TEXT    NOT NULL,
            title             TEXT    NOT NULL,
            cwd               TEXT        NULL,
            git_branch        TEXT        NULL,
            command           TEXT        NULL,
            pid               INTEGER     NULL,
            status            TEXT    NOT NULL,
            started_at        TEXT    NOT NULL,
            ended_at          TEXT        NULL,
            exit_code         INTEGER     NULL,
            last_event_at     TEXT        NULL,
            notification_mode TEXT    NOT NULL,
            metadata_json     TEXT        NULL
        );

        CREATE TABLE ai_session_events (
            id            TEXT NOT NULL PRIMARY KEY,
            session_id    TEXT NOT NULL,
            event_type    TEXT NOT NULL,
            created_at    TEXT NOT NULL,
            message       TEXT     NULL,
            metadata_json TEXT     NULL,
            FOREIGN KEY (session_id) REFERENCES ai_sessions (id) ON DELETE CASCADE
        );

        CREATE TABLE ai_session_artifacts (
            id            TEXT NOT NULL PRIMARY KEY,
            session_id    TEXT NOT NULL,
            artifact_type TEXT NOT NULL,
            created_at    TEXT NOT NULL,
            capture_id    TEXT     NULL,
            external_id   TEXT     NULL,
            path          TEXT     NULL,
            uri           TEXT     NULL,
            title         TEXT     NULL,
            metadata_json TEXT     NULL,
            FOREIGN KEY (session_id) REFERENCES ai_sessions (id) ON DELETE CASCADE,
            FOREIGN KEY (capture_id) REFERENCES captures (id) ON DELETE SET NULL
        );

        CREATE INDEX ix_ai_sessions_provider             ON ai_sessions (provider);
        CREATE INDEX ix_ai_sessions_status               ON ai_sessions (status);
        CREATE INDEX ix_ai_sessions_started_at           ON ai_sessions (started_at);
        CREATE INDEX ix_ai_sessions_last_event_at        ON ai_sessions (last_event_at);
        CREATE INDEX ix_ai_session_events_session_created ON ai_session_events (session_id, created_at);
        CREATE INDEX ix_ai_session_artifacts_session     ON ai_session_artifacts (session_id);
        CREATE INDEX ix_ai_session_artifacts_capture_id  ON ai_session_artifacts (capture_id);
        """;

    /// <summary>Migration 5: adds clipboard-history clip metadata and payload pointers.</summary>
    private const string Migration5AddClipboardClips =
        """
        CREATE TABLE clipboard_clips (
            id             TEXT    NOT NULL PRIMARY KEY,
            kind           TEXT    NOT NULL,
            created_at     TEXT    NOT NULL,
            last_seen_at   TEXT    NOT NULL,
            seen_count     INTEGER NOT NULL DEFAULT 1,
            source_process TEXT        NULL,
            source_window  TEXT        NULL,
            format_name    TEXT        NULL,
            text           TEXT        NULL,
            image_path     TEXT        NULL,
            thumbnail_path TEXT        NULL,
            content_hash   TEXT        NULL,
            size_bytes     INTEGER     NULL,
            is_favorite    INTEGER NOT NULL DEFAULT 0,
            deleted_at     TEXT        NULL,
            metadata_json  TEXT        NULL,
            CHECK (seen_count >= 1)
        );

        CREATE INDEX ix_clipboard_clips_created_at   ON clipboard_clips (created_at);
        CREATE INDEX ix_clipboard_clips_last_seen_at ON clipboard_clips (last_seen_at);
        CREATE INDEX ix_clipboard_clips_kind         ON clipboard_clips (kind);
        CREATE INDEX ix_clipboard_clips_deleted_at   ON clipboard_clips (deleted_at);
        CREATE INDEX ix_clipboard_clips_favorite     ON clipboard_clips (is_favorite);
        CREATE INDEX ix_clipboard_clips_content_hash ON clipboard_clips (kind, content_hash);
        """;
}
