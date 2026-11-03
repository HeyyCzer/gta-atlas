using GtaAtlas.App.Browsing;
using GtaAtlas.App.Ui.Controls;
using GtaAtlas.Data;

namespace GtaAtlas.App.Ui;

/// <summary>
/// Modal detail window for one asset: preview on the left (3D viewport in Phase 3), info on the right.
/// ←/→ step through the current result list, Esc closes.
/// </summary>
internal sealed class DetailForm : Form
{
    private readonly AssetQueries _queries;
    private readonly Func<int, AssetSummary?> _itemAt;
    private readonly int _count;

    private readonly PreviewPane _preview = new() { Dock = DockStyle.Fill };
    private readonly DetailHeader _header = new() { Dock = DockStyle.Top };
    private readonly PillButton _favorite = new() { Icon = Glyph.Star, Text = "Favorite" };
    private readonly PillButton _copyName = new() { Icon = Glyph.Copy, Text = "Copy name", Primary = true };
    private readonly DetailView _info = new() { Dock = DockStyle.Fill };
    private readonly Toast _toast = new();

    private AssetDetail? _detail;
    private bool _isFavorite;

    public DetailForm(AssetQueries queries, Func<int, AssetSummary?> itemAt, int count, int index)
    {
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

    /// <summary>Left area: preview placeholder until the 3D viewport lands, position and prev/next.</summary>
    private sealed class PreviewPane : ThemedControl
    {
        private AssetSummary? _item;
        private int _index;
        private int _count;
        private int _hoverArrow;

        public event EventHandler<int>? Navigate;

        public void Display(AssetSummary item, int index, int count)
        {
            _item = item;
            _index = index;
            _count = count;
            Invalidate();
        }

        private Rectangle Stage => new(Px(24), Px(24), Width - Px(48), Height - Px(80));
        private Rectangle Arrow(int dir) => new(dir < 0 ? Stage.X + Px(14) : Stage.Right - Px(54), Stage.Y + Stage.Height / 2 - Px(20), Px(40), Px(40));

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
            var icon = new Rectangle(stage.X, stage.Y + stage.Height / 2 - Px(70), stage.Width, Px(100));
            using (var big = new Font(Theme.IconLarge.FontFamily, 54f))
                Theme.DrawIcon(g, AssetGridView.KindGlyph(_item.Kind), icon, Color.FromArgb(0xA0, color), big);
            TextRenderer.DrawText(g, "3D preview coming soon", Theme.UiFont, new Rectangle(stage.X, icon.Bottom + Px(8), stage.Width, Px(24)), Theme.Faint, Theme.Centered);

            foreach (var dir in new[] { -1, 1 })
            {
                var enabled = dir < 0 ? _index > 0 : _index < _count - 1;
                if (!enabled)
                    continue;
                var r = Arrow(dir);
                Theme.FillRounded(g, r, r.Height / 2, _hoverArrow == dir ? Theme.Hover : Theme.Raised, Theme.Border);
                Theme.DrawIcon(g, dir < 0 ? Glyph.ChevronLeft : Glyph.ChevronRight, r, Theme.Text);
            }

            var footer = new Rectangle(stage.X, stage.Bottom + Px(12), stage.Width, Px(32));
            TextRenderer.DrawText(g, $"{_index + 1:N0} of {_count:N0}", Theme.Bold, footer, Theme.Muted, Theme.Line);
            TextRenderer.DrawText(g, "←  →  browse     Ctrl+C  copy name     Ctrl+D  favorite     Esc  close", Theme.Small, footer, Theme.Faint,
                Theme.Line | TextFormatFlags.Right);
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
