using GtaAtlas.Core.Game;

namespace GtaAtlas.Tests.Core;

public class GameInstallTests
{
    [Fact]
    public void Detects_legacy_exe()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Sub("GTA5.exe"), "");

        var install = GameInstall.TryFromFolder(dir.Path);

        Assert.NotNull(install);
        Assert.False(install.Gen9);
        Assert.Equal(dir.Sub("GTA5.exe"), install.ExePath);
    }

    [Fact]
    public void Detects_enhanced_exe()
    {
        using var dir = new TempDir();
        File.WriteAllText(dir.Sub("GTA5_Enhanced.exe"), "");

        var install = GameInstall.TryFromFolder(dir.Path);

        Assert.NotNull(install);
        Assert.True(install.Gen9);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"Z:\does\not\exist")]
    public void Rejects_missing_folder(string? folder) =>
        Assert.Null(GameInstall.TryFromFolder(folder));

    [Fact]
    public void Rejects_folder_without_exe()
    {
        using var dir = new TempDir();
        Assert.Null(GameInstall.TryFromFolder(dir.Path));
    }
}
