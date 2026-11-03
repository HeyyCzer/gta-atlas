using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
using GtaAtlas.Data;
using GtaAtlas.Render;

namespace GtaAtlas.App.Ui;

/// <summary>
/// Modal detail window for one asset: 3D preview on the left (props and vehicles), info on the right.
/// ←/→ step through the current result list, Esc closes.
/// </summary>
internal sealed class DetailForm : Form
{
    private readonly AssetQueries _queries;
    private readonly Func<int, AssetSummary?> _itemAt;
    private readonly int _count;

    private readonly PreviewPane _preview;
    private readonly DetailHeader _header = new() { Dock = DockStyle.Top };
    private readonly PillButton _favorite = new() { Icon = Glyph.Star, Text = "Favorite" };
    private readonly PillButton _copyName = new() { Icon = Glyph.Copy, Text = "Copy name", Primary = true };
    private readonly DetailView _info = new() { Dock = DockStyle.Fill };
    private readonly Toast _toast = new();

    private AssetDetail? _detail;
    private bool _isFavorite;

    public DetailForm(AssetQueries queries, GameAssets? assets, Func<int, AssetSummary?> itemAt, int count, int index)
    {
        _preview = new PreviewPane(assets) { Dock = DockStyle.Fill };
        _queries = queries;
        _itemAt = itemAt;
        _count = count;
        Index = index;

        Text = "GTA Atlas";
        Icon = AppIcon.Get();
        Theme.ApplyWindowChrome(this);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.UiFont;
        BackColor = Theme.Back;
        ForeColor = Theme.Text;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;
        KeyPreview = true;
        ClientSize = new Size(1180, 720);
        MinimumSize = new Size(820, 520);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(58),
            Padding = new Padding(LogicalToDeviceUnits(22), 0, 0, LogicalToDeviceUnits(14)),
            BackColor = Theme.Surface,
            WrapContents = false,
        };
        _favorite.BackColor = _copyName.BackColor = Theme.Surface;
        _favorite.Margin = new Padding(0, 0, LogicalToDeviceUnits(8), 0);
        actions.Controls.AddRange([_copyName, _favorite]);

        var side = new Panel { Dock = DockStyle.Right, Width = LogicalToDeviceUnits(470), BackColor = Theme.Surface };
        side.Controls.Add(_info);
        side.Controls.Add(new Divider { Dock = DockStyle.Top });
        side.Controls.Add(actions);
        side.Controls.Add(_header);

        Controls.Add(_preview);
        Controls.Add(side);
        Controls.Add(_toast);

        _favorite.Click += (_, _) => ToggleFavorite();
        _copyName.Click += (_, _) =>
        {
            if (_detail is not null)
                Copy(AssetText.CopyText(_detail.Asset));
        };
        _info.Copied += (_, text) => Copy(text);
        _preview.Navigate += (_, delta) => Step(delta);

