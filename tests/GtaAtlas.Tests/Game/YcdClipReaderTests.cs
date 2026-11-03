using CodeWalker.GameFiles;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Indexer;
using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.Tests.Game;

[Collection(GameCollection.Name)]
public class YcdClipReaderTests
{
    /// <summary>The fast reader must agree with CodeWalker's full YcdFile parse.</summary>
    [Fact]
    public void Matches_full_ycd_parse_on_sample_of_game_files()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var session = GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });
        var archive = GameArchive.Scan(session);

        var ycds = archive.Files.OfType<RpfResourceFileEntry>().Where(e => e.NameLower.EndsWith(".ycd")).ToList();
        var sample = ycds.Where((_, i) => i % (ycds.Count / 300) == 0).ToList();
        Assert.True(sample.Count >= 250);

        var compared = 0;
        foreach (var entry in sample)
        {
            var data = entry.File.ExtractFile(entry);
            var full = new YcdFile();
            full.Load(data, entry);
            var expected = (full.ClipMapEntries ?? [])
                .Where(e => e?.Clip is not null)
                .Select(e => (e.Hash.Hash, Name: (string?)e.Clip.Name, Duration: e.Clip switch
                {
                    ClipAnimation a => a.Rate > 0 ? (a.EndTime - a.StartTime) / a.Rate : a.EndTime - a.StartTime,
                    ClipAnimationList l => l.Duration,
                    _ => 0f,
                }))
                .OrderBy(c => c.Hash).ThenBy(c => c.Name)
                .ToList();

            var fast = YcdClipReader.Read(entry, entry.File.ExtractFile(entry))
                .Select(c => (c.Hash, c.Name, c.Duration))
                .OrderBy(c => c.Hash).ThenBy(c => c.Name)
                .ToList();

            Assert.Equal(expected, fast);
            compared += fast.Count;
        }
        Assert.True(compared > 500, $"only {compared} clips compared");
    }
}
