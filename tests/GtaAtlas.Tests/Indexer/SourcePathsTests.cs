using GtaAtlas.Indexer;

namespace GtaAtlas.Tests.Indexer;

public class SourcePathsTests
{
    [Theory]
    [InlineData(@"update\x64\dlcpacks\mpheist4\dlc.rpf\x64\levels\gta5\props.ytyp", "mpheist4")]
    [InlineData(@"x64w.rpf\dlcpacks\mpbeach\dlc.rpf\x64\data\peds.ymt", "mpbeach")]
    [InlineData(@"x64\dlcpacks\patchday1ng\dlc.rpf\x.ytyp", "patchday1ng")]
    [InlineData(@"Update\X64\DLCPacks\MP2026_01\dlc.rpf\common\data\vehicles.meta", "mp2026_01")]
    [InlineData(@"update\update.rpf\dlc_patch\mpheist4\x64\levels\a.ytyp", "mpheist4")]
    [InlineData(@"update\update.rpf\x64\data\peds.ymt", "update")]
    [InlineData(@"x64a.rpf\data\peds.ymt", "base")]
    [InlineData(@"common.rpf\data\levels\gta5\vehicles.meta", "base")]
    public void Resolves_owning_dlc(string path, string dlc) =>
        Assert.Equal(dlc, SourcePaths.DlcOf(path));

    [Theory]
    [InlineData(@"mods\update\update.rpf", true)]
    [InlineData(@"update\update.rpf", false)]
    public void Detects_mods_folder(string path, bool isMod) =>
        Assert.Equal(isMod, SourcePaths.IsMod(path));
}
