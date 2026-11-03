using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer;
using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.Tests.Game;

[Collection(GameCollection.Name)]
public class GameIndexerTests
{
    [Fact]
    public void Indexes_vehicles_with_dlc_and_display_name_then_skips_when_unchanged()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var session = GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });
        var db = AtlasDb.Open(dir.Sub("atlas.db"));
        var errors = new List<string>();

        var first = GameIndexer.Run(session, db, error: errors.Add, extractors: [new VehicleExtractor()]);
        var second = GameIndexer.Run(session, db, extractors: [new VehicleExtractor()]);

        Assert.False(first.Skipped);
        Assert.Empty(errors);
        Assert.True(second.Skipped);

        var q = new AssetQueries(db);
        var adder = q.Find(AssetKind.Vehicle, "adder");
        Assert.NotNull(adder);
        Assert.Equal(DlcNames.Base, adder.Dlc);
        Assert.Equal("Adder", adder.DisplayName);
        var dlcs = q.Dlcs().Select(d => d.Name).ToList();
        Assert.Equal(DlcNames.Base, dlcs[0]);
        Assert.True(dlcs.IndexOf("mpbeach") < dlcs.IndexOf("mpheist4"));
        Assert.True(dlcs.IndexOf("mpheist4") < dlcs.IndexOf(DlcNames.Update));

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    }
}
