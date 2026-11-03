using System.Diagnostics;
using CodeWalker.GameFiles;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Render;

namespace GtaAtlas.Tests.Render;

[Collection(GameCollection.Name)]
public class GameAssetsTests(ITestOutputHelper output)
{
    [Fact]
    public async Task Loads_prop_drawable_and_vehicle_fragment()
    {
        var game = TestEnv.RequireGame();
        using var dir = new TempDir();
        var session = GameSession.Open(dir.ToAppPaths(), new AtlasSettings { GameFolder = game.Folder });

        var sw = Stopwatch.StartNew();
        using var assets = GameAssets.Start(session.Install);
        await assets.Ready.WaitAsync(TimeSpan.FromMinutes(3), TestContext.Current.CancellationToken);
        output.WriteLine($"init {sw.ElapsedMilliseconds} ms, {GC.GetTotalMemory(false) >> 20} MB managed");

        var arch = assets.Cache.GetArchetype(JenkHash.GenHash("prop_bench_01a"));
        Assert.NotNull(arch);
        var drawable = await Task.Run(() => assets.LoadDrawable(arch), TestContext.Current.CancellationToken);
        Assert.NotNull(drawable);
        Assert.True(drawable.BoundingSphereRadius > 0);
        output.WriteLine($"prop_bench_01a LODs: {string.Join(", ", ModelLods.Available(drawable))}");
        Assert.Contains(DetailLevel.High, ModelLods.Available(drawable));

        // DLC prop: archetypes of every DLC are loaded, not just base.
        Assert.NotNull(assets.Cache.GetArchetype(JenkHash.GenHash("h4_prop_x17_sub")));

        var adder = await Task.Run(() => assets.LoadVehicle("adder"), TestContext.Current.CancellationToken);
        Assert.NotNull(adder);
        Assert.NotNull(adder.Hi);
        Assert.NotNull(adder.Lods);
        output.WriteLine($"adder_hi LODs: {string.Join(", ", ModelLods.Available(adder.Hi.Drawable))}; adder: {string.Join(", ", ModelLods.Available(adder.Lods.Drawable))}");
        Assert.Equal([DetailLevel.High, DetailLevel.Medium, DetailLevel.Low, DetailLevel.VeryLow], ModelLods.Vehicle(adder.Hi.Drawable, adder.Lods.Drawable));
        Assert.Null(await Task.Run(() => assets.LoadVehicle("not_a_vehicle"), TestContext.Current.CancellationToken));
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}
