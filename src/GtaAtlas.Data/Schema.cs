namespace GtaAtlas.Data;

/// <summary>Ordered schema migrations. Append new entries; never edit shipped ones.</summary>
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
            dlc_id       INTEGER NOT NULL REFERENCES dlc(id), -- first DLC (load order) that ships it
            data         TEXT,                   -- kind-specific JSON
            UNIQUE (kind, key)
        );
        CREATE INDEX ix_asset_kind_dlc ON asset(kind, dlc_id);
        CREATE INDEX ix_asset_hash ON asset(hash);
        CREATE INDEX ix_asset_parent ON asset(parent_id);

        -- Every file that defines the asset; more than one means later DLCs override it.
        CREATE TABLE asset_source (
            asset_id INTEGER NOT NULL,
            dlc_id   INTEGER NOT NULL,
            path     TEXT NOT NULL,
            PRIMARY KEY (asset_id, path)
        ) WITHOUT ROWID;
        CREATE INDEX ix_source_dlc ON asset_source(dlc_id);

        CREATE TABLE asset_tag (
            asset_id INTEGER NOT NULL,
            tag      TEXT NOT NULL,
            PRIMARY KEY (asset_id, tag)
        ) WITHOUT ROWID;
        CREATE INDEX ix_tag ON asset_tag(tag, asset_id);

        -- rowid = asset.id. Trigram tokenizer gives substring search on names.
        CREATE VIRTUAL TABLE asset_fts USING fts5(name, display_name, tags, tokenize = 'trigram case_sensitive 0');

        -- Keyed by kind/key so favorites survive a full reindex.
        CREATE TABLE favorite (
            kind TEXT NOT NULL,
            key  TEXT NOT NULL,
            PRIMARY KEY (kind, key)
        ) WITHOUT ROWID;
        """,
    ];
}
