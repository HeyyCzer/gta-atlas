namespace GtaAtlas.Data;

/// <summary>Ordered schema migrations. Append new entries once the app has been released; before that, edit v1.</summary>
internal static class Schema
{
    public static readonly string[] Migrations =
    [
        """
        CREATE TABLE meta (
            key   TEXT PRIMARY KEY,
            value TEXT NOT NULL
        ) WITHOUT ROWID;

        CREATE TABLE dlc (
            id          INTEGER PRIMARY KEY,
            name        TEXT NOT NULL UNIQUE,   -- pack folder (mpheist4), or 'base' / 'update'
            device_name TEXT,
            order_index INTEGER NOT NULL,       -- load order: base first, update last
            setup_order INTEGER,
            timestamp   TEXT,
            title       TEXT
        );

        CREATE TABLE asset (
            id           INTEGER PRIMARY KEY,
            kind         TEXT NOT NULL,
            key          TEXT NOT NULL,          -- unique within kind (usually name; 'dict/clip' for anims)
            name         TEXT NOT NULL,
            hash         INTEGER NOT NULL,       -- joaat as unsigned
            display_name TEXT,
            parent_id    INTEGER,
            dlc_id       INTEGER NOT NULL,       -- first DLC (load order) that ships it
            data         TEXT                    -- kind-specific JSON
        );

        -- Game files that define assets; paths are shared (every clip points at its .ycd).
        CREATE TABLE source_file (
            id     INTEGER PRIMARY KEY,
            dlc_id INTEGER NOT NULL,
            path   TEXT NOT NULL
        );

        -- Every file that defines the asset; more than one means later DLCs override it.
        CREATE TABLE asset_source (
            asset_id INTEGER NOT NULL,
            file_id  INTEGER NOT NULL,
            PRIMARY KEY (asset_id, file_id)
        ) WITHOUT ROWID;

        CREATE TABLE asset_tag (
            asset_id INTEGER NOT NULL,
            tag      TEXT NOT NULL,
            PRIMARY KEY (asset_id, tag)
        ) WITHOUT ROWID;

        -- rowid = asset.id. Contentless: only used for MATCH. Trigram gives substring search.
        CREATE VIRTUAL TABLE asset_fts USING fts5(name, display_name, tags, content = '', tokenize = 'trigram case_sensitive 0');

        -- Keyed by kind/key so favorites survive a full reindex.
        CREATE TABLE favorite (
            kind TEXT NOT NULL,
            key  TEXT NOT NULL,
            PRIMARY KEY (kind, key)
        ) WITHOUT ROWID;
        """ + Indexes,
    ];

    /// <summary>Secondary indexes. Also run by <see cref="IndexWriter"/> after bulk load, which builds without them.</summary>
    public const string Indexes =
        """

        CREATE UNIQUE INDEX IF NOT EXISTS ux_asset_kind_key ON asset(kind, key);
        CREATE INDEX IF NOT EXISTS ix_asset_kind_dlc ON asset(kind, dlc_id);
        CREATE INDEX IF NOT EXISTS ix_asset_hash ON asset(hash);
        CREATE INDEX IF NOT EXISTS ix_asset_parent ON asset(parent_id);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_source_file_path ON source_file(path);
        CREATE INDEX IF NOT EXISTS ix_source_file_dlc ON source_file(dlc_id);
        CREATE INDEX IF NOT EXISTS ix_asset_source_file ON asset_source(file_id);
        CREATE INDEX IF NOT EXISTS ix_tag ON asset_tag(tag, asset_id);
        """;

    public const string DropIndexes =
        """
        DROP INDEX IF EXISTS ux_asset_kind_key;
        DROP INDEX IF EXISTS ix_asset_kind_dlc;
        DROP INDEX IF EXISTS ix_asset_hash;
        DROP INDEX IF EXISTS ix_asset_parent;
        DROP INDEX IF EXISTS ux_source_file_path;
        DROP INDEX IF EXISTS ix_source_file_dlc;
        DROP INDEX IF EXISTS ix_asset_source_file;
        DROP INDEX IF EXISTS ix_tag;
        """;
}
