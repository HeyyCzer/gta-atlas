using CodeWalker.GameFiles;
using GtaAtlas.Indexer;
using GtaAtlas.Indexer.Extractors;
using SharpDX;

namespace GtaAtlas.Tests.Indexer;

public class TagRulesTests
{
    [Fact]
    public void Permanent_ityps_come_from_level_meta_and_dlc_content()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <CDataFileMgr__ContentsOfDataFileXml>
              <dataFiles>
                <Item>
                  <filename>dlc_mpTuner:/%PLATFORM%/levels/gta5/props/tr_Prop_TR_Vehicles.ityp</filename>
                  <fileType>DLC_ITYP_REQUEST</fileType>
                </Item>
                <Item>
                  <filename>platform:/levels/gta5/props/roadside/v_fences.rpf</filename>
                  <fileType>RPF_FILE</fileType>
                </Item>
              </dataFiles>
              <filesToEnable>
                <Item>platform:/levels/gta5/props/ignored.ityp</Item>
              </filesToEnable>
            </CDataFileMgr__ContentsOfDataFileXml>
            """;
        const string meta = """
            <CDataFileMgr__ContentsOfDataFileXml>
              <includedXmlFiles>
                <Item><xi:include href="commoncrc:/data/common.meta" xpointer="xpointer(/dataFiles)"/></Item>
              </includedXmlFiles>
              <dataFiles>
                <Item><filename>platform:/levels/gta5/props/roadside/v_fences.ityp</filename><fileType>PERMANENT_ITYP_FILE</fileType></Item>
              </dataFiles>
            </CDataFileMgr__ContentsOfDataFileXml>
            """;

        Assert.Equal(["tr_prop_tr_vehicles"], PermanentTypes.Parse(xml));
        Assert.Equal(["v_fences"], PermanentTypes.Parse(meta));
        Assert.Empty(PermanentTypes.Parse("<broken"));
    }

    [Theory]
    [InlineData(0.3f, "size: small")]
    [InlineData(2f, "size: medium")]
    [InlineData(12f, "size: large")]
    [InlineData(80f, "size: huge")]
    [InlineData(0f, null)]
    public void Size_follows_largest_side(float side, string? tag) =>
        Assert.Equal(tag, ArchetypeExtractor.SizeTag(new Vector3(Math.Min(side, 0.1f), side, Math.Min(side, 0.1f))));

    [Theory]
    [InlineData("dt1_lod", true)]
    [InlineData("cs1_08_slod2", true)]
    [InlineData("lod_ch1_04", true)]
    [InlineData("prop_lodge_01", false)]
    [InlineData("prop_bench_01a", false)]
    public void Lod_names(string name, bool lod) => Assert.Equal(lod, ArchetypeExtractor.IsLodName(name));

    [Fact]
    public void Prop_tags_skip_technical_details()
    {
        var tags = ArchetypeExtractor.PropTags("prop_gate", rage__fwArchetypeDef__eAssetType.ASSET_TYPE_FRAGMENT, timed: false, animated: true,
            new Vector3(-1, -0.2f, 0), new Vector3(1, 0.2f, 2));
        Assert.Equal(["fragment", "animated", "size: medium"], tags);
        Assert.Equal(["size: small"], ArchetypeExtractor.PropTags("prop_cup", rage__fwArchetypeDef__eAssetType.ASSET_TYPE_DRAWABLE, false, false,
            new Vector3(-0.1f), new Vector3(0.1f)));
    }

    [Theory]
    [InlineData("CIVMALE", "Civilian male")]
    [InlineData("civfemale", "Civilian female")]
    [InlineData("ANIMAL", "Animal")]
    [InlineData("cop", "Cop")]
    [InlineData("swat", "SWAT")]
    [InlineData("hash_A49E591C", null)]
    [InlineData(null, null)]
    public void Ped_type_tag(string? raw, string? tag) => Assert.Equal(tag, PedExtractor.PedTypeTag(raw));
}
