using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.Tests.Indexer;

public class VehicleMetaTests
{
    private const string Xml = """
        <?xml version="1.0" encoding="UTF-8"?>
        <CVehicleModelInfo__InitDataList>
          <residentTxd>vehshare</residentTxd>
          <InitDatas>
            <Item>
              <modelName>velenogt</modelName>
              <txdName>velenogt</txdName>
              <handlingId>VELENOGT</handlingId>
              <gameName>VELENOGT</gameName>
              <vehicleMakeName>GROTTI</vehicleMakeName>
              <expressionDictName>null</expressionDictName>
              <type>VEHICLE_TYPE_CAR</type>
              <vehicleClass>VC_SUPER</vehicleClass>
              <wheelType>VWT_HIEND</wheelType>
              <flags>FLAG_SPORTS  FLAG_HAS_LIVERY
                FLAG_NO_BOOT</flags>
              <kits>
                <Item>631_velenogt_modkit</Item>
              </kits>
            </Item>
            <Item>
              <txdName>no_model_is_skipped</txdName>
            </Item>
          </InitDatas>
        </CVehicleModelInfo__InitDataList>
        """;

    [Fact]
    public void Parses_init_datas()
    {
        var v = Assert.Single(VehicleMeta.Parse(Xml));

        Assert.Equal("velenogt", v.ModelName);
        Assert.Equal("VELENOGT", v.GameName);
        Assert.Equal("GROTTI", v.MakeName);
        Assert.Equal("VC_SUPER", v.Class);
        Assert.Equal(["FLAG_SPORTS", "FLAG_HAS_LIVERY", "FLAG_NO_BOOT"], v.Flags);
        Assert.Equal(["631_velenogt_modkit"], v.Kits);
    }

    [Fact]
    public void Literal_null_values_become_null()
    {
        var xml = Xml.Replace("<handlingId>VELENOGT</handlingId>", "<handlingId>null</handlingId>");
        Assert.Null(Assert.Single(VehicleMeta.Parse(xml)).HandlingId);
    }

    [Theory]
    [InlineData("VC_SPORT_CLASSIC", "Sports Classics")]
    [InlineData("VC_OFF_ROAD", "Off-Road")]
    [InlineData("VC_SOMETHING_NEW", "Something New")]
    [InlineData(null, null)]
    public void Pretty_class(string? raw, string? pretty) =>
        Assert.Equal(pretty, VehicleMeta.PrettyClass(raw));

    [Fact]
    public void Pretty_type() => Assert.Equal("Car", VehicleMeta.PrettyType("VEHICLE_TYPE_CAR"));
}
