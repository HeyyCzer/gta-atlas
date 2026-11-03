using Dapper;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

namespace GtaAtlas.Tests.Game;

/// <summary>
/// Global clothing indices must match the running game (what FiveM scripts use).
/// Expected values come from forge.plebmasters.de and the DurtyFree/gta-v-data-dumps runtime dump.
/// </summary>
[Collection(GameCollection.Name)]
public sealed class ClothingIndexTests(ClothingIndexTests.Index index) : IClassFixture<ClothingIndexTests.Index>
{
    public sealed class Row
    {
        public string Name { get; init; } = "";
        public string Dlc { get; init; } = "";
        public string Collection { get; init; } = "";
        public long Prop { get; init; }
        public long Slot { get; init; }
        public long Drawable { get; init; }
        public string? FirstTexture { get; init; }
    }

    public sealed class Index : IDisposable
    {
        private readonly TempDir _dir = new();

        public Index()
        {
            var game = TestEnv.Game;
            if (game is null || game.Gen9)
                return;
            var session = GameSession.Open(_dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });
            var db = AtlasDb.Open(_dir.Sub("atlas.db"));
            GameIndexer.Run(session, db, extractors: []); // clothing pass only

            using var c = db.Connect();
            Rows = c.Query<Row>(
                """
                SELECT a.name, d.name AS dlc, json_extract(a.data, '$.collection') AS collection,
                       json_extract(a.data, '$.prop') AS prop, json_extract(a.data, '$.slot') AS slot,
                       json_extract(a.data, '$.drawable') AS drawable, json_extract(a.data, '$.textures[0].name') AS firstTexture
                FROM asset a JOIN dlc d ON d.id = a.dlc_id WHERE a.kind = 'clothing'
                """).ToList();
        }

        public List<Row>? Rows { get; }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            _dir.Dispose();
        }
    }

    private List<Row> Rows()
    {
        TestEnv.RequireGame();
        Assert.SkipWhen(index.Rows is null, "Clothing numbering expectations are for Legacy.");
        return index.Rows!;
    }

    [Theory]
    [InlineData("C/6/137/M", "Black Cloth Clogs", "mp2023_02")]
    [InlineData("C/8/101/M", "Blue Digital T-Shirt", "mpgunrunning")]
    [InlineData("C/2/49/M", "Chopped", "mpgunrunning")]
    [InlineData("P/0/76/M", "Atomic Cap", "mpstunt")]
    [InlineData("P/7/8/M", "Blue Bangles (R)", "mpheist4")]
    public void Matches_known_items(string name, string firstTexture, string dlc)
    {
        var row = Assert.Single(Rows(), r => r.Name == name);
        Assert.Equal(dlc, row.Dlc);
        Assert.Equal(firstTexture, row.FirstTexture);
    }

    [Theory]
    [InlineData("mp_m_freemode_01_male_xmas", false, 6, 17)]
    [InlineData("mp_m_freemode_01_mp_m_xmas_03", false, 6, 39)]
    [InlineData("mp_m_freemode_01_mp_m_christmas3", false, 6, 109)]
    [InlineData("mp_m_freemode_01_mp_m_christmas3", false, 11, 413)]
    [InlineData("mp_m_freemode_01_mp_m_christmas3", true, 0, 173)]
    [InlineData("mp_f_freemode_01_mp_f_christmas3", false, 11, 440)]
    public void Collections_start_where_the_game_puts_them(string collection, bool prop, int slot, int start) =>
        Assert.Equal(start, Rows().Where(r => r.Collection == collection && r.Prop == (prop ? 1 : 0) && r.Slot == slot).Min(r => r.Drawable));

    [Fact]
    public void Legacy_skips_enhanced_only_collections() =>
        Assert.DoesNotContain(Rows(), r => r.Collection.Contains("g9ec"));
}
