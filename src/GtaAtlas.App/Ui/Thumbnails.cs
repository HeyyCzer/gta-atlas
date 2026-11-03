using GtaAtlas.App.Browsing;
using GtaAtlas.Data;
using GtaAtlas.Render;

namespace GtaAtlas.App.Ui;

/// <summary>
/// Card thumbnails: <see cref="Get"/> answers from memory and queues the rest; one worker thread loads saved PNGs and,
/// once the game files are loaded (<see cref="AttachAssets"/>), renders missing ones offscreen and saves them.
/// Results arrive on the UI thread and raise <see cref="Updated"/> (coalesced). Create and use on the UI thread.
/// </summary>
internal sealed class Thumbnails : IDisposable
{
    private const int MaxBitmaps = 96;
    private const int UpdateDelayMs = 80;

    private readonly ThumbnailStore _store;
    private readonly ThumbnailQueue<ThumbKey> _queue = new();
    private readonly SynchronizationContext _ui;
    private readonly Thread _worker;

    // UI thread only.
    private readonly Dictionary<ThumbKey, LinkedListNode<(ThumbKey Key, Bitmap Bitmap)>> _bitmaps = [];
    private readonly LinkedList<(ThumbKey Key, Bitmap Bitmap)> _recent = new();
    private readonly HashSet<ThumbKey> _failed = [];
    private bool _updatePending;
    private bool _disposed;

    private volatile ThumbnailRenderer? _renderer;
    private volatile bool _paused;
    private volatile bool _closing;
    private int _backgroundDone;
    private int _backgroundTotal;

    public Thumbnails(ThumbnailStore store)
    {
        _store = store;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Create on the UI thread.");
        _worker = new Thread(Work) { IsBackground = true, Name = "Thumbnails", Priority = ThreadPriority.BelowNormal };
        _worker.Start();
    }

    /// <summary>New thumbnails are available (or the game finished loading): repaint and ask again.</summary>
    public event EventHandler? Updated;

    /// <summary>Whether missing thumbnails can be rendered (game files loaded, device created).</summary>
    public bool CanRender => _renderer is not null;

    /// <summary>Holds rendering (saved thumbnails still load), e.g. while the 3D viewport needs the GPU and file cache.</summary>
    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    /// <summary>The asset's thumbnail if loaded; otherwise null, and it's queued ahead of earlier requests.</summary>
    public Bitmap? Get(AssetSummary item)
    {
        if (!ThumbnailStore.Supports(item.Kind))
            return null;
        var key = ThumbKey.Of(item);
        if (_bitmaps.TryGetValue(key, out var node))
        {
            _recent.Remove(node);
            _recent.AddFirst(node);
            return node.Value.Bitmap;
        }
        if (!_failed.Contains(key) && !_disposed)
            _queue.Request(key);
        return null;
    }

    /// <summary>Starts rendering missing thumbnails with the loaded game files.</summary>
    public void AttachAssets(GameAssets assets)
    {
        try
        {
            _renderer = new ThumbnailRenderer(assets);
        }
        catch (Exception e)
        {
            AppLog.Write("thumbs", [$"renderer: {e}"]);
            return;
        }
        RaiseUpdated(); // cards painted before this asked for thumbnails that couldn't be rendered yet
    }

    /// <summary>Renders, after any visible card, every listed asset that has no thumbnail yet.</summary>
    public void GenerateAll(IEnumerable<ThumbKey> keys)
    {
        var missing = keys.Where(k => ThumbnailStore.Supports(k.Kind)).ToList();
        Interlocked.Add(ref _backgroundTotal, missing.Count);
        _queue.EnqueueBackground(missing);
    }

    public void StopGenerating()
    {
        _queue.ClearBackground();
        Interlocked.Exchange(ref _backgroundDone, 0);
        Interlocked.Exchange(ref _backgroundTotal, 0);
    }

    /// <summary>Progress of <see cref="GenerateAll"/>; null when idle.</summary>
    public (int Done, int Total)? Generating
    {
        get
        {
            var total = Volatile.Read(ref _backgroundTotal);
            var done = Volatile.Read(ref _backgroundDone);
            if (total == 0)
                return null;
            if (done >= total && _queue.BackgroundCount == 0)
            {
                StopGenerating();
                return null;
            }
            return (done, total);
        }
    }

    private void Work()
    {
        while (_queue.Take(out var key, out var background))
        {
            try
            {
                Process(key, background);
            }
            catch (Exception e)
            {
                AppLog.Write("thumbs", [$"{key.Kind} {key.Name}: {e}"]);
                Deliver(key, null, failed: true);
            }
            finally
            {
                _queue.Done(key);
                if (background)
                    Interlocked.Increment(ref _backgroundDone);
            }
        }
    }

    private void Process(ThumbKey key, bool background)
    {
        if (_store.Exists(key))
        {
            if (!background)
                Deliver(key, _store.Load(key), failed: false);
            return;
        }
        if (_store.IsFailed(key))
        {
            Deliver(key, null, failed: true);
            return;
        }

        while (_paused && !_closing)
            Thread.Sleep(100);
        if (_renderer is not { } renderer || _closing)
            return; // asked again once the game files are loaded (AttachAssets raises Updated)

        Bitmap bitmap;
        try
        {
            bitmap = key.Kind == AssetKind.Vehicle ? renderer.RenderVehicle(key.Name) : renderer.RenderProp(key.Hash);
        }
        catch (ModelNotFoundException e)
        {
            _store.MarkFailed(key, e.Message);
            Deliver(key, null, failed: true);
            return;
        }
        catch (ObjectDisposedException) when (_closing)
        {
            return;
        }

        _store.Save(key, bitmap);
        if (background)
            bitmap.Dispose();
        else
            Deliver(key, bitmap, failed: false);
    }

    /// <summary>Hands a result to the UI thread (which owns the bitmaps from then on).</summary>
    private void Deliver(ThumbKey key, Bitmap? bitmap, bool failed)
    {
        _ui.Post(_ =>
        {
            if (_disposed)
            {
                bitmap?.Dispose();
                return;
            }
            if (bitmap is not null)
                Store(key, bitmap);
            else if (failed)
                _failed.Add(key);
            else
                return;
            RaiseUpdated();
        }, null);
    }

    private void Store(ThumbKey key, Bitmap bitmap)
    {
        if (_bitmaps.Remove(key, out var old))
        {
            _recent.Remove(old);
            old.Value.Bitmap.Dispose();
        }
        _bitmaps[key] = _recent.AddFirst((key, bitmap));
        while (_recent.Count > MaxBitmaps)
        {
            var last = _recent.Last!;
            _recent.RemoveLast();
            _bitmaps.Remove(last.Value.Key);
            last.Value.Bitmap.Dispose();
        }
    }

    /// <summary>Raises <see cref="Updated"/> once for a burst of results.</summary>
    private async void RaiseUpdated()
    {
        if (_updatePending)
            return;
        _updatePending = true;
        await Task.Delay(UpdateDelayMs);
        _updatePending = false;
        if (!_disposed)
            Updated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Stops the worker (waiting for a render in progress) and releases the device and bitmaps.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _closing = true;
        _queue.Close();
        _worker.Join(TimeSpan.FromSeconds(10));
        _renderer?.Dispose();
        foreach (var (_, bitmap) in _recent)
            bitmap.Dispose();
        _recent.Clear();
        _bitmaps.Clear();
    }
}
