using GtaAtlas.Indexer;
using GtaAtlas.Indexer.Clothing;

namespace GtaAtlas.Tests.Indexer;

public class ShopMetaTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <ShopPedApparel>
          <pedName>mp_m_freemode_01</pedName>
          <dlcName>mp_m_2023_02</dlcName>
          <fullDlcName>mp_m_freemode_01_mp_m_2023_02</fullDlcName>
          <pedOutfits>
            <Item><textLabel>CLO_OUTFIT</textLabel></Item>
          </pedOutfits>
          <pedComponents>
            <!-- FEET: Black Cloth Clogs -->
            <Item>
              <textLabel>CLO_X7M_F_2_0</textLabel>
              <uniqueNameHash>DLC_MP_X23_M_FEET_2_0</uniqueNameHash>
              <drawableIndex value="0" />
              <localDrawableIndex value="2" />
              <eCompType>PV_COMP_FEET</eCompType>
              <textureIndex value="0" />
            </Item>
            <Item>
              <textLabel />
              <localDrawableIndex value="0" />
              <eCompType>PV_COMP_BERD</eCompType>
              <textureIndex value="3" />
            </Item>
          </pedComponents>
          <pedProps>
            <!-- PRWRIST: Blue Bangles -->
            <Item>
              <textLabel>CLO_H4M_PRW_0_1</textLabel>
              <localPropIndex value="0" />
              <eAnchorPoint>ANCHOR_RIGHT_WRIST</eAnchorPoint>
              <textureIndex value="1" />
            </Item>
          </pedProps>
        </ShopPedApparel>
        """;

    [Fact]
    public void Parses_components_and_props_with_dev_names()
    {
        var meta = ShopMeta.Parse(Xml)!;

        Assert.Equal("mp_m_freemode_01", meta.PedName);
        Assert.Equal("mp_m_freemode_01_mp_m_2023_02", meta.FullDlcName);
        Assert.Equal(
        [
            new ShopItem(false, 6, 2, 0, "CLO_X7M_F_2_0", "Black Cloth Clogs", "DLC_MP_X23_M_FEET_2_0"),
            new ShopItem(false, 1, 0, 3, null, null, null), // comment belongs to the previous item only
            new ShopItem(true, 7, 0, 1, "CLO_H4M_PRW_0_1", "Blue Bangles", null),
        ], meta.Items);
    }

    [Fact]
    public void Ignores_other_shop_metas() =>
        Assert.Null(ShopMeta.Parse("<ShopWeaponData><x/></ShopWeaponData>"));

    [Theory]
    [InlineData("PV_COMP_FEET", false, 6)]
    [InlineData("PV_COMP_JBIB", false, 11)]
    [InlineData("ANCHOR_RIGHT_WRIST", true, 7)]
    [InlineData("ANCHOR_HEAD", true, 0)]
    [InlineData("PV_COMP_NOPE", false, -1)]
    [InlineData(null, false, -1)]
    public void Parses_slots(string? value, bool prop, int slot) =>
        Assert.Equal(slot, ClothingSlots.ParseSlot(value, prop));
}

public class ClothingNumberingTests
{
    private static readonly Dictionary<string, int> Order = new() { ["base"] = 0, ["patch1"] = 1, ["mpbeach"] = 2, ["mpheist"] = 3, ["patch9"] = 9 };

    private static ClothingCollection Col(string name, string dlc, int shoes, bool overlay = false, int hats = 0) => new()
    {
        Name = name,
        Ped = "mp_m_freemode_01",
        Dlc = dlc,
        Path = $@"{dlc}\{name}.ymt",
        Overlay = overlay,
        Components = new Dictionary<int, IReadOnlyList<int>> { [6] = Enumerable.Repeat(2, shoes).ToList() },
        Props = hats == 0 ? new Dictionary<int, IReadOnlyList<int>>() : new Dictionary<int, IReadOnlyList<int>> { [0] = Enumerable.Repeat(1, hats).ToList() },
    };

    private static Dictionary<string, (int Start, int Count)> Shoes(IEnumerable<ClothingCollection> files) =>
        ClothingNumbering.Assign(files, d => Order[d])
            .Where(d => !d.Prop && d.Slot == 6)
            .GroupBy(d => d.Collection.Name)
            .ToDictionary(g => g.Key, g => (g.Min(d => d.GlobalIndex), g.Count()));

    [Fact]
    public void Base_first_then_collections_by_load_order()
    {
        var shoes = Shoes([Col("mp_m_freemode_01_heist", "mpheist", 3), Col("mp_m_freemode_01_beach", "mpbeach", 2), Col("mp_m_freemode_01", "base", 16)]);

        Assert.Equal((0, 16), shoes["mp_m_freemode_01"]);
        Assert.Equal((16, 2), shoes["mp_m_freemode_01_beach"]);
        Assert.Equal((18, 3), shoes["mp_m_freemode_01_heist"]);
    }

    [Fact]
    public void Reshipped_collection_keeps_position_and_uses_newest_contents()
    {
        var shoes = Shoes(
        [
            Col("mp_m_freemode_01", "base", 16),
            Col("mp_m_freemode_01_beach", "mpbeach", 2),
            Col("mp_m_freemode_01_beach", "patch9", 4, overlay: true),
            Col("mp_m_freemode_01_heist", "mpheist", 3),
        ]);

        Assert.Equal((16, 4), shoes["mp_m_freemode_01_beach"]);
        Assert.Equal((20, 3), shoes["mp_m_freemode_01_heist"]);
    }

    [Fact]
    public void Overlay_copy_in_earlier_pack_does_not_move_collection()
    {
        // e.g. patchday13ng (low setup order) overlays mpbiker's collection
        var numbered = ClothingNumbering.Assign(
        [
            Col("mp_m_freemode_01", "base", 16),
            Col("mp_m_freemode_01_heist", "patch1", 3, overlay: true),
            Col("mp_m_freemode_01_beach", "mpbeach", 2),
            Col("mp_m_freemode_01_heist", "mpheist", 3),
        ], d => Order[d]);

        var heist = numbered.Where(d => d.Collection.Name == "mp_m_freemode_01_heist").ToList();
        Assert.Equal(18, heist.Min(d => d.GlobalIndex));
        Assert.All(heist, d => Assert.Equal("mpheist", d.Collection.Dlc));
    }

    [Fact]
    public void Collections_only_in_overlays_are_not_registered()
    {
        var shoes = Shoes([Col("mp_m_freemode_01", "base", 16), Col("mp_m_freemode_01_heist_01", "patch9", 3, overlay: true)]);
        Assert.False(shoes.ContainsKey("mp_m_freemode_01_heist_01"));
    }

    [Fact]
    public void Props_and_peds_are_numbered_independently()
    {
        var female = Col("mp_f_freemode_01", "base", 5) with { Ped = "mp_f_freemode_01" };
        var numbered = ClothingNumbering.Assign(
            [Col("mp_m_freemode_01", "base", 16, hats: 4), Col("mp_m_freemode_01_beach", "mpbeach", 2, hats: 3), female], d => Order[d]);

        Assert.Equal([4, 5, 6], numbered.Where(d => d.Prop && d.Collection.Name.EndsWith("beach")).Select(d => d.GlobalIndex));
        Assert.Equal(0, numbered.Where(d => d.Collection.Ped == "mp_f_freemode_01").Min(d => d.GlobalIndex));
        Assert.All(numbered.Where(d => !d.Prop), d => Assert.Equal(2, d.Textures));
    }
}

public class DlcOverlaysTests
{
    [Fact]
    public void Finds_overlay_rpfs_in_content_xml()
    {
        const string xml = """
            <CDataFileMgr__ContentsOfDataFileXml>
              <dataFiles>
                <Item>
                  <filename>dlc_patchDay5NG:/%PLATFORM%/models/cdimages/patchday5ng_male.rpf</filename>
                  <fileType>RPF_FILE</fileType>
                  <overlay value="true" />
                </Item>
                <Item>
                  <filename>dlc_mpHeist4:/%PLATFORM%/models/cdimages/mpHeist4_male.rpf</filename>
                  <fileType>RPF_FILE</fileType>
                  <overlay value="false" />
                </Item>
                <Item>
                  <filename>dlc_patchDay5NG:/common/data/peds.meta</filename>
                  <fileType>PED_METADATA_FILE</fileType>
                  <overlay value="true" />
                </Item>
              </dataFiles>
            </CDataFileMgr__ContentsOfDataFileXml>
            """;

        Assert.Equal([@"x64\models\cdimages\patchday5ng_male.rpf"], DlcOverlays.ParseOverlayRpfs(xml));
    }
}
