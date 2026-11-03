using System.Text.Json;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

namespace GtaAtlas.Tests.Indexer;

public class AssetMergerTests
{
    private static readonly List<DlcInput> Dlcs =
    [
        new(DlcNames.Base, 0),
        new("mpbeach", 1),
        new("mpheist4", 2),
        new(DlcNames.Update, 3),
    ];

    private static RawAsset Raw(string dlc, string path, object? data = null, string? display = null) => new()
    {
        Kind = AssetKind.Prop,
        Key = "prop_a",
        Name = "prop_a",
        Hash = 1,
        Path = path,
        Dlc = dlc,
        Data = data,
        DisplayName = display,
        Tags = [dlc],
    };

    [Fact]
    public void Attributes_to_first_dlc_and_keeps_latest_data()
    {
        var merged = AssetMerger.Merge(
        [
            Raw("mpheist4", @"b\heist.ytyp", new { lod = 2 }),
            Raw("mpbeach", @"a\beach.ytyp", new { lod = 1 }, "Beach name"),
        ], Dlcs);

        var a = Assert.Single(merged);
        Assert.Equal("mpbeach", a.Dlc);
        Assert.Equal(2, JsonDocument.Parse(a.Data!).RootElement.GetProperty("lod").GetInt32());
        Assert.Equal(["mpheist4"], a.Tags);
        Assert.Equal("Beach name", a.DisplayName);
        Assert.Equal(["mpbeach", "mpheist4"], a.Sources.Select(s => s.Dlc));
    }

    [Fact]
    public void Keeps_distinct_keys_and_kinds_apart()
    {
        var merged = AssetMerger.Merge(
        [
            Raw(DlcNames.Base, "x"),
            Raw(DlcNames.Base, "y") with { Key = "prop_b", Name = "prop_b" },
            Raw(DlcNames.Base, "z") with { Kind = AssetKind.Mlo },
        ], Dlcs);

        Assert.Equal(3, merged.Count);
    }

    [Fact]
    public void Duplicate_paths_are_collapsed()
    {
        var a = Assert.Single(AssetMerger.Merge([Raw(DlcNames.Base, "x"), Raw(DlcNames.Base, "x")], Dlcs));
        Assert.Single(a.Sources);
    }

    [Fact]
    public void Unknown_dlc_sorts_last()
    {
        var a = Assert.Single(AssetMerger.Merge([Raw("mystery", "m"), Raw(DlcNames.Update, "u")], Dlcs));
        Assert.Equal(DlcNames.Update, a.Dlc);
    }
}
