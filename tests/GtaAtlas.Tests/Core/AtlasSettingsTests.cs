using GtaAtlas.Core;

namespace GtaAtlas.Tests.Core;

public class AtlasSettingsTests
{
    [Fact]
    public void Round_trips_through_disk()
    {
        using var dir = new TempDir();
        var paths = dir.ToAppPaths();

        new AtlasSettings { GameFolder = @"C:\Games\GTAV", Gen9 = true, AesKey = "abc=" }.Save(paths);
        var loaded = AtlasSettings.Load(paths);

        Assert.Equal(@"C:\Games\GTAV", loaded.GameFolder);
        Assert.True(loaded.Gen9);
        Assert.Equal("abc=", loaded.AesKey);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        using var dir = new TempDir();

        var loaded = AtlasSettings.Load(dir.ToAppPaths());

        Assert.Null(loaded.GameFolder);
        Assert.False(loaded.Gen9);
    }

    [Fact]
    public void Corrupt_file_gives_defaults()
    {
        using var dir = new TempDir();
        var paths = dir.ToAppPaths();
        File.WriteAllText(paths.SettingsFile, "{ not json");

        Assert.Null(AtlasSettings.Load(paths).GameFolder);
    }
}