        ShowAsset(index);
    }

    /// <summary>Position in the result list currently shown; the main grid follows it on close.</summary>
    public int Index { get; private set; }

    /// <summary>Raised after the user toggles favorite, so lists filtered by favorites can refresh.</summary>
    public event EventHandler? FavoritesChanged;

    private void ShowAsset(int index)
    {
        if (_itemAt(index) is not { } item)
            return;
        Index = index;
        _detail = _queries.Get(item.Id);
        _isFavorite = _queries.IsFavorite(item.Kind, item.Key);
        Text = $"{item.Name} — GTA Atlas";
        _header.Display(item);
        _preview.Display(item, index, _count);
        _info.Display(_detail);
        UpdateFavorite();
    }

    private void Step(int delta)
    {
        var next = Index + delta;
        if (next >= 0 && next < _count)
            ShowAsset(next);
    }

    private void ToggleFavorite()
    {
        if (_detail is null)
            return;
        _isFavorite = !_isFavorite;
        _queries.SetFavorite(_detail.Asset.Kind, _detail.Asset.Key, _isFavorite);
        UpdateFavorite();
        _toast.Show(_isFavorite ? "Added to favorites" : "Removed from favorites", _isFavorite ? Glyph.StarFilled : Glyph.Star);
        FavoritesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateFavorite()
    {
        _favorite.Icon = _isFavorite ? Glyph.StarFilled : Glyph.Star;
        _favorite.IconColor = _isFavorite ? Theme.Star : null;
        _favorite.Text = _isFavorite ? "Favorited" : "Favorite";
    }

    private void Copy(string text)
    {
        if (Clipboard2.TrySet(text))
            _toast.Show($"Copied {text}");
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        switch (keyData)
        {
            case Keys.Escape:
                Close();
                return true;
            case Keys.Left or Keys.Up or Keys.PageUp:
                Step(-1);
                return true;
            case Keys.Right or Keys.Down or Keys.PageDown:
                Step(1);
                return true;
            case Keys.Control | Keys.C when _detail is not null:
                Copy(AssetText.CopyText(_detail.Asset));
                return true;
            case Keys.Control | Keys.D:
                ToggleFavorite();
                return true;
            case Keys.W or Keys.B or Keys.S or Keys.L when _preview.HandleKey(keyData):
                return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    /// <summary>Kind, name (wrapping) and subtitle at the top of the info column.</summary>
    private sealed class DetailHeader : ThemedControl
    {
        private AssetSummary? _item;

        public DetailHeader()
        {
            BackColor = Theme.Surface;
            Height = Px(130);
        }

        public void Display(AssetSummary item)
        {
            _item = item;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_item is null)
                return;

            var pad = Px(24);
            var color = Theme.KindColor(_item.Kind);
            var kind = AssetText.KindTitle(_item.Kind).ToUpperInvariant();
            var kindWidth = TextRenderer.MeasureText(kind, Theme.Caption).Width + Px(30);
            var pill = new Rectangle(pad, Px(22), kindWidth, Px(22));
            Theme.FillRounded(g, pill, Px(6), Color.FromArgb(0x26, color));
            Theme.DrawIcon(g, AssetGridView.KindGlyph(_item.Kind), new Rectangle(pill.X + Px(4), pill.Y, Px(16), pill.Height), color, Theme.IconSmall);
            TextRenderer.DrawText(g, kind, Theme.Caption, new Rectangle(pill.X + Px(20), pill.Y, pill.Width - Px(22), pill.Height), color, Theme.Line);

            var dlcWidth = TextRenderer.MeasureText(_item.Dlc, Theme.Caption).Width + Px(14);
            var dlc = new Rectangle(pill.Right + Px(6), pill.Y, dlcWidth, pill.Height);
            Theme.FillRounded(g, dlc, Px(6), Theme.Hover);
            TextRenderer.DrawText(g, _item.Dlc, Theme.Caption, dlc, Theme.Muted, Theme.Centered);

            var name = new Rectangle(pad, Px(54), Width - pad * 2, Px(34));
            TextRenderer.DrawText(g, _item.Name, Theme.Title, name, Theme.Text, Theme.Line);
            if (AssetText.Subtitle(_item) is { } sub)
                TextRenderer.DrawText(g, sub, Theme.UiFont, new Rectangle(pad, Px(88), Width - pad * 2, Px(24)), Theme.Muted, Theme.Line);
        }
    }

    /// <summary>
    /// Left area: 3D viewport for kinds it can show (placeholder with status otherwise), view toolbar, position and
    /// prev/next. The viewport is a native D3D window that nothing can be painted over, so controls sit above and below it.
    /// </summary>
    private sealed class PreviewPane : ThemedControl
    {
        private static readonly string[] LodNames = ["High", "Medium", "Low", "Very low"];

        private readonly GameAssets? _assets;
        private readonly Segmented _lodPick = new();
        private readonly PillButton _wireframe = new() { Text = "Wireframe" };
        private readonly PillButton _bounds = new() { Text = "Bounds" };
        private readonly PillButton _skeleton = new() { Text = "Skeleton" };
        private readonly ToolTip _toolTip = new();
        private IReadOnlyList<DetailLevel> _lods = [DetailLevel.High];
        private DetailLevel _lod; // requested; kept while browsing models that lack it
        private bool _syncingLods;
        private ModelViewport? _viewport;
        private AssetSummary? _item;
        private int _index;
        private int _count;
        private int _hoverArrow;
        private string _message = "";

        public PreviewPane(GameAssets? assets)
        {
            _assets = assets;
            foreach (var c in Toolbar)
            {
                c.BackColor = Theme.Back;
                c.Height = Px(34);
                c.Visible = false;
                Controls.Add(c);
            }
            _toolTip.SetToolTip(_lodPick, "Level of detail (L)");
            _toolTip.SetToolTip(_wireframe, "Wireframe (W)");
            _toolTip.SetToolTip(_bounds, "Bounding box (B)");
            _toolTip.SetToolTip(_skeleton, "Skeleton bones (S)");
            _lodPick.SelectedChanged += (_, _) =>
            {
                if (!_syncingLods && _lodPick.Selected < _lods.Count)
                    SetLod(_lods[_lodPick.Selected]);
            };
            _wireframe.Click += (_, _) => Toggle(_wireframe);
            _bounds.Click += (_, _) => Toggle(_bounds);
            _skeleton.Click += (_, _) => Toggle(_skeleton);
        }

        private Control[] Toolbar => [_lodPick, _wireframe, _bounds, _skeleton];

        /// <summary>W/B/S toggle view options, L cycles the level of detail. False when no model is shown.</summary>
        public bool HandleKey(Keys key)
        {
            if (_viewport is not { Visible: true })
                return false;
            switch (key)
            {
                case Keys.W:
                    Toggle(_wireframe);
                    break;
                case Keys.B:
                    Toggle(_bounds);
                    break;
                case Keys.S:
                    Toggle(_skeleton);
                    break;
                case Keys.L:
                    var shown = _lods.ToList().IndexOf(ModelLods.Effective(_lods, _lod));
                    SetLod(_lods[(shown + 1) % _lods.Count]);
                    break;
            }
            return true;
        }

        private void Toggle(PillButton button)
        {
            button.Checked = !button.Checked;
            ApplyViewOptions();
        }

        private void SetLod(DetailLevel lod)
        {
            _lod = lod;
            ApplyViewOptions();
            SyncLodPick();
        }

        private void ApplyViewOptions()
        {
            if (_viewport is null)
                return;
            _viewport.Lod = _lod;
            _viewport.Wireframe = _wireframe.Checked;
            _viewport.ShowBounds = _bounds.Checked;
            _viewport.ShowSkeleton = _skeleton.Checked;
        }

        /// <summary>Lists the levels the current model has, with the one actually drawn selected.</summary>
        private void SyncLodPick()
        {
            _syncingLods = true;
            _lodPick.Segments = _lods.Select(l => new Segment(null, LodNames[(int)l])).ToList();
            _lodPick.Selected = _lods.ToList().IndexOf(ModelLods.Effective(_lods, _lod));
            _syncingLods = false;
            LayoutToolbar();
        }

        private void ShowToolbar(bool visible)
        {
            foreach (var c in Toolbar)
                c.Visible = visible;
        }

        private void LayoutToolbar()
        {
            var x = Stage.X;
            foreach (var c in Toolbar)
            {
                c.Location = new Point(x, Px(24));
                x += c.Width + Px(c == _lodPick ? 16 : 8);
            }
        }

        public event EventHandler<int>? Navigate;

        public void Display(AssetSummary item, int index, int count)
        {
            _item = item;
            _index = index;
            _count = count;
            ShowModel(item);
            Invalidate();
        }

        private void ShowModel(AssetSummary item)
        {
            var supported = item.Kind is AssetKind.Prop or AssetKind.Vehicle;
            if (!supported || _assets is null)
            {
                _message = supported ? "3D preview needs the game folder" : "3D preview coming soon";
                if (_viewport is not null)
                {
                    _viewport.Clear();
                    _viewport.Visible = false;
                }
                ShowToolbar(false);
                return;
            }

            if (_viewport is null)
            {
                _viewport = new ModelViewport(_assets, Rectangle.Inflate(Stage, -1, -1).Size) { Background = Theme.Surface };
                _viewport.ItemLoaded += (_, error) =>
                {
                    _message = $"Preview unavailable: {error}";
                    _viewport.Visible = error is null;
                    _lods = _viewport.AvailableLods;
                    SyncLodPick();
                    ShowToolbar(error is null);
                    Invalidate();
                };
                Controls.Add(_viewport);
                ApplyViewOptions();
                LayoutViewport();
            }
            _message = _assets.Ready.IsCompleted ? "Loading model…" : "Loading game files…";
            if (item.Kind == AssetKind.Vehicle)
                _viewport.ShowVehicle(item.Name);
            else
                _viewport.ShowProp(item.Hash);
            if (!_assets.Ready.IsCompleted)
                _ = RepaintWhileScanning();
        }

        /// <summary>Repaints the cache scan status until the game files are ready.</summary>
        private async Task RepaintWhileScanning()
        {
            while (_assets is { Ready.IsCompleted: false } && !IsDisposed)
            {
                Invalidate();
                await Task.Delay(250);
            }
        }

        private Rectangle Stage => new(Px(24), Px(70), Width - Px(48), Height - Px(126));
        private Rectangle Arrow(int dir) => new(Stage.X + (dir < 0 ? 0 : Px(40)), Stage.Bottom + Px(12), Px(34), Px(30));

        private void LayoutViewport()
        {
            if (_viewport is not null)
                _viewport.Bounds = Rectangle.Inflate(Stage, -1, -1);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            LayoutViewport();
            LayoutToolbar();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _viewport is not null)
            {
                _viewport.Close(); // releases the D3D device; a child form is not closed with its parent
                _viewport.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            if (_item is null)
                return;

            var stage = Stage;
            var color = Theme.KindColor(_item.Kind);
            using (var path = Theme.RoundedRect(stage, Px(16)))
            using (var brush = new System.Drawing.Drawing2D.LinearGradientBrush(stage, Color.FromArgb(0x1C, color), Color.FromArgb(0x04, color), 90f))
            {
                using var back = new SolidBrush(Theme.Surface);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillPath(back, path);
                g.FillPath(brush, path);
                using var pen = new Pen(Theme.Border);
                g.DrawPath(pen, path);
            }
            var showing = _viewport is { Visible: true };
            if (!showing)
            {
                var icon = new Rectangle(stage.X, stage.Y + stage.Height / 2 - Px(70), stage.Width, Px(100));
                using (var big = new Font(Theme.IconLarge.FontFamily, 54f))
                    Theme.DrawIcon(g, AssetGridView.KindGlyph(_item.Kind), icon, Color.FromArgb(0xA0, color), big);
                var text = _assets is { Ready.IsCompleted: false } && _viewport is not null ? $"{_message}\n{_assets.Status}" : _message;
                TextRenderer.DrawText(g, text, Theme.UiFont, new Rectangle(stage.X + Px(16), icon.Bottom + Px(8), stage.Width - Px(32), Px(48)), Theme.Faint,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }

            foreach (var dir in new[] { -1, 1 })
            {
                var r = Arrow(dir);
                var enabled = dir < 0 ? _index > 0 : _index < _count - 1;
                Theme.FillRounded(g, r, Px(8), enabled && _hoverArrow == dir ? Theme.Hover : Theme.Raised, Theme.Border);
                Theme.DrawIcon(g, dir < 0 ? Glyph.ChevronLeft : Glyph.ChevronRight, r, enabled ? Theme.Text : Theme.Faint);
            }

            var footer = new Rectangle(Arrow(1).Right + Px(12), stage.Bottom + Px(12), stage.Right - Arrow(1).Right - Px(12), Px(30));
            TextRenderer.DrawText(g, $"{_index + 1:N0} of {_count:N0}", Theme.Bold, footer, Theme.Muted, Theme.Line);
            var hints = showing
                ? "Drag  orbit     Right-drag  pan     Wheel  zoom     R  reset     ←  →  browse     Esc  close"
                : "←  →  browse     Ctrl+C  copy name     Ctrl+D  favorite     Esc  close";
            TextRenderer.DrawText(g, hints, Theme.Small, footer, Theme.Faint, Theme.Line | TextFormatFlags.Right);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var hover = Arrow(-1).Contains(e.Location) ? -1 : Arrow(1).Contains(e.Location) ? 1 : 0;
            if (hover != _hoverArrow)
            {
                _hoverArrow = hover;
                Cursor = hover != 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hoverArrow = 0;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (_hoverArrow != 0)
                Navigate?.Invoke(this, _hoverArrow);
            base.OnMouseDown(e);
        }
    }
}

/// <summary>1px horizontal rule.</summary>
internal sealed class Divider : Control
{
    public Divider()
    {
        Height = 1;
        BackColor = Theme.Border;
    }
}

internal static class Clipboard2
{
    /// <summary>Clipboard.SetText, tolerating another process holding the clipboard.</summary>
    public static bool TrySet(string text)
    {
        try
        {
            Clipboard.SetText(text);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }
}
