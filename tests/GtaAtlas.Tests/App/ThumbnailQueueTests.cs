using System.Drawing;
using GtaAtlas.App.Browsing;
using GtaAtlas.Data;

namespace GtaAtlas.Tests.App;

public class ThumbnailQueueTests
{
    private static List<(string Key, bool Background)> Drain(ThumbnailQueue<string> queue)
    {
        var taken = new List<(string, bool)>();
        while (queue.Take(out var key, out var background, timeoutMs: 0))
        {
            taken.Add((key, background));
            queue.Done(key);
        }
        return taken;
    }

    [Fact]
    public void Latest_request_goes_first_and_background_waits_for_requests()
    {
        var queue = new ThumbnailQueue<string>();
        queue.EnqueueBackground(["bg1", "bg2", "bg1"]);
        queue.Request("a");
        queue.Request("b");
        queue.Request("a"); // painted again: back on top, not twice

        Assert.Equal([("a", false), ("b", false), ("bg1", true), ("bg2", true)], Drain(queue));
    }

    [Fact]
    public void Key_in_progress_is_not_requested_again_until_done()
    {
        var queue = new ThumbnailQueue<string>();
        queue.Request("a");
        Assert.True(queue.Take(out var key, out _, timeoutMs: 0));
        queue.Request("a");
        Assert.False(queue.Take(out _, out _, timeoutMs: 0));

        queue.Done(key);
        queue.Request("a");
        Assert.Equal([("a", false)], Drain(queue));
    }

    [Fact]
    public void Oldest_requests_drop_past_the_cap()
    {
        var queue = new ThumbnailQueue<string>();
        for (var i = 0; i < ThumbnailQueue<string>.MaxRequests + 10; i++)
            queue.Request($"k{i}");

        var taken = Drain(queue);
        Assert.Equal(ThumbnailQueue<string>.MaxRequests, taken.Count);
        Assert.Equal($"k{ThumbnailQueue<string>.MaxRequests + 9}", taken[0].Key);
        Assert.DoesNotContain(("k0", false), taken);
    }

    [Fact]
    public void Clearing_background_keeps_requests()
    {
        var queue = new ThumbnailQueue<string>();
        queue.EnqueueBackground(["bg"]);
        queue.Request("a");
        queue.ClearBackground();
        Assert.Equal(0, queue.BackgroundCount);
        Assert.Equal([("a", false)], Drain(queue));
    }

    [Fact]
    public async Task Take_waits_for_work_and_close_ends_it()
    {
        var queue = new ThumbnailQueue<string>();
        var take = Task.Run(() => queue.Take(out var key, out _) ? key : null, TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(take.IsCompleted);
        queue.Request("a");
        Assert.Equal("a", await take);

        var ended = Task.Run(() => queue.Take(out _, out _), TestContext.Current.CancellationToken);
        queue.Close();
        Assert.False(await ended);
    }
}

public class ThumbnailStoreTests
{
    [Fact]
    public void Saves_loads_and_marks_failures_per_kind_and_hash()
    {
        using var dir = new TempDir();
        var store = new ThumbnailStore(dir.Path);
        var key = new ThumbKey(AssetKind.Prop, 0xDEADBEEF, "prop_x");

        Assert.Equal(Path.Combine(dir.Path, "prop", "deadbeef.png"), store.PathFor(key));
        Assert.False(store.Exists(key));
        Assert.Null(store.Load(key));

        store.MarkFailed(key, "Archetype not found");
        Assert.True(store.IsFailed(key));

        using (var bitmap = new Bitmap(4, 2))
        {
            bitmap.SetPixel(1, 1, Color.FromArgb(128, 255, 0, 0));
            store.Save(key, bitmap);
        }
        Assert.True(store.Exists(key));
        Assert.False(store.IsFailed(key)); // a later success clears the marker
        Assert.False(File.Exists(store.PathFor(key) + ".tmp"));

        using var loaded = store.Load(key);
        Assert.NotNull(loaded);
        Assert.Equal(new Size(4, 2), loaded.Size);
        Assert.Equal(Color.FromArgb(128, 255, 0, 0), loaded.GetPixel(1, 1));
        File.Delete(store.PathFor(key)); // not locked by Load
    }

    [Fact]
    public void Trim_deletes_least_recently_used_down_to_the_target()
    {
        using var dir = new TempDir();
        var store = new ThumbnailStore(dir.Path);
        var keys = Enumerable.Range(0, 5).Select(i => new ThumbKey(AssetKind.Prop, (uint)i, $"p{i}")).ToList();
        var start = DateTime.UtcNow.AddHours(-1);
        foreach (var (key, i) in keys.Select((k, i) => (k, i)))
        {
            using (var bitmap = new Bitmap(32, 32))
                store.Save(key, bitmap);
            File.SetLastWriteTimeUtc(store.PathFor(key), start.AddMinutes(i)); // p0 oldest
        }
        using (store.Load(keys[0])) { } // shown again: now the most recent
        store.MarkFailed(new ThumbKey(AssetKind.Prop, 99, "gone"), "no model");
        File.WriteAllText(store.PathFor(keys[0]) + ".tmp", "partial");
        var size = new FileInfo(store.PathFor(keys[0])).Length;

        Assert.Equal(0, store.Trim(maxBytes: size * 5, targetBytes: size * 2)); // under the limit: untouched
        Assert.False(File.Exists(store.PathFor(keys[0]) + ".tmp"));

        Assert.Equal(3, store.Trim(maxBytes: size * 4, targetBytes: size * 2));
        Assert.Equal([true, false, false, false, true], keys.Select(store.Exists));
        Assert.True(store.IsFailed(new ThumbKey(AssetKind.Prop, 99, "gone")));
    }

    [Theory]
    [InlineData(AssetKind.Prop, true)]
    [InlineData(AssetKind.Vehicle, true)]
    [InlineData(AssetKind.Ped, false)]
    [InlineData(AssetKind.Anim, false)]
    public void Supports_what_the_renderer_draws(string kind, bool supported) =>
        Assert.Equal(supported, ThumbnailStore.Supports(kind));
}
