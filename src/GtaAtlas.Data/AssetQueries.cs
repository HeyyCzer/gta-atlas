using System.Text;
using Dapper;

namespace GtaAtlas.Data;

public sealed class AssetQueries(AtlasDb db)
{
    /// <summary>Named assets first, then hash-only names; matches the expression indexes in <see cref="Schema"/>.</summary>
    private const string BrowseOrder = "ORDER BY a.name GLOB '0x*', a.name";

    private const string SummaryColumns = "a.id AS Id, a.kind AS Kind, a.key AS Key, a.name AS Name, a.hash AS Hash, a.display_name AS DisplayName, d.name AS Dlc";

    public SearchResult Search(SearchQuery q)
    {
        var where = new StringBuilder("1 = 1");
        var p = new DynamicParameters();
        var text = q.Text?.Trim();

        if (q.Kind is not null)
        {
            where.Append(" AND a.kind = @kind");
            p.Add("kind", q.Kind);
        }
        if (q.Dlc is not null)
        {
            where.Append(" AND a.dlc_id = (SELECT id FROM dlc WHERE name = @dlc)");
            p.Add("dlc", q.Dlc);
        }
        if (q.ParentId is not null)
        {
            where.Append(" AND a.parent_id = @parent");
            p.Add("parent", q.ParentId);
        }
        for (var i = 0; i < q.Tags.Count; i++)
        {
            where.Append($" AND EXISTS (SELECT 1 FROM asset_tag t WHERE t.asset_id = a.id AND t.tag = @tag{i})");
            p.Add($"tag{i}", q.Tags[i]);
        }
        if (q.FavoritesOnly)
            // Drive from the (small) favorite table through ux_asset_kind_key instead of probing it per asset.
            where.Append(" AND a.id IN (SELECT fa.id FROM favorite f JOIN asset fa ON fa.kind = f.kind AND fa.key = f.key)");
        if (!string.IsNullOrEmpty(text))
            where.Append(" AND (").Append(TextCondition(text, p)).Append(')');

        var from = $"FROM asset a WHERE {where}";
        var order = string.IsNullOrEmpty(text)
            ? BrowseOrder
            : "ORDER BY (a.name = @exact COLLATE NOCASE) DESC, (a.name LIKE @prefix) DESC, length(a.name), a.name";
        p.Add("exact", text);
        p.Add("prefix", LikeEscape(text ?? "") + "%");
        p.Add("limit", q.Limit);
        p.Add("offset", q.Offset);

        using var c = db.Connect();
        // Count and page without the dlc join so plain filters stay index-only; DLC names are joined onto the page.
        var total = c.ExecuteScalar<int>($"SELECT COUNT(*) {from}", p);
        var rows = c.Query<SummaryRow>(
            $"""
            SELECT {SummaryColumns}, (SELECT group_concat(t.tag, char(31)) FROM asset_tag t WHERE t.asset_id = a.id) AS TagList
            FROM (SELECT a.* {from} {order} LIMIT @limit OFFSET @offset) a JOIN dlc d ON d.id = a.dlc_id {order}
            """, p);
        return new SearchResult(rows.Select(r => r.ToSummary()).ToList(), total);
    }

    /// <summary>
    /// Hash match (any notation) OR every word matching name/display name/tags.
    /// Words of 3+ chars use the trigram FTS index; shorter ones fall back to LIKE.
    /// </summary>
    private static string TextCondition(string text, DynamicParameters p)
    {
        var parts = new List<string>();
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var fts = words.Where(w => w.Length >= 3).Select(w => "\"" + w.Replace("\"", "\"\"") + "\"").ToList();
        if (fts.Count > 0)
        {
            parts.Add("a.id IN (SELECT rowid FROM asset_fts WHERE asset_fts MATCH @fts)");
            p.Add("fts", string.Join(" AND ", fts));
        }

        var shortWords = words.Where(w => w.Length < 3).ToList();
        for (var i = 0; i < shortWords.Count; i++)
        {
            parts.Add($"(a.name LIKE @short{i} ESCAPE '\\' OR a.display_name LIKE @short{i} ESCAPE '\\')");
            p.Add($"short{i}", "%" + LikeEscape(shortWords[i]) + "%");
        }

        var textMatch = parts.Count > 0 ? string.Join(" AND ", parts) : "0";
        if (!HashText.TryParse(text, out var hash))
            return textMatch;

        p.Add("hash", (long)hash);
        return $"a.hash = @hash OR ({textMatch})";
    }

