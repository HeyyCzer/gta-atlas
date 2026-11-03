using Dapper;
using Microsoft.Data.Sqlite;

namespace GtaAtlas.Data;

/// <summary>Replaces the whole index in one transaction (readers keep seeing the old data until commit).</summary>
public static class IndexWriter
{
    public static void ReplaceAll(AtlasDb db, IReadOnlyList<DlcInput> dlcs, IReadOnlyCollection<AssetInput> assets)
    {
        using var c = db.Connect();
        using var tx = c.BeginTransaction();

        c.Execute("DELETE FROM asset_fts; DELETE FROM asset_tag; DELETE FROM asset_source; DELETE FROM asset; DELETE FROM dlc;", transaction: tx);

        var dlcIds = InsertDlcs(c, tx, dlcs);
        var assetIds = InsertAssets(c, tx, assets, dlcIds);
        LinkParents(c, tx, assets, assetIds);

        tx.Commit();
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
            "INSERT INTO asset(kind, key, name, hash, display_name, dlc_id, data) VALUES ($kind, $key, $name, $hash, $display, $dlc, $data) RETURNING id",
            "$kind", "$key", "$name", "$hash", "$display", "$dlc", "$data");
        using var insertSource = Prepare(c, tx,
            "INSERT OR IGNORE INTO asset_source(asset_id, dlc_id, path) VALUES ($asset, $dlc, $path)",
            "$asset", "$dlc", "$path");
        using var insertTag = Prepare(c, tx,
            "INSERT OR IGNORE INTO asset_tag(asset_id, tag) VALUES ($asset, $tag)",
            "$asset", "$tag");
        using var insertFts = Prepare(c, tx,
            "INSERT INTO asset_fts(rowid, name, display_name, tags) VALUES ($id, $name, $display, $tags)",
            "$id", "$name", "$display", "$tags");

        var ids = new Dictionary<(string, string), long>(assets.Count);
        foreach (var a in assets)
        {
            var dlcId = DlcId(dlcIds, a.Dlc);
            var tags = a.Tags.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var id = (long)Run(insertAsset, a.Kind, a.Key, a.Name, (long)a.Hash, a.DisplayName, dlcId, a.Data)!;
            ids[(a.Kind, a.Key)] = id;

            foreach (var s in a.Sources)
                RunNonQuery(insertSource, id, DlcId(dlcIds, s.Dlc), s.Path);
            foreach (var t in tags)
                RunNonQuery(insertTag, id, t);
            RunNonQuery(insertFts, id, a.Name, a.DisplayName, string.Join(' ', tags));
        }
        return ids;
    }

    private static void LinkParents(SqliteConnection c, SqliteTransaction tx, IReadOnlyCollection<AssetInput> assets, Dictionary<(string, string), long> ids)
    {
        using var update = Prepare(c, tx, "UPDATE asset SET parent_id = $parent WHERE id = $id", "$parent", "$id");
        foreach (var a in assets)
        {
            if (a.Parent is { } p && ids.TryGetValue(p, out var parentId))
                RunNonQuery(update, parentId, ids[(a.Kind, a.Key)]);
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

    private static void Bind(SqliteCommand cmd, object?[] values)
    {
        for (var i = 0; i < values.Length; i++)
            cmd.Parameters[i].Value = values[i] ?? DBNull.Value;
    }

    private static object? Run(SqliteCommand cmd, params object?[] values)
    {
        Bind(cmd, values);
        return cmd.ExecuteScalar();
    }

    private static void RunNonQuery(SqliteCommand cmd, params object?[] values)
    {
        Bind(cmd, values);
        cmd.ExecuteNonQuery();
    }
}
