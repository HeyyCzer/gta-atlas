using GtaAtlas.App.Browsing;
using GtaAtlas.Data;

namespace GtaAtlas.Tests.App;

public sealed class BrowseStateTests
{
    [Fact]
    public void Category_sets_kind_and_its_own_tags()
    {
        var q = new BrowseState().WithCategory(Category.Ipls).ToQuery();

        Assert.Equal(AssetKind.Ymap, q.Kind);
        Assert.Equal(["ipl"], q.Tags);
        Assert.Null(q.Text);
    }

    [Fact]
    public void Gender_applies_only_to_clothes()
    {
        var clothes = new BrowseState().WithCategory(Category.Clothes) with { Gender = Gender.Female };
        Assert.Equal(["Female"], clothes.ToQuery().Tags);

        var props = clothes.WithCategory(Category.Props);
        Assert.Equal(Gender.Any, props.Gender);
        Assert.Empty(props.ToQuery().Tags);
    }

    [Fact]
    public void Chips_toggle_and_reset_on_category_change()
    {
        var s = new BrowseState().WithCategory(Category.Vehicles).ToggleTag("Grotti").ToggleTag("Super");
        Assert.Equal(["Grotti", "Super"], s.ToQuery().Tags);

        s = s.ToggleTag("Grotti");
        Assert.Equal(["Super"], s.ToQuery().Tags);
        Assert.Empty(s.WithCategory(Category.Peds).Tags);
    }

    [Fact]
    public void Keeps_text_and_dlc_across_categories()
    {
        var s = new BrowseState { Text = "  bench ", Dlc = "mp2026_01" }.WithCategory(Category.All);
        var q = s.ToQuery(offset: 200, limit: 50);

        Assert.Equal("bench", q.Text);
        Assert.Equal("mp2026_01", q.Dlc);
        Assert.Null(q.Kind);
        Assert.Equal((200, 50), (q.Offset, q.Limit));
    }

    [Fact]
    public void Favorites_category_filters_favorites_of_any_kind()
    {
        var q = new BrowseState().WithCategory(Category.Favorites).ToQuery();
        Assert.True(q.FavoritesOnly);
        Assert.Null(q.Kind);
    }
}

public sealed class ResultPagerTests
{
    private static readonly List<AssetSummary> Items =
        Enumerable.Range(0, 95).Select(i => new AssetSummary(i + 1, AssetKind.Prop, $"p{i:000}", $"p{i:000}", (uint)i, null, "base")).ToList();

    private readonly List<(int Offset, int Limit)> _calls = [];

    private SearchResult Search(SearchQuery q)
    {
        _calls.Add((q.Offset, q.Limit));
        return new SearchResult(Items.Skip(q.Offset).Take(q.Limit).ToList(), Items.Count);
    }

    [Fact]
    public void Loads_first_page_with_total()
    {
        var pager = ResultPager.Load(Search, new SearchQuery(), pageSize: 10);

        Assert.Equal(95, pager.Total);
        Assert.Equal([(0, 10)], _calls);
        Assert.Equal("p009", pager[9]!.Name);
        Assert.Single(_calls);
    }

    [Fact]
    public void Fetches_pages_on_demand_and_bounds_checks()
    {
        var pager = ResultPager.Load(Search, new SearchQuery(), pageSize: 10);

        Assert.Equal("p094", pager[94]!.Name);
        Assert.Equal((90, 10), _calls[^1]);
        Assert.Null(pager[95]);
        Assert.Null(pager[-1]);
        Assert.Equal(2, _calls.Count);
    }

    [Fact]
    public void Evicts_least_recently_used_pages()
    {
        var pager = ResultPager.Load(Search, new SearchQuery(), pageSize: 10, maxPages: 2);

        _ = pager[15]; // pages 0,1
        _ = pager[3];  // touch 0
        _ = pager[25]; // evicts 1
        Assert.Equal(2, pager.LoadedPages);

        _calls.Clear();
        _ = pager[3];
        Assert.Empty(_calls);
        _ = pager[15];
        Assert.Equal([(10, 10)], _calls);
    }

    [Fact]
    public void Finds_loaded_asset_position()
    {
        var pager = ResultPager.Load(Search, new SearchQuery(), pageSize: 10);
        Assert.Equal(4, pager.IndexOf(5));
        Assert.Equal(-1, pager.IndexOf(50)); // not loaded yet
    }

    [Fact]
    public void Keeps_query_filters_on_every_page()
    {
        SearchQuery? last = null;
        var pager = ResultPager.Load(q => { last = q; return Search(q); }, new SearchQuery { Kind = AssetKind.Prop, Text = "x" }, pageSize: 10);
        _ = pager[42];

        Assert.Equal(AssetKind.Prop, last!.Kind);
        Assert.Equal("x", last.Text);
        Assert.Equal(40, last.Offset);
    }
}

public sealed class AssetTextTests
{
    private static AssetSummary Summary(string kind, string key, string name, string? display = null) => new(1, kind, key, name, 1, display, "base");

    [Fact]
    public void Clip_subtitle_and_copy_include_dictionary()
    {
        var clip = Summary(AssetKind.Anim, "amb@world_human_seat/idle_a", "idle_a");

        Assert.Equal("amb@world_human_seat", AssetText.Subtitle(clip));
        Assert.Equal("amb@world_human_seat idle_a", AssetText.CopyText(clip));
    }

    [Fact]
    public void Display_name_is_the_subtitle()
    {
        var car = Summary(AssetKind.Vehicle, "velenogt", "velenogt", "Veleno GT");
        Assert.Equal("Veleno GT", AssetText.Subtitle(car));
        Assert.Equal("velenogt", AssetText.CopyText(car));
        Assert.Null(AssetText.Subtitle(Summary(AssetKind.Prop, "bench", "bench")));
    }

    [Fact]
    public void Flattens_data_json()
    {
        var fields = AssetText.DataFields(
            """{"make":"Grotti","seats":2,"lod":12.5,"flags":null,"bbMin":[-1,-2.25,0],"extra":{"a":true},"textures":[{"name":"Red","index":0},{"name":null,"index":1}]}""");

        Assert.Equal(
        [
            new DetailField("make", "Grotti"),
            new DetailField("seats", "2"),
            new DetailField("lod", "12.5"),
            new DetailField("bbMin", "-1, -2.25, 0"),
            new DetailField("extra.a", "true"),
            new DetailField("textures[0]", "name: Red, index: 0"),
            new DetailField("textures[1]", "index: 1"),
        ], fields);
    }

    [Fact]
    public void No_data_no_fields() => Assert.Empty(AssetText.DataFields(null));
}
