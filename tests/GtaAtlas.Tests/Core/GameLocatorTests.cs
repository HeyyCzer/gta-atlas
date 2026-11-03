using GtaAtlas.Core.Game;

namespace GtaAtlas.Tests.Core;

public class GameLocatorTests
{
    [Fact]
    public void Parses_all_steam_library_paths()
    {
        const string vdf = """
            "libraryfolders"
            {
            	"0"
            	{
            		"path"		"C:\\Program Files (x86)\\Steam"
            		"label"		""
            		"apps"
            		{
            			"228980"		"123"
            		}
            	}
            	"1"
            	{
            		"path"		"C:\\SteamLibrary"
            		"apps"
            		{
            			"271590"		"456"
            		}
            	}
            }
            """;

        var paths = GameLocator.ParseSteamLibraryFolders(vdf).ToList();

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"C:\SteamLibrary"], paths);
    }

    [Fact]
    public void Empty_vdf_yields_no_paths() =>
        Assert.Empty(GameLocator.ParseSteamLibraryFolders("\"libraryfolders\" { }"));
}
