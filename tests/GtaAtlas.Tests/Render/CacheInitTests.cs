using System.Text.RegularExpressions;
using GtaAtlas.Render;

namespace GtaAtlas.Tests.Render;

/// <summary>Guards the reflection replay in CacheInit against upstream changes to GameFileCache's init sequence.</summary>
public class CacheInitTests
{
    private static string Source => File.ReadAllText(Path.Combine(TestEnv.CodeWalkerDir, "CodeWalker.Core", "GameFiles", "GameFileCache.cs"));

    private static List<string> Calls(string method)
    {
        var body = Regex.Match(Source, $@"private void {method}\(\)\s*\{{(?<body>.*?)\n        \}}", RegexOptions.Singleline).Groups["body"].Value;
        return Regex.Matches(body, @"^\s*(Init\w+)\(\);", RegexOptions.Multiline).Select(m => m.Groups[1].Value).ToList();
    }

    [Fact]
    public void Finds_every_upstream_member() => Assert.True(CacheInit.Supported);

    [Fact]
    public void Dlc_steps_match_upstream() => Assert.Equal(CacheInit.DlcSteps, Calls("InitDlc"));

    [Fact]
    public void Global_step_only_builds_global_dicts() => Assert.Equal(["InitGlobalDicts"], Calls("InitGlobal"));
}
