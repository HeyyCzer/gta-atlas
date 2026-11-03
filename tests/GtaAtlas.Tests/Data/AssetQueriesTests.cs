using GtaAtlas.Data;

namespace GtaAtlas.Tests.Data;

public sealed class AssetQueriesTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly AtlasDb _db;
    private readonly AssetQueries _q;

    private static readonly List<DlcInput> Dlcs = [new(DlcNames.Base, 0), new("mp2026_01", 1), new(DlcNames.Update, 2)];

    public AssetQueriesTests()
    {
        _db = AtlasDb.Open(_dir.Sub("atlas.db"));
        _q = new AssetQueries(_db);
        IndexWriter.Rebuild(_db, Dlcs, Sample());
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        _dir.Dispose();
    }

    private static List<AssetInput> Sample() =>
    [
        Asset(AssetKind.Vehicle, "velenogt", 3316510914, "mp2026_01", "Veleno GT", ["Grotti", "Super"]),
        Asset(AssetKind.Vehicle, "laufer", 1, "mp2026_01", "Läufer", ["Benefactor", "Vans"]),
        Asset(AssetKind.Prop, "prop_bench_01a", 2, DlcNames.Base, null, ["props_1"]),
        Asset(AssetKind.Prop, "v_med_bench1", 3, DlcNames.Base, null, ["props_2"]),
        Asset(AssetKind.Prop, "bench", 4, "mp2026_01", null, []),
        Asset(AssetKind.AnimDict, "amb@world_human_seat", 5, DlcNames.Base, null, []),
        Asset(AssetKind.Anim, "amb@world_human_seat/idle_a", 6, DlcNames.Base, null, []) with
        {
            Name = "idle_a",
            Parent = (AssetKind.AnimDict, "amb@world_human_seat"),
        },
    ];

    private static AssetInput Asset(string kind, string name, uint hash, string dlc, string? display, string[] tags) => new()
    {
        Kind = kind,
        Key = name,
        Name = name,
        Hash = hash,
        Dlc = dlc,
        DisplayName = display,
        Tags = tags,
        Data = "{\"x\":1}",
        Sources = [new AssetSource(dlc, $@"{dlc}\{name}.file")],
    };

    [Fact]
    public void Migrates_to_latest_schema() => Assert.True(_db.SchemaVersion >= 1);

    [Fact]
    public void Substring_search_ranks_exact_then_prefix()
    {
        var r = _q.Search(new SearchQuery { Text = "bench", Kind = AssetKind.Prop });

        Assert.Equal(3, r.Total);
        // exact match, then shorter names first
        Assert.Equal(["bench", "v_med_bench1", "prop_bench_01a"], r.Items.Select(i => i.Name));
    }

    [Fact]
    public void Searches_display_names_and_tags()
    {
        Assert.Equal("velenogt", Assert.Single(_q.Search(new SearchQuery { Text = "veleno" }).Items).Name);
        Assert.Equal("velenogt", Assert.Single(_q.Search(new SearchQuery { Text = "grotti" }).Items).Name);
        Assert.Equal("laufer", Assert.Single(_q.Search(new SearchQuery { Text = "läufer" }).Items).Name);
    }

    [Theory]
    [InlineData("3316510914")]
    [InlineData("-978456382")]
    [InlineData("0xC5ADF0C2")]
    public void Finds_by_hash(string text) =>
        Assert.Equal("velenogt", Assert.Single(_q.Search(new SearchQuery { Text = text }).Items).Name);

    [Fact]
    public void Short_terms_fall_back_to_like()
    {
        var r = _q.Search(new SearchQuery { Text = "gt", Kind = AssetKind.Vehicle });
        Assert.Equal("velenogt", Assert.Single(r.Items).Name);
    }

    [Fact]
    public void Like_wildcards_in_input_are_literal()
    {
        Assert.Equal(0, _q.Search(new SearchQuery { Text = "%" }).Total);
        // '_' matches only names that contain an underscore, not every name
        Assert.Equal(4, _q.Search(new SearchQuery { Text = "_" }).Total);
    }

    [Fact]
    public void Filters_by_kind_dlc_and_tags()
    {
        Assert.Equal(2, _q.Search(new SearchQuery { Kind = AssetKind.Vehicle }).Total);
        Assert.Equal(3, _q.Search(new SearchQuery { Dlc = "mp2026_01" }).Total);
        Assert.Equal("laufer", Assert.Single(_q.Search(new SearchQuery { Tags = ["Vans"] }).Items).Name);
        Assert.Equal(0, _q.Search(new SearchQuery { Tags = ["Vans", "Super"] }).Total);
    }

    [Fact]
    public void Pages_results()
    {
        var page = _q.Search(new SearchQuery { Kind = AssetKind.Prop, Offset = 1, Limit = 1 });
        Assert.Equal(3, page.Total);
        Assert.Equal("prop_bench_01a", Assert.Single(page.Items).Name);
    }

    [Fact]
    public void Links_children_to_parent()
    {
        var dict = _q.Find(AssetKind.AnimDict, "amb@world_human_seat")!;
        var children = _q.Search(new SearchQuery { ParentId = dict.Id });

        var clip = Assert.Single(children.Items);
        Assert.Equal("idle_a", clip.Name);
        Assert.Equal(dict.Id, _q.Get(clip.Id)!.Parent!.Id);
    }

    [Fact]
    public void Detail_includes_tags_sources_and_data()
    {
        var d = _q.Get(_q.Find(AssetKind.Vehicle, "velenogt")!.Id)!;

        Assert.Equal(["Grotti", "Super"], d.Tags);
        Assert.Equal("mp2026_01", Assert.Single(d.Sources).Dlc);
        Assert.Equal("{\"x\":1}", d.Data);
        Assert.Equal(3316510914u, d.Asset.Hash);
    }

    [Fact]
    public void Counts_per_dlc_in_load_order()
    {
        var dlcs = _q.Dlcs();

        Assert.Equal(["base", "mp2026_01", "update"], dlcs.Select(d => d.Name));
        Assert.Equal(2, dlcs[1].CountsByKind[AssetKind.Vehicle]);
        Assert.Empty(dlcs[2].CountsByKind);
    }

    [Fact]
    public void Favorites_survive_reindex()
    {
        _q.SetFavorite(AssetKind.Vehicle, "velenogt", true);
        IndexWriter.Rebuild(_db, Dlcs, Sample());

        Assert.True(_q.IsFavorite(AssetKind.Vehicle, "velenogt"));
        Assert.Equal("velenogt", Assert.Single(_q.Search(new SearchQuery { FavoritesOnly = true }).Items).Name);

        _q.SetFavorite(AssetKind.Vehicle, "velenogt", false);
        Assert.Equal(0, _q.Search(new SearchQuery { FavoritesOnly = true }).Total);
    }

    [Fact]
    public void Reindex_replaces_previous_content()
    {
        IndexWriter.Rebuild(_db, Dlcs, Sample().Take(1).ToList());

        Assert.Equal(1, _q.Search(new SearchQuery()).Total);
        Assert.Equal(0, _q.Search(new SearchQuery { Text = "bench" }).Total);
    }

    [Fact]
    public void Top_tags_by_frequency()
    {
        var tags = _q.TopTags(AssetKind.Vehicle);
        Assert.Equal(4, tags.Count);
        Assert.All(tags, t => Assert.Equal(1, t.Count));
    }
}
