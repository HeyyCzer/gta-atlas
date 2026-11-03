using System.Xml;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

namespace GtaAtlas.Tests.Indexer;

public class DlcCatalogTests
{
    [Fact]
    public void Parses_dlclist_in_order()
    {
        var doc = new XmlDocument();
        doc.LoadXml("""
            <SMandatoryPacksData>
              <Paths>
                <Item>platform:/dlcPacks/mpBeach/</Item>
                <Item>platform:/dlcPacks/mpHeist4/</Item>
                <Item>platform:\dlcPacks\patchday1ng</Item>
              </Paths>
            </SMandatoryPacksData>
            """);

        Assert.Equal(["mpbeach", "mpheist4", "patchday1ng"], DlcCatalog.ParseDlcList(doc));
    }

    [Fact]
    public void Appends_unlisted_dlcs_after_known_ones()
    {
        List<DlcInput> dlcs = [new(DlcNames.Base, 0), new("mpbeach", 1), new(DlcNames.Update, 2)];

        var result = DlcCatalog.WithUnlisted(dlcs, ["mpbeach", "zzz", "aaa", "zzz"]);

        Assert.Equal(["base", "mpbeach", "update", "aaa", "zzz"], result.Select(d => d.Name));
        Assert.Equal([0, 1, 2, 3, 4], result.Select(d => d.OrderIndex));
    }
}
