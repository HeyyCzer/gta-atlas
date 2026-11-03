using Dapper;
using Microsoft.Data.Sqlite;

namespace GtaAtlas.Data;

/// <summary>Opens the atlas SQLite database and keeps its schema current.</summary>
public sealed class AtlasDb
{
    private readonly string _connectionString;

    public AtlasDb(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            Pooling = true,
        }.ToString();
    }

    public string Path { get; }

    public static AtlasDb Open(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (dir is not null)
            Directory.CreateDirectory(dir);

        var db = new AtlasDb(path);
        db.Migrate();
        return db;
    }

    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;");
        return connection;
    }

    /// <summary>Connection tuned for a one-shot build of a throwaway file: no journal, no fsync.</summary>
    internal SqliteConnection ConnectForBulkLoad()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        connection.Execute("PRAGMA journal_mode = OFF; PRAGMA synchronous = OFF; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -262144;");
        return connection;
    }

    public int SchemaVersion
    {
        get
        {
            using var c = Connect();
            return c.ExecuteScalar<int>("PRAGMA user_version");
        }
    }

    private void Migrate()
    {
        using var c = Connect();
        c.CreateFunction("natural_key", (string name) => NaturalSort.Key(name), isDeterministic: true);
        var version = c.ExecuteScalar<int>("PRAGMA user_version");
        for (var i = version; i < Schema.Migrations.Length; i++)
        {
            using var tx = c.BeginTransaction();
            c.Execute(Schema.Migrations[i], transaction: tx);
            c.Execute($"PRAGMA user_version = {i + 1}", transaction: tx);
            tx.Commit();
        }
    }

    public string? GetMeta(string key)
    {
        using var c = Connect();
        return c.ExecuteScalar<string?>("SELECT value FROM meta WHERE key = @key", new { key });
    }

    public void SetMeta(string key, string value)
    {
        using var c = Connect();
        c.Execute("INSERT INTO meta(key, value) VALUES (@key, @value) ON CONFLICT(key) DO UPDATE SET value = excluded.value",
            new { key, value });
    }
}
