using CodeWalker.GameFiles;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;

namespace GtaAtlas.Tests.Game;

/// <summary>Integration tests against the real game install; skipped when none is found.</summary>
[Collection(GameCollection.Name)]
public class GameSessionTests
{
    [Fact]
    public void Open_loads_keys_and_persists_settings()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var paths = dir.ToAppPaths();
        var settings = new AtlasSettings { GameFolder = game.Folder };

        var session = GameSession.Open(paths, settings);

        Assert.Equal(game, session.Install);
        var saved = AtlasSettings.Load(paths);
        Assert.Equal(game.Folder, saved.GameFolder);
        Assert.False(string.IsNullOrEmpty(saved.AesKey));
    }

    [Fact]
    public void Can_scan_encrypted_root_rpf()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });

        var rpf = new RpfFile(Path.Combine(game.Folder, "x64a.rpf"), "x64a.rpf");
        var errors = new List<string>();
        rpf.ScanStructure(null, errors.Add);

        Assert.Empty(errors);
        var file = rpf.AllEntries.OfType<RpfFileEntry>().First();
        var data = rpf.ExtractFile(file);
        Assert.NotNull(data);
        Assert.NotEmpty(data);
    }
}
