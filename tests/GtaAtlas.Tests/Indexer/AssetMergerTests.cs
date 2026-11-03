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
    public void Overlay_definitions_do_not_introduce_assets()
    {
        var a = Assert.Single(AssetMerger.Merge(
            [Raw("mpbeach", "patch", new { v = 1 }) with { Overlay = true }, Raw("mpheist4", "real", new { v = 2 })], Dlcs));

        Assert.Equal("mpheist4", a.Dlc);
        Assert.Equal(2, a.Sources.Count);
    }

    [Fact]
    public void Overlay_only_asset_falls_back_to_first_definition()
    {
        var a = Assert.Single(AssetMerger.Merge([Raw("mpheist4", "b") with { Overlay = true }, Raw("mpbeach", "a") with { Overlay = true }], Dlcs));
        Assert.Equal("mpbeach", a.Dlc);
    }

    [Fact]
    public void Unknown_dlc_sorts_last()
    {
        var a = Assert.Single(AssetMerger.Merge([Raw("mystery", "m"), Raw(DlcNames.Update, "u")], Dlcs));
        Assert.Equal(DlcNames.Update, a.Dlc);
    }
}
