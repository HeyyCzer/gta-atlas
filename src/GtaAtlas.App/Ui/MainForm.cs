using System.Diagnostics;
using GtaAtlas.App.Browsing;
using GtaAtlas.Core;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

namespace GtaAtlas.App.Ui;

internal sealed class MainForm : Form
{
    private const int SearchDelayMs = 150;

    private readonly AppPaths _paths;
    private readonly AtlasSettings _settings;
    private readonly AtlasDb _db;
    private readonly AssetQueries _queries;

    private readonly ListBox _sidebar = new()
    {
        Dock = DockStyle.Left,
        DrawMode = DrawMode.OwnerDrawFixed,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel,
        IntegralHeight = false,
    };
    private readonly TextBox _search = new() { PlaceholderText = "Search name, display name, tag or hash  (Ctrl+F)", Dock = DockStyle.Fill };
    private readonly ComboBox _dlc = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 240, FormattingEnabled = true };
    private readonly ComboBox _gender = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110, FormattingEnabled = true };
    private readonly Button _viewToggle = new() { FlatStyle = FlatStyle.Flat, AutoSize = true };
    private readonly Button _reindex = new() { FlatStyle = FlatStyle.Flat, AutoSize = true, Text = "Reindex…" };
    private readonly FlowLayoutPanel _chips = new() { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = false, Padding = new Padding(0, 2, 0, 0) };
    private readonly AssetGridView _grid = new() { Dock = DockStyle.Fill };
    private readonly ListView _dlcList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Back,
        ForeColor = Theme.Text,
        Visible = false,
    };
    private readonly DetailView _detail = new() { Dock = DockStyle.Right };
    private readonly ToolStripStatusLabel _status = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _indexInfo = new() { ForeColor = Theme.Muted };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = SearchDelayMs };
    private readonly ToolTip _toolTip = new();

    private BrowseState _state = new();
    private IReadOnlyDictionary<string, int> _counts = new Dictionary<string, int>();
    private int _generation;
    private bool _updatingFilters;

    public MainForm(AppPaths paths, AtlasSettings settings, AtlasDb db)
    {
        _paths = paths;
        _settings = settings;
        _db = db;
        _queries = new AssetQueries(db);

        Text = "GTA Atlas";
        Icon = AppIcon.Get();
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.UiFont;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        ClientSize = new Size(1400, 860);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;

        BuildLayout();
        WireEvents();
    }

    private void BuildLayout()
    {
        _sidebar.Width = LogicalToDeviceUnits(190);
        _sidebar.ItemHeight = LogicalToDeviceUnits(32);
        _sidebar.Items.AddRange([.. Category.Sidebar]);

        _gender.Items.AddRange([Gender.Any, Gender.Male, Gender.Female]);
        _gender.SelectedIndex = 0;
        _dlc.Format += (_, e) => e.Value = e.ListItem is DlcSummary d ? DlcLabel(d) : e.ListItem;
        _gender.Format += (_, e) => e.Value = e.ListItem is Gender.Any ? "Any gender" : e.ListItem?.ToString();

        var bar = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 5, RowCount = 2, Padding = new Padding(10, 8, 10, 4), BackColor = Theme.Back };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 4; i++)
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.Controls.Add(_search, 0, 0);
        bar.Controls.Add(_dlc, 1, 0);
        bar.Controls.Add(_gender, 2, 0);
        bar.Controls.Add(_viewToggle, 3, 0);
        bar.Controls.Add(_reindex, 4, 0);
        bar.Controls.Add(_chips, 0, 1);
        bar.SetColumnSpan(_chips, 5);
        _chips.Height = LogicalToDeviceUnits(32);
        _search.Margin = new Padding(0, 4, 8, 4);
        _toolTip.SetToolTip(_viewToggle, "Toggle grid / list (Ctrl+L)");

        foreach (var (title, width) in new[] { ("#", 44), ("Pack", 170), ("Title", 200), ("Props", 70), ("Vehicles", 70), ("Peds", 60), ("Clothes", 70), ("Anims", 80), ("MLOs", 60), ("YMAPs", 70), ("Total", 80) })
            _dlcList.Columns.Add(title, LogicalToDeviceUnits(width), title is "#" or "Pack" or "Title" ? HorizontalAlignment.Left : HorizontalAlignment.Right);

        _detail.Width = LogicalToDeviceUnits(400);
        var detailSplitter = new Splitter { Dock = DockStyle.Right, Width = 4, BackColor = Theme.Border };
        var status = new StatusStrip { SizingGrip = true };
        status.Items.AddRange([_status, _indexInfo]);

        // Last added docks outermost: sidebar spans full height, status the full width.
        Controls.Add(_grid);
        Controls.Add(_dlcList);
        Controls.Add(detailSplitter);
        Controls.Add(_detail);
        Controls.Add(bar);
        Controls.Add(_sidebar);
        Controls.Add(status);
        UpdateViewToggle();
    }

    private void WireEvents()
    {
        _sidebar.DrawItem += DrawSidebarItem;
        _sidebar.SelectedIndexChanged += (_, _) =>
        {
            if (_sidebar.SelectedItem is Category c && c != _state.Category)
                SetCategory(c);
        };
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
        _dlc.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFilters)
                return;
            _state = _state with { Dlc = (_dlc.SelectedItem as DlcSummary)?.Name };
            RunSearchNow();
        };
        _gender.SelectedIndexChanged += (_, _) =>
        {
            if (_updatingFilters)
                return;
            _state = _state with { Gender = (Gender)_gender.SelectedItem! };
            RunSearchNow();
        };
        _viewToggle.Click += (_, _) => ToggleView();
        _reindex.Click += (_, _) => Reindex(firstRun: false);

        _grid.SelectionChanged += (_, _) => ShowDetail(_grid.SelectedItem);
        _grid.ItemActivated += (_, _) => _detail.FocusFields();
        _grid.CopyRequested += (_, item) => Copy(AssetText.CopyText(item));
        _detail.Copied += (_, text) => Copy(text);
        _detail.FavoriteToggled += (_, favorite) => SetFavorite(favorite);
        _dlcList.DoubleClick += (_, _) => OpenSelectedDlc();
        _dlcList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
                OpenSelectedDlc();
        };
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
        _sidebar.SelectedItem = Category.Props;
        SetCategory(Category.Props);
        _grid.Focus();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Control | Keys.F:
                _search.Focus();
                _search.SelectAll();
                return true;
            case Keys.Control | Keys.L:
                ToggleView();
                return true;
            case Keys.Control | Keys.D when _grid.SelectedItem is not null:
                SetFavorite(!_queries.IsFavorite(_grid.SelectedItem.Kind, _grid.SelectedItem.Key));
                return true;
            case Keys.Control | Keys.C when _grid.Focused && _grid.SelectedItem is { } item:
                Copy(AssetText.CopyText(item));
                return true;
            case Keys.Escape when _search.Focused && _search.TextLength > 0:
                _search.Clear();
                return true;
            case Keys.Escape when _detail.ContainsFocus:
                _grid.Focus();
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Loads what only changes after a reindex: per-kind counts, DLC list, index info.</summary>
    private void LoadCatalog()
    {
        _counts = _queries.CountsByKind();
        var dlcs = _queries.Dlcs();

        _updatingFilters = true;
        _dlc.Items.Clear();
        _dlc.Items.Add("All DLCs");
        foreach (var d in dlcs.Where(d => d.CountsByKind.Count > 0))
            _dlc.Items.Add(d);
        _dlc.SelectedIndex = 0;
        _state = _state with { Dlc = null };
        _updatingFilters = false;

        _dlcList.BeginUpdate();
        _dlcList.Items.Clear();
        foreach (var d in dlcs)
        {
            string N(params string[] kinds) => kinds.Sum(k => d.CountsByKind.GetValueOrDefault(k)).ToString("N0");
            _dlcList.Items.Add(new ListViewItem(
            [
                d.OrderIndex.ToString(), d.Name, d.Title ?? "",
                N(AssetKind.Prop), N(AssetKind.Vehicle), N(AssetKind.Ped), N(AssetKind.Clothing),
                N(AssetKind.Anim), N(AssetKind.Mlo), N(AssetKind.Ymap), d.CountsByKind.Values.Sum().ToString("N0"),
            ]) { Tag = d });
        }
        // Packs only have titles when the game text names them; hide the column otherwise.
        _dlcList.Columns[2].Width = dlcs.Any(d => !string.IsNullOrEmpty(d.Title)) ? LogicalToDeviceUnits(200) : 0;
        _dlcList.EndUpdate();

        var version = _db.GetMeta("game_version");
        var indexedAt = DateTime.TryParse(_db.GetMeta("indexed_at"), out var at) ? at.ToLocalTime().ToString("g") : "?";
        _indexInfo.Text = $"{_counts.Values.Sum():N0} assets · game {version} · indexed {indexedAt}";
        _sidebar.Invalidate();
    }

    private static string DlcLabel(DlcSummary d) => d.Title is { Length: > 0 } t ? $"{d.Name} — {t}" : d.Name;

    private void SetCategory(Category category)
    {
        _state = _state.WithCategory(category);
        _grid.Visible = !category.IsDlcList;
        _dlcList.Visible = category.IsDlcList;
        _gender.Visible = category.HasGender;
        _updatingFilters = true;
        _gender.SelectedItem = _state.Gender;
        _updatingFilters = false;
        LoadChips();
        if (category.IsDlcList)
        {
            _searchTimer.Stop();
            _status.Text = $"{_dlcList.Items.Count} packs in load order · double-click to browse a pack";
            ShowDetail(null);
        }
        else
        {
            RunSearchNow();
        }
    }

    private void LoadChips()
    {
        _chips.SuspendLayout();
        foreach (Control c in _chips.Controls)
            c.Dispose();
        _chips.Controls.Clear();
        if (_state.Category.Kind is { } kind)
        {
            var hidden = new HashSet<string>(_state.Category.Tags, StringComparer.OrdinalIgnoreCase);
            if (_state.Category.HasGender)
                hidden.UnionWith(["Male", "Female"]);
            foreach (var (tag, count) in _queries.TopTags(kind, 24).Where(t => !hidden.Contains(t.Tag)).Take(16))
            {
                var chip = new CheckBox
                {
                    Text = tag,
                    Appearance = Appearance.Button,
                    FlatStyle = FlatStyle.Flat,
                    AutoSize = true,
                    Margin = new Padding(0, 0, 6, 0),
                    Checked = _state.Tags.Contains(tag),
                };
                chip.FlatAppearance.BorderColor = Theme.Border;
                chip.FlatAppearance.CheckedBackColor = Theme.Selected;
                _toolTip.SetToolTip(chip, $"{count:N0} assets");
                chip.CheckedChanged += (_, _) =>
                {
                    _state = _state.ToggleTag(tag);
                    RunSearchNow();
                };
                _chips.Controls.Add(chip);
            }
        }
        _chips.ResumeLayout();
    }

    private async void RunSearchNow()
    {
        _searchTimer.Stop();
        if (_state.Category.IsDlcList)
            return;

        var generation = ++_generation;
        var query = _state.ToQuery();
        var sw = Stopwatch.StartNew();
        ResultPager pager;
        try
        {
            pager = await Task.Run(() => ResultPager.Load(_queries.Search, query));
        }
        catch (Exception e)
        {
            if (generation == _generation)
                _status.Text = "Search failed: " + e.Message;
            AppLog.Write("app", [e.ToString()]);
            return;
        }

        // A newer search started while this one ran: drop the stale result.
        if (generation != _generation)
            return;
        _grid.SetItems(pager);
        _status.Text = $"{pager.Total:N0} {(pager.Total == 1 ? "result" : "results")} · {sw.ElapsedMilliseconds} ms";
    }

    private void ShowDetail(AssetSummary? item)
    {
        if (item is null)
        {
            _detail.Display(null, false);
            return;
        }
        _detail.Display(_queries.Get(item.Id), _queries.IsFavorite(item.Kind, item.Key));
    }

    private void SetFavorite(bool favorite)
    {
        if (_grid.SelectedItem is not { } item)
            return;
        _queries.SetFavorite(item.Kind, item.Key, favorite);
        _status.Text = favorite ? $"Added {item.Name} to favorites" : $"Removed {item.Name} from favorites";
        if (_state.Category.FavoritesOnly)
            RunSearchNow();
        else
            ShowDetail(item);
    }

    private void OpenSelectedDlc()
    {
        if (_dlcList.SelectedItems.Count == 0 || _dlcList.SelectedItems[0].Tag is not DlcSummary d)
            return;
        _sidebar.SelectedItem = Category.All;
        var index = _dlc.Items.IndexOf(d);
        _dlc.SelectedIndex = index >= 0 ? index : 0;
        _grid.Focus();
    }

    private void ToggleView()
    {
        _grid.Mode = _grid.Mode == ViewMode.Grid ? ViewMode.List : ViewMode.Grid;
        UpdateViewToggle();
    }

    private void UpdateViewToggle() => _viewToggle.Text = _grid.Mode == ViewMode.Grid ? "▦ Grid" : "☰ List";

    private void Copy(string text)
    {
        try
        {
            Clipboard.SetText(text);
            _status.Text = $"Copied: {text}";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _status.Text = "Clipboard is busy, try again.";
        }
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
        _status.Text = form.Result is { Skipped: false } r ? $"Indexed {r.Assets:N0} assets." : "Index is up to date.";
        return true;
    }

    private void DrawSidebarItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || _sidebar.Items[e.Index] is not Category c)
            return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(selected ? Theme.Selected : Theme.Panel))
            e.Graphics.FillRectangle(back, e.Bounds);
        if (selected)
        {
            using var accent = new SolidBrush(Theme.Accent);
            e.Graphics.FillRectangle(accent, e.Bounds.X, e.Bounds.Y, LogicalToDeviceUnits(3), e.Bounds.Height);
        }

        var pad = LogicalToDeviceUnits(14);
        var bounds = Rectangle.Inflate(e.Bounds, -pad, 0);
        TextRenderer.DrawText(e.Graphics, c.Title, selected ? Theme.Bold : Theme.UiFont, bounds, Theme.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        if (CategoryCount(c) is { } n)
            TextRenderer.DrawText(e.Graphics, n.ToString("N0"), Theme.Small, bounds, Theme.Muted,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Count shown next to a sidebar entry when it is a whole kind (filtered categories show none).</summary>
    private int? CategoryCount(Category c) => c switch
    {
        { IsDlcList: true } => null,
        { FavoritesOnly: true } => null,
        { Kind: null } => _counts.Values.Sum(),
        { Tags.Count: 0, Kind: { } k } => _counts.GetValueOrDefault(k),
        _ => null,
    };
}
