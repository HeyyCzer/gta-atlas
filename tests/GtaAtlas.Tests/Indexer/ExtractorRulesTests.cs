using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.Tests.Indexer;

public class ExtractorRulesTests
{
    [Theory]
    [InlineData(1u, 1u, true)]    // script-managed map
    [InlineData(1u, 65u, true)]
    [InlineData(3u, 2u, false)]   // LOD map: streams with its parent
    [InlineData(0u, 73u, false)]  // always-loaded map
    [InlineData(2u, 16u, false)]
    public void Ipl_is_script_managed_and_not_lod(uint flags, uint contentFlags, bool ipl) =>
        Assert.Equal(ipl, YmapExtractor.IsIpl(flags, contentFlags));

    [Theory]
    [InlineData("pack:/idle_a.clip", "idle_a")]
    [InlineData("idle_b.clip", "idle_b")]
    [InlineData("pack:/sub/dir/enter.CLIP", "enter")]
    [InlineData("noext", "noext")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Clip_name_is_cleaned(string? raw, string clean) =>
        Assert.Equal(clean, AnimExtractor.CleanClipName(raw));
}
