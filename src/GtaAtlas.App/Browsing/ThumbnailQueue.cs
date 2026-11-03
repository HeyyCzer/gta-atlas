namespace GtaAtlas.App.Browsing;

/// <summary>
/// Work order for thumbnails. Requests from painted cards form a stack: the latest request goes first, so while the
/// user scrolls the cards on screen win over the ones scrolled past (which drop off past <see cref="MaxRequests"/>).
/// Bulk work (<see cref="EnqueueBackground"/>) runs only when no card is waiting. A key being worked on is not
/// requested again until <see cref="Done"/>. Thread-safe; <see cref="Take"/> blocks.
/// </summary>
public sealed class ThumbnailQueue<TKey> where TKey : notnull
{
    public const int MaxRequests = 400;

    private readonly object _lock = new();
    private readonly LinkedList<TKey> _stack = new();
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _stacked = [];
    private readonly Queue<TKey> _background = new();
    private readonly HashSet<TKey> _queued = [];
    private readonly HashSet<TKey> _working = [];
    private bool _closed;

    /// <summary>Asks for <paramref name="key"/> ahead of everything else. No-op while it's being worked on.</summary>
    public void Request(TKey key)
    {
        lock (_lock)
        {
            if (_working.Contains(key))
                return;
            if (_stacked.Remove(key, out var node))
                _stack.Remove(node);
            _stacked[key] = _stack.AddFirst(key);
            if (_stack.Count > MaxRequests)
            {
                _stacked.Remove(_stack.Last!.Value);
                _stack.RemoveLast();
            }
            Monitor.Pulse(_lock);
        }
    }

    /// <summary>Adds keys to do after all requests, in order; skips ones already queued.</summary>
    public void EnqueueBackground(IEnumerable<TKey> keys)
    {
        lock (_lock)
        {
            foreach (var key in keys)
            {
                if (_queued.Add(key))
                    _background.Enqueue(key);
            }
            Monitor.Pulse(_lock);
        }
    }

    /// <summary>Drops pending background work (requests stay).</summary>
    public void ClearBackground()
    {
        lock (_lock)
        {
            _background.Clear();
            _queued.Clear();
        }
    }

    public int BackgroundCount
    {
        get
        {
            lock (_lock)
                return _background.Count;
        }
    }

    /// <summary>Next key, waiting for one; false once <see cref="Close"/>d. <paramref name="background"/> tells which list it came from.</summary>
    public bool Take(out TKey key, out bool background, int timeoutMs = Timeout.Infinite)
    {
        lock (_lock)
        {
            while (!_closed)
            {
                if (_stack.First is { } first)
                {
                    key = first.Value;
                    _stack.RemoveFirst();
                    _stacked.Remove(key);
                    background = false;
                    _working.Add(key);
                    return true;
                }
                if (_background.TryDequeue(out key!))
                {
                    _queued.Remove(key);
                    background = true;
                    _working.Add(key);
                    return true;
                }
                if (!Monitor.Wait(_lock, timeoutMs))
                    break;
            }
            key = default!;
            background = false;
            return false;
        }
    }

    /// <summary>Marks a taken key finished, so it can be requested again.</summary>
    public void Done(TKey key)
    {
        lock (_lock)
            _working.Remove(key);
    }

    /// <summary>Wakes and ends every <see cref="Take"/>.</summary>
    public void Close()
    {
        lock (_lock)
        {
            _closed = true;
            Monitor.PulseAll(_lock);
        }
    }
}
