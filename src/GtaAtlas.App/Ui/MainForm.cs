using System.Diagnostics;
using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer;
using GtaAtlas.Render;

namespace GtaAtlas.App.Ui;

internal sealed class MainForm : Form
{
    private const int SearchDelayMs = 150;

    private readonly AppPaths _paths;
    private readonly AtlasSettings _settings;
    private readonly AtlasDb _db;
    private readonly AssetQueries _queries;

    private readonly NavSidebar _nav = new() { Dock = DockStyle.Left };
    private readonly Panel _top = new() { Dock = DockStyle.Top, BackColor = Theme.Back };
    private readonly PageTitle _title = new();
    private readonly Segmented _scope = new()
    {
        Segments = [new Segment(Glyph.Apps, "All"), new Segment(Glyph.StarFilled, "Favorites", "Only your favorites in this category (Ctrl+D toggles)")],
    };
    private readonly Segmented _view = new()
    {
        Segments = [new Segment(Glyph.Grid, null, "Grid (Ctrl+L)"), new Segment(Glyph.List, null, "List (Ctrl+L)")],
    };
    private readonly PillButton _generate = new() { Icon = Glyph.Photo, Text = "Generate thumbnails" };
    private readonly ToolTip _toolTip = new();
    private readonly SearchBox _search = new() { Placeholder = "Search by name, display name, tag or hash" };
    private readonly Dropdown _dlc = new() { Text = "DLC" };
    private readonly Dropdown _gender = new() { Text = "Gender" };
    private readonly ChipBar _chips = new();
    private readonly AssetGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly DlcTableView _dlcTable = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Toast _toast = new();
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = SearchDelayMs };

    private BrowseState _state = new();
    private IReadOnlyDictionary<string, int> _counts = new Dictionary<string, int>();
    private Dictionary<Category, int> _taggedCounts = [];
    private int _generation;
    private GameAssets? _assets;
    private readonly Thumbnails _thumbnails;

    public MainForm(AppPaths paths, AtlasSettings settings, AtlasDb db)
    {
        _paths = paths;
        _settings = settings;
        _db = db;
        _queries = new AssetQueries(db);
        _thumbnails = new Thumbnails(new ThumbnailStore(paths.Thumbnails));
        _grid.Thumbnails = _thumbnails;

        Text = "GTA Atlas";
        Icon = AppIcon.Get();
        Theme.ApplyWindowChrome(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.UiFont;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(960, 600);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        BuildLayout();
        WireEvents();
    }

    private int Px(int logical) => LogicalToDeviceUnits(logical);

    private void BuildLayout()
    {
        _gender.Items =
        [
            new PickerItem(Gender.Any, "Any"),
            new PickerItem(Gender.Male, "Male"),
            new PickerItem(Gender.Female, "Female"),
        ];
        _generate.BackColor = Theme.Back;
        _generate.Visible = false;
        _toolTip.SetToolTip(_generate, "Render a thumbnail for every prop and vehicle in this list that has none yet");
        _top.Controls.AddRange([_title, _generate, _scope, _view, _search, _dlc, _gender, _chips]);
        _top.Layout += (_, _) => LayoutTop();

        var content = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
        content.Controls.Add(_grid);
        content.Controls.Add(_dlcTable);
        content.Controls.Add(_top);
        content.Controls.Add(_toast);

        Controls.Add(content);
        Controls.Add(_nav);
    }

    /// <summary>Title row (title, scope, view) / search row (search, DLC, gender) / chip row.</summary>
    private void LayoutTop()
    {
        var pad = Px(24);
        var w = _top.ClientSize.Width;
        var y = Px(20);

        _view.Location = new Point(w - pad - _view.Width, y + Px(4));
        _scope.Location = new Point(_view.Left - Px(10) - _scope.Width, y + Px(4));
        _generate.Height = _scope.Height;
        _generate.Location = new Point(_scope.Left - Px(10) - _generate.Width, y + Px(4));
        var titleRight = _generate.Visible ? _generate.Left : _scope.Visible ? _scope.Left : _view.Left;
        _title.SetBounds(pad, y, Math.Max(0, titleRight - pad * 2), Px(48));
        y += Px(62);

        if (_search.Visible)
        {
            var right = w - pad;
            if (_gender.Visible)
            {
                _gender.Location = new Point(right - _gender.Width, y);
                right = _gender.Left - Px(8);
            }
            _dlc.Location = new Point(right - _dlc.Width, y);
            right = _dlc.Left - Px(8);
            _search.SetBounds(pad, y, Math.Max(Px(200), right - pad), _search.Height);
            y += _search.Height + Px(12);
        }

        _chips.SetBounds(pad, y, w - pad * 2, _chips.Height);
        if (_chips.Visible)
            y += _chips.Height + Px(10);
        _top.Height = y + Px(4);
    }

    private void WireEvents()
    {
        _nav.SelectedChanged += (_, _) => SetCategory(_nav.Selected);
        _nav.ReindexClicked += (_, _) => Reindex(firstRun: false);
        _search.TextChanged += (_, _) =>
        {
            _state = _state with { Text = _search.Text };
            _searchTimer.Stop();
            _searchTimer.Start();
        };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode is Keys.Down or Keys.Enter)
            {
                RunSearchNow();
                _grid.Focus();
                e.SuppressKeyPress = true;
            }
        };
        _searchTimer.Tick += (_, _) => RunSearchNow();
        _dlc.SelectedChanged += (_, _) =>
        {
            _state = _state with { Dlc = _dlc.SelectedValue as string };
            RunSearchNow();
        };
        _gender.SelectedChanged += (_, _) =>
        {
            _state = _state with { Gender = (Gender)_gender.SelectedValue! };
            RunSearchNow();
        };
        _scope.SelectedChanged += (_, _) =>
        {
            _state = _state with { FavoritesOnly = _scope.Selected == 1 };
            RunSearchNow();
        };
        _view.SelectedChanged += (_, _) => _grid.Mode = _view.Selected == 0 ? ViewMode.Grid : ViewMode.List;
        _chips.Toggled += (_, tag) =>
        {
            _state = _state.ToggleTag(tag);
            RunSearchNow();
        };

        _generate.Click += (_, _) => ToggleGenerate();
        _grid.ItemActivated += (_, _) => OpenDetail();
        _grid.CopyRequested += (_, item) => Copy(AssetText.CopyText(item));
        _dlcTable.DlcActivated += (_, d) => OpenDlc(d);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_db.GetMeta(GameIndexer.FingerprintKey) is null && !Reindex(firstRun: true))
        {
            Close();
            return;
        }
        LoadCatalog();
        SetCategory(Category.Props);
        _grid.Focus();
        StartGameAssets();
    }

    /// <summary>
    /// Starts loading the game file cache for 3D previews; takes ~6 s, so it runs while the user browses, with
    /// progress in the sidebar.
    /// </summary>
    private async void StartGameAssets()
    {
        _nav.SetLoading("Loading 3D preview", "Opening game files");
        try
        {
            var open = Task.Run(() => GameSession.Open(_paths, _settings).Install);
            while (!open.IsCompleted)
            {
                _nav.Invalidate(); // animates the indeterminate bar
                await Task.Delay(50);
            }
            _assets = GameAssets.Start(await open, error => AppLog.Write("game", [error]));
            while (!_assets.Ready.IsCompleted && !IsDisposed)
            {
                _nav.SetLoading("Loading 3D preview", _assets.StageLabel, _assets.Progress?.Value);
                await Task.Delay(50);
            }
            await _assets.Ready;
            _nav.SetLoading(null);
            _thumbnails.AttachAssets(_assets);
            UpdateGenerateButton();
        }
        catch (Exception e)
        {
            AppLog.Write("game", [e.ToString()]);
            if (IsDisposed)
                return;
            _nav.SetLoading(null);
            _toast.Show(@"3D preview unavailable, see logs\game.log", Glyph.Clear);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _thumbnails.Dispose(); // uses the game cache: before it
        _assets?.Dispose();
        base.OnFormClosed(e);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                _search.FocusText();
                return true;
            case Keys.Control | Keys.L when !_state.Category.IsDlcList:
                _view.Selected = 1 - _view.Selected;
                return true;
            case Keys.Control | Keys.D when _grid.SelectedItem is { } fav:
                ToggleFavorite(fav);
                return true;
            case Keys.Control | Keys.C when _grid.Focused && _grid.SelectedItem is { } item:
                Copy(AssetText.CopyText(item));
                return true;
            case Keys.Escape when _search.TextFocused && _search.Text.Length > 0:
                _search.Clear();
                return true;
            case Keys.Escape when _search.TextFocused:
                _grid.Focus();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Loads what only changes after a reindex: per-kind counts, DLC list, index info.</summary>
    private void LoadCatalog()
    {
        _counts = _queries.CountsByKind();
        _taggedCounts = Category.Sidebar.Where(c => c.Tags.Count > 0)
            .ToDictionary(c => c, c => _queries.Search(new SearchQuery { Kind = c.Kind, Tags = c.Tags, Limit = 0 }).Total);
        var dlcs = _queries.Dlcs();

        _dlc.Items =
        [
            new PickerItem(null, "All"),
            .. dlcs.Where(d => d.CountsByKind.Count > 0)
                .OrderByDescending(d => d.OrderIndex)
                .Select(d => new PickerItem(d.Name, d.Name, d.CountsByKind.Values.Sum().ToString("N0"))),
        ];
        _dlc.Select(null);
        _state = _state with { Dlc = null };
        _dlcTable.SetDlcs(dlcs);

        var version = _db.GetMeta("game_version");
        var indexedAt = DateTime.TryParse(_db.GetMeta("indexed_at"), out var at) ? at.ToLocalTime().ToString("g") : "?";
        _nav.SetCounts(CategoryCount, $"{_counts.Values.Sum():N0} assets · game {version}\nIndexed {indexedAt}");
    }

    private int? CategoryCount(Category c) => c switch
    {
        { IsDlcList: true } => _dlcTable.Count,
        { Kind: null } => _counts.Values.Sum(),
        { Tags.Count: 0, Kind: { } k } => _counts.GetValueOrDefault(k),
        _ => _taggedCounts.TryGetValue(c, out var n) ? n : null,
    };

    private void SetCategory(Category category)
    {
        _state = _state.WithCategory(category);
        _nav.Selected = category;
        _grid.Visible = !category.IsDlcList;
        _dlcTable.Visible = category.IsDlcList;
        _search.Visible = _dlc.Visible = _scope.Visible = _view.Visible = !category.IsDlcList;
        _gender.Visible = category.HasGender;
        _gender.Select(_state.Gender);
        UpdateGenerateButton();
        _scope.Selected = 0;
        _grid.EmptyText = "No results";
        LoadChips();
        _top.PerformLayout();

        if (category.IsDlcList)
        {
            _searchTimer.Stop();
            _generation++;
            _title.Set("DLCs", $"{_dlcTable.Count} packs · click a column to sort · double-click to browse a pack");
            _dlcTable.Focus();
        }
        else
        {
            RunSearchNow();
            UpdateFavoriteCount();
        }
    }

    private void LoadChips()
    {
        var chips = new List<Chip>();
        if (_state.Category.Kind is { } kind && !_state.Category.IsDlcList)
        {
            var hidden = new HashSet<string>(_state.Category.Tags, StringComparer.OrdinalIgnoreCase);
            if (_state.Category.HasGender)
                hidden.UnionWith(["Male", "Female"]);
            chips.AddRange(_queries.TopTags(kind, 30).Where(t => !hidden.Contains(t.Tag)).Take(24)
                .Select(t => new Chip(t.Tag, $"{t.Count:N0} assets")));
        }
        _chips.SetChips(chips, _state.Tags);
        _chips.Visible = chips.Count > 0;
    }

    private void UpdateFavoriteCount()
    {
        var n = _queries.Search(new BrowseState { Category = _state.Category, FavoritesOnly = true }.ToQuery(limit: 1)).Total;
        _scope.SetText(1, n > 0 ? $"Favorites  {n:N0}" : "Favorites");
        _top.PerformLayout();
    }

    private async void RunSearchNow()
    {
        _searchTimer.Stop();
        if (_state.Category.IsDlcList)
            return;

        var generation = ++_generation;
        var state = _state;
        var sw = Stopwatch.StartNew();
        ResultPager pager;
        try
        {
            pager = await Task.Run(() => ResultPager.Load(_queries.Search, state.ToQuery()));
        }
        catch (Exception e)
        {
            if (generation == _generation)
                _toast.Show("Search failed: " + e.Message, Glyph.Clear);
            AppLog.Write("app", [e.ToString()]);
            return;
        }

        // A newer search started while this one ran: drop the stale result.
        if (generation != _generation)
            return;
        _grid.EmptyText = state.FavoritesOnly ? "No favorites here yet — press Ctrl+D on an asset" : "No results";
        _grid.SetItems(pager);
        var noun = pager.Total == 1 ? "result" : "results";
        _title.Set(state.Category.Title, $"{pager.Total:N0} {noun} · {sw.ElapsedMilliseconds} ms");
    }

    private void OpenDetail()
    {
        if (_grid.SelectedIndex < 0)
            return;
        var favoritesChanged = false;
        using var form = new DetailForm(_queries, _assets, _grid.ItemAt, _grid.Count, _grid.SelectedIndex);
        form.FavoritesChanged += (_, _) => favoritesChanged = true;
        _thumbnails.Paused = true; // the viewport gets the file cache and GPU to itself
        try
        {
            form.ShowDialog(this);
        }
        finally
        {
            _thumbnails.Paused = false;
        }
        _grid.SelectedIndex = form.Index;
        if (favoritesChanged)
            AfterFavoritesChanged();
        _grid.Focus();
    }

    /// <summary>"Generate thumbnails" shows where the list can hold props or vehicles, once the renderer is up.</summary>
    private void UpdateGenerateButton()
    {
        var kind = _state.Category.Kind;
        var visible = _thumbnails.CanRender && !_state.Category.IsDlcList && (kind is null || ThumbnailStore.Supports(kind));
        if (visible == _generate.Visible)
            return;
        _generate.Visible = visible;
        _top.PerformLayout();
    }

    /// <summary>Starts rendering every missing thumbnail of the current result (props and vehicles), or stops it.</summary>
    private async void ToggleGenerate()
    {
        if (_thumbnails.Generating is not null)
        {
            _thumbnails.StopGenerating();
            return;
        }

        var query = _state.ToQuery() with { Offset = 0, Limit = int.MaxValue };
        var kinds = query.Kind is { } k ? [k] : new[] { AssetKind.Prop, AssetKind.Vehicle };
        var keys = await Task.Run(() => kinds.Where(ThumbnailStore.Supports)
            .SelectMany(kind => _queries.Search(query with { Kind = kind }).Items)
            .Select(ThumbKey.Of)
            .ToList());
        if (keys.Count == 0)
            return;
        _thumbnails.GenerateAll(keys);
        _generate.Icon = Glyph.Stop;
        _generate.Text = "Stop thumbnails";
        _top.PerformLayout();
        while (_thumbnails.Generating is { } p && !IsDisposed)
        {
            _nav.SetLoading("Generating thumbnails", $"{p.Done:N0} of {p.Total:N0}", (double)p.Done / p.Total);
            await Task.Delay(250);
        }
        if (IsDisposed)
            return;
        _nav.SetLoading(null);
        _generate.Icon = Glyph.Photo;
        _generate.Text = "Generate thumbnails";
        _top.PerformLayout();
    }

    private void ToggleFavorite(AssetSummary item)
    {
        var favorite = !_queries.IsFavorite(item.Kind, item.Key);
        _queries.SetFavorite(item.Kind, item.Key, favorite);
        _toast.Show(favorite ? $"Added {item.Name} to favorites" : $"Removed {item.Name} from favorites", favorite ? Glyph.StarFilled : Glyph.Star);
        AfterFavoritesChanged();
    }

    private void AfterFavoritesChanged()
    {
        UpdateFavoriteCount();
        if (_state.FavoritesOnly)
            RunSearchNow();
    }

    private void OpenDlc(DlcSummary d)
    {
        SetCategory(Category.All);
        _dlc.Select(d.Name);
        _state = _state with { Dlc = d.Name };
        _top.PerformLayout();
        RunSearchNow();
        _grid.Focus();
    }

    private void Copy(string text)
    {
        if (Clipboard2.TrySet(text))
            _toast.Show($"Copied {text}");
        else
            _toast.Show("Clipboard is busy, try again", Glyph.Clear);
    }

    private bool Reindex(bool firstRun)
    {
        using var form = new IndexForm(_paths, _db, _settings, firstRun);
        if (form.ShowDialog(this) != DialogResult.OK)
            return !firstRun;
        if (!firstRun)
        {
            LoadCatalog();
            SetCategory(_state.Category);
        }
        if (form.Result is { Skipped: false } r)
            _toast.Show($"Indexed {r.Assets:N0} assets");
        return true;
    }

    /// <summary>Large page title with a muted line next to it (result count, timing).</summary>
    private sealed class PageTitle : ThemedControl
    {
        private string _title = "";
        private string _subtitle = "";

        public void Set(string title, string subtitle)
        {
            _title = title;
            _subtitle = subtitle;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var titleWidth = TextRenderer.MeasureText(_title, Theme.PageTitle).Width;
            TextRenderer.DrawText(g, _title, Theme.PageTitle, new Rectangle(0, 0, titleWidth + Px(4), Height), Theme.Text, Theme.Line);
            TextRenderer.DrawText(g, _subtitle, Theme.UiFont, new Rectangle(titleWidth + Px(12), Px(4), Width - titleWidth - Px(12), Height),
                Theme.Muted, Theme.Line);
        }
    }
}
