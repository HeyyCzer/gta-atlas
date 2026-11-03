using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Indexer;

namespace GtaAtlas.Tests.Game;

[Collection(GameCollection.Name)]
public class PermanentTypesTests
{
    [Fact]
    public void Prop_ytyps_are_permanent_and_map_metadata_is_not()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var session = GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });
        var types = PermanentTypes.Find(GameArchive.Scan(session));

        // Base level meta, a DLC content.xml, then streamed map sections and DLC interiors.
        Assert.True(types.Contains(@"x64i.rpf\levels\gta5\props\roadside\v_fences.rpf\v_fences.ytyp"));
        Assert.True(types.Contains(@"update\x64\dlcpacks\mptuner\dlc.rpf\x64\levels\gta5\props\prop_tr_vehicles.rpf\tr_prop_tr_vehicles.ytyp"));
        Assert.False(types.Contains(@"x64m.rpf\levels\gta5\_cityw\airport_01\airport_metadata.rpf\airport_metadata_001.ytyp"));
        Assert.False(types.Contains(@"update\x64\dlcpacks\mpapartment\dlc.rpf\x64\levels\gta5\interiors\int_mp_h_01.rpf\apa_int_mp_h_01.ytyp"));
        Assert.InRange(types.Count, 500, 5000);
    }
}
