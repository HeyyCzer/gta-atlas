using System.Text.RegularExpressions;
using CodeWalker;

namespace GtaAtlas.Tests.Render;

/// <summary>Guards Stubs.cs against drifting from upstream when the CodeWalker submodule is updated.</summary>
public class StubsTests
{
    [Fact]
    public void MapSelectionMode_matches_upstream()
    {
        var source = File.ReadAllText(Path.Combine(TestEnv.CodeWalkerDir, "CodeWalker", "World", "MapSelection.cs"));
        var body = Regex.Match(source, @"enum MapSelectionMode\s*\{(?<body>.*?)\}", RegexOptions.Singleline).Groups["body"].Value;
        var upstream = Regex.Matches(body, @"(\w+)\s*=\s*(\d+)")
            .ToDictionary(m => m.Groups[1].Value, m => int.Parse(m.Groups[2].Value));

        var stub = Enum.GetValues<MapSelectionMode>().ToDictionary(v => v.ToString(), v => (int)v);

        Assert.NotEmpty(upstream);
        Assert.Equal(upstream, stub);
    }
}
