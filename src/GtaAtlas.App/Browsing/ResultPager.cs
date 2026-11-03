using GtaAtlas.Data;

namespace GtaAtlas.App.Browsing;

/// <summary>
/// Random access over a search result for a virtualized view: fetches fixed-size pages on demand
/// and keeps only the most recently used ones.
/// </summary>
public sealed class ResultPager
{
    private readonly Func<SearchQuery, SearchResult> _search;
    private readonly SearchQuery _query;
    private readonly int _pageSize;
    private readonly int _maxPages;
    private readonly Dictionary<int, IReadOnlyList<AssetSummary>> _pages = [];
    private readonly LinkedList<int> _recent = new();

    private ResultPager(Func<SearchQuery, SearchResult> search, SearchQuery query, int pageSize, int maxPages, SearchResult first)
    {
        _search = search;
        _query = query;
        _pageSize = pageSize;
        _maxPages = maxPages;
        Total = first.Total;
        Store(0, first.Items);
    }

    public static ResultPager Empty { get; } = new(_ => new SearchResult([], 0), new SearchQuery(), 1, 1, new SearchResult([], 0));

    public int Total { get; }

    /// <summary>Runs the query for the first page (and the total). Safe to call off the UI thread.</summary>
    public static ResultPager Load(Func<SearchQuery, SearchResult> search, SearchQuery query, int pageSize = 200, int maxPages = 32)
    {
        var first = search(query with { Offset = 0, Limit = pageSize });
        return new ResultPager(search, query, pageSize, maxPages, first);
    }

    public AssetSummary? this[int index]
    {
        get
        {
            if (index < 0 || index >= Total)
                return null;

            var page = index / _pageSize;
            if (!_pages.TryGetValue(page, out var items))
            {
                items = _search(_query with { Offset = page * _pageSize, Limit = _pageSize }).Items;
                Store(page, items);
            }
            else
            {
                _recent.Remove(page);
                _recent.AddFirst(page);
            }

            var i = index - page * _pageSize;
            return i < items.Count ? items[i] : null;
        }
    }

    /// <summary>Position of an asset among the pages already loaded, or -1.</summary>
    public int IndexOf(long assetId)
    {
        foreach (var (page, items) in _pages)
        {
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Id == assetId)
                    return page * _pageSize + i;
            }
        }
        return -1;
    }

    public int LoadedPages => _pages.Count;

    private void Store(int page, IReadOnlyList<AssetSummary> items)
    {
        _pages[page] = items;
        _recent.AddFirst(page);
        while (_recent.Count > _maxPages)
        {
            _pages.Remove(_recent.Last!.Value);
            _recent.RemoveLast();
        }
    }
}
