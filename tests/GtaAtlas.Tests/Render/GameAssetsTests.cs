using System.Diagnostics;
using CodeWalker.GameFiles;
using CodeWalker.World;
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
        var drawable = await Until(() => assets.Cache.TryGetDrawable(arch));
        Assert.True(drawable.BoundingSphereRadius > 0);

        // DLC prop: archetypes of every DLC are loaded, not just base.
        Assert.NotNull(assets.Cache.GetArchetype(JenkHash.GenHash("h4_prop_x17_sub")));

        var vehicle = new Vehicle();
        await Task.Run(() => vehicle.Init("adder", assets.Cache), TestContext.Current.CancellationToken);
        Assert.NotNull(vehicle.Yft?.Fragment);
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }

    private static async Task<T> Until<T>(Func<T?> get) where T : class
    {
        for (var i = 0; i < 500; i++)
        {
            if (get() is { } value)
                return value;
            await Task.Delay(10);
        }
        throw new TimeoutException();
    }
}