    private static string LikeEscape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    public AssetDetail? Get(long id)
    {
        using var c = db.Connect();
        var row = c.QuerySingleOrDefault<SummaryRow>(
            $"SELECT {SummaryColumns}, a.data AS Data, a.parent_id AS ParentId FROM asset a JOIN dlc d ON d.id = a.dlc_id WHERE a.id = @id", new { id });
        if (row is null)
            return null;

        var tags = c.Query<string>("SELECT tag FROM asset_tag WHERE asset_id = @id ORDER BY tag", new { id }).ToList();
        var sources = c.Query<(string Dlc, string Path)>(
                """
                SELECT d.name, f.path FROM asset_source s
                JOIN source_file f ON f.id = s.file_id JOIN dlc d ON d.id = f.dlc_id
                WHERE s.asset_id = @id ORDER BY d.order_index, f.path
                """, new { id })
            .Select(s => new AssetSource(s.Dlc, s.Path)).ToList();
        var parent = row.ParentId is { } pid ? Get(pid)?.Asset : null;
        return new AssetDetail(row.ToSummary(), row.Data, tags, sources, parent);
    }

    public AssetSummary? Find(string kind, string key)
    {
        using var c = db.Connect();
        return c.QuerySingleOrDefault<SummaryRow>(
            $"SELECT {SummaryColumns} FROM asset a JOIN dlc d ON d.id = a.dlc_id WHERE a.kind = @kind AND a.key = @key", new { kind, key })?.ToSummary();
    }

    public IReadOnlyList<DlcSummary> Dlcs()
    {
        using var c = db.Connect();
        var counts = c.Query<(long DlcId, string Kind, int Count)>("SELECT dlc_id, kind, COUNT(*) FROM asset GROUP BY dlc_id, kind")
            .GroupBy(x => x.DlcId)
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, int>)g.ToDictionary(x => x.Kind, x => x.Count));
        return c.Query<(long Id, string Name, int OrderIndex, string? Title, string? Timestamp)>(
                "SELECT id, name, order_index, title, timestamp FROM dlc ORDER BY order_index")
            .Select(d => new DlcSummary(d.Id, d.Name, d.OrderIndex, d.Title, d.Timestamp,
                counts.GetValueOrDefault(d.Id) ?? new Dictionary<string, int>()))
            .ToList();
    }

    public IReadOnlyDictionary<string, int> CountsByKind()
    {
        using var c = db.Connect();
        return c.Query<(string Kind, int Count)>("SELECT kind, COUNT(*) FROM asset GROUP BY kind").ToDictionary(x => x.Kind, x => x.Count);
    }

    /// <summary>Most common tags for a kind, for filter chips.</summary>
    public IReadOnlyList<(string Tag, int Count)> TopTags(string kind, int limit = 20)
    {
        using var c = db.Connect();
        return c.Query<(string, int)>(
            """
            SELECT t.tag, COUNT(*) AS n FROM asset_tag t JOIN asset a ON a.id = t.asset_id
            WHERE a.kind = @kind GROUP BY t.tag ORDER BY n DESC, t.tag LIMIT @limit
            """, new { kind, limit }).ToList();
    }

    public void SetFavorite(string kind, string key, bool favorite)
    {
        using var c = db.Connect();
        c.Execute(favorite
            ? "INSERT OR IGNORE INTO favorite(kind, key) VALUES (@kind, @key)"
            : "DELETE FROM favorite WHERE kind = @kind AND key = @key", new { kind, key });
    }

    public bool IsFavorite(string kind, string key)
    {
        using var c = db.Connect();
        return c.ExecuteScalar<int>("SELECT COUNT(*) FROM favorite WHERE kind = @kind AND key = @key", new { kind, key }) > 0;
    }

    private sealed class SummaryRow
    {
        public long Id { get; init; }
        public string Kind { get; init; } = "";
        public string Key { get; init; } = "";
        public string Name { get; init; } = "";
        public long Hash { get; init; }
        public string? DisplayName { get; init; }
        public string Dlc { get; init; } = "";
        public string? Data { get; init; }
        public long? ParentId { get; init; }
        public string? TagList { get; init; }

        public AssetSummary ToSummary() => new(Id, Kind, Key, Name, unchecked((uint)Hash), DisplayName, Dlc)
        {
            Tags = TagList?.Split('') ?? [],
        };
    }
}
