using Dapper;
using Microsoft.Data.Sqlite;

namespace GtaAtlas.Data;

/// <summary>
/// Rebuilds the index into a fresh file and swaps it in place of the live database.
/// Bulk load runs without journal or secondary indexes, which is several times faster than
/// deleting and re-inserting, and leaves no free pages behind (no VACUUM needed).
/// Favorites are carried over from the previous database.
/// </summary>
public static class IndexWriter
{
    public static void Rebuild(AtlasDb db, IReadOnlyList<DlcInput> dlcs, IReadOnlyCollection<AssetInput> assets,
        IReadOnlyDictionary<string, string>? meta = null)
    {
        var buildPath = db.Path + ".building";
        DeleteDatabaseFiles(buildPath);

        var build = AtlasDb.Open(buildPath);
        SqliteConnection.ClearAllPools(); // leaving WAL mode needs the migration connection gone
        using (var c = build.ConnectForBulkLoad())
        {
            c.Execute(Schema.DropIndexes);
            using (var tx = c.BeginTransaction())
            {
                var dlcIds = InsertDlcs(c, tx, dlcs);
                var assetIds = InsertAssets(c, tx, assets, dlcIds);
                LinkParents(c, tx, assets, assetIds);
                CopyFavorites(c, tx, db);
                foreach (var (key, value) in meta ?? new Dictionary<string, string>())
                    c.Execute("INSERT OR REPLACE INTO meta(key, value) VALUES (@key, @value)", new { key, value }, tx);
                tx.Commit();
            }
            c.Execute(Schema.Indexes);
            c.Execute("INSERT INTO asset_fts(asset_fts) VALUES ('optimize'); PRAGMA optimize;");
        }

        SqliteConnection.ClearAllPools();
        DeleteDatabaseFiles(db.Path, keepMain: true);
        File.Move(buildPath, db.Path, overwrite: true);
    }

    private static void CopyFavorites(SqliteConnection c, SqliteTransaction tx, AtlasDb live)
    {
        if (!File.Exists(live.Path))
            return;
        using var old = live.Connect();
        var favorites = old.Query<(string Kind, string Key)>("SELECT kind, key FROM favorite").ToList();
        foreach (var (kind, key) in favorites)
            c.Execute("INSERT OR IGNORE INTO favorite(kind, key) VALUES (@kind, @key)", new { kind, key }, tx);
    }

    private static void DeleteDatabaseFiles(string path, bool keepMain = false)
    {
        if (!keepMain)
            File.Delete(path);
        File.Delete(path + "-wal");
        File.Delete(path + "-shm");
    }

    private static Dictionary<string, long> InsertDlcs(SqliteConnection c, SqliteTransaction tx, IReadOnlyList<DlcInput> dlcs)
    {
        var ids = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in dlcs)
        {
            ids[d.Name] = c.ExecuteScalar<long>(
                """
                INSERT INTO dlc(name, device_name, order_index, setup_order, timestamp)
                VALUES (@Name, @DeviceName, @OrderIndex, @SetupOrder, @Timestamp)
                RETURNING id
                """, d, tx);
        }
        return ids;
    }

    private static Dictionary<(string, string), long> InsertAssets(
        SqliteConnection c, SqliteTransaction tx, IReadOnlyCollection<AssetInput> assets, Dictionary<string, long> dlcIds)
    {
        using var insertAsset = Prepare(c, tx,
            "INSERT INTO asset(id, kind, key, name, hash, display_name, dlc_id, data) VALUES ($id, $kind, $key, $name, $hash, $display, $dlc, $data)",
            "$id", "$kind", "$key", "$name", "$hash", "$display", "$dlc", "$data");
        using var insertFile = Prepare(c, tx,
            "INSERT INTO source_file(id, dlc_id, path) VALUES ($id, $dlc, $path)",
            "$id", "$dlc", "$path");
        using var insertSource = Prepare(c, tx,
            "INSERT OR IGNORE INTO asset_source(asset_id, file_id) VALUES ($asset, $file)",
            "$asset", "$file");
        using var insertTag = Prepare(c, tx,
            "INSERT OR IGNORE INTO asset_tag(asset_id, tag) VALUES ($asset, $tag)",
            "$asset", "$tag");
        using var insertFts = Prepare(c, tx,
            "INSERT INTO asset_fts(rowid, name, display_name, tags) VALUES ($id, $name, $display, $tags)",
            "$id", "$name", "$display", "$tags");

        var ids = new Dictionary<(string, string), long>(assets.Count);
        var files = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        long nextId = 0;
        foreach (var a in assets)
        {
            var id = ++nextId;
            if (!ids.TryAdd((a.Kind, a.Key), id))
                throw new InvalidOperationException($"Duplicate asset {a.Kind}/{a.Key}.");

            var tags = a.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Run(insertAsset, id, a.Kind, a.Key, a.Name, (long)a.Hash, a.DisplayName, DlcId(dlcIds, a.Dlc), a.Data);

            foreach (var s in a.Sources)
            {
                if (!files.TryGetValue(s.Path, out var fileId))
                {
                    fileId = files.Count + 1;
                    files[s.Path] = fileId;
                    Run(insertFile, fileId, DlcId(dlcIds, s.Dlc), s.Path);
                }
                Run(insertSource, id, fileId);
            }
            foreach (var t in tags)
                Run(insertTag, id, t);
            Run(insertFts, id, a.Name, a.DisplayName, string.Join(' ', tags));
        }
        return ids;
    }

    private static void LinkParents(SqliteConnection c, SqliteTransaction tx, IReadOnlyCollection<AssetInput> assets, Dictionary<(string, string), long> ids)
    {
        using var update = Prepare(c, tx, "UPDATE asset SET parent_id = $parent WHERE id = $id", "$parent", "$id");
        foreach (var a in assets)
        {
            if (a.Parent is { } p && ids.TryGetValue(p, out var parentId))
                Run(update, parentId, ids[(a.Kind, a.Key)]);
        }
    }

    private static long DlcId(Dictionary<string, long> dlcIds, string dlc) =>
        dlcIds.TryGetValue(dlc, out var id) ? id : throw new InvalidOperationException($"Unknown DLC '{dlc}'.");

    private static SqliteCommand Prepare(SqliteConnection c, SqliteTransaction tx, string sql, params string[] parameters)
    {
        var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var p in parameters)
            cmd.Parameters.Add(new SqliteParameter { ParameterName = p });
        cmd.Prepare();
        return cmd;
    }

    private static void Run(SqliteCommand cmd, params object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
            cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
        cmd.ExecuteNonQuery();
    }
}
