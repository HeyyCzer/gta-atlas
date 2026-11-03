using GtaAtlas.Render;

namespace GtaAtlas.Tests.Render;

public class InitProgressTests
{
    private static double ScanShare => InitProgress.Stages[0].Weight / InitProgress.Stages.Sum(s => s.Weight);

    [Fact]
    public void Stages_follow_the_init_replay()
    {
        var replay = new[] { InitProgress.Scan, "InitGlobal" }.Concat(CacheInit.DlcSteps.Where(s => !CacheInit.Skipped.Contains(s)));
        Assert.Equal(replay, InitProgress.Stages.Select(s => s.Stage));
    }

    [Fact]
    public void Scan_advances_by_bytes_of_archives_already_passed()
    {
        var p = new InitProgress([("a.rpf", 300), (@"update\b.rpf", 100)]);
        p.Status("Scanning a.rpf...");
        Assert.Equal(0, p.Value);
        p.Status(@"Scanning a.rpf\levels\nested.rpf..."); // nested: still inside a.rpf
        Assert.Equal(0, p.Value);
        p.Status(@"Scanning update\b.rpf...");
        Assert.Equal(0.75 * ScanShare, p.Value, 4);
    }

    [Fact]
    public void Ignores_other_messages_and_unknown_archives()
    {
        var p = new InitProgress([("a.rpf", 1)]);
        p.Status("Building jenkindex...");
        p.Status("Scanning mods\\x.rpf...");
        p.Status(null!);
        Assert.Equal(0, p.Value);
    }

    [Fact]
    public void Stages_only_move_forward_and_complete_reaches_one()
    {
        var p = new InitProgress([]);
        p.Enter("InitGtxds");
        var atGtxd = p.Value;
        Assert.True(atGtxd > ScanShare);
        p.Enter("InitGlobal"); // earlier stage: ignored
        Assert.Equal(atGtxd, p.Value);
        p.Complete();
        Assert.Equal(1, p.Value);
        Assert.Equal(["InitGtxds"], p.Timings.Select(t => t.Stage));
    }
}
