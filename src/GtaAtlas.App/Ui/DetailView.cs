using GtaAtlas.App.Browsing;
using GtaAtlas.Data;

namespace GtaAtlas.App.Ui;

/// <summary>Right-hand panel: identity (name and hash in every notation), tags, kind-specific data and source files.</summary>
internal sealed class DetailView : UserControl
{
    private readonly Label _title = new() { AutoSize = false, Dock = DockStyle.Top, Font = Theme.Title, ForeColor = Theme.Text, AutoEllipsis = true };
    private readonly Label _subtitle = new() { AutoSize = false, Dock = DockStyle.Top, ForeColor = Theme.Muted, AutoEllipsis = true };
    private readonly Button _favorite = new() { AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button _copyName = new() { AutoSize = true, FlatStyle = FlatStyle.Flat, Text = "Copy name" };
    private readonly ListView _fields = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        HeaderStyle = ColumnHeaderStyle.None,
        BorderStyle = BorderStyle.None,
        BackColor = Theme.Panel,
        ForeColor = Theme.Text,
        ShowItemToolTips = true,
    };

    private AssetDetail? _detail;
    private bool _isFavorite;

    public DetailView()
    {
        BackColor = Theme.Panel;
        Padding = new Padding(12);

        _fields.Columns.Add("Field", 110);
        _fields.Columns.Add("Value", 240);
        _fields.DoubleClick += (_, _) => CopySelectedField();
        _fields.KeyDown += (_, e) =>
        {
            if (e is { Control: true, KeyCode: Keys.C })
            {
                CopySelectedField();
                e.Handled = true;
            }
        };
        _fields.Resize += (_, _) => _fields.Columns[1].Width = Math.Max(80, _fields.ClientSize.Width - _fields.Columns[0].Width);

        _favorite.Click += (_, _) =>
        {
            if (_detail is null)
                return;
            _isFavorite = !_isFavorite;
            UpdateFavoriteButton();
            FavoriteToggled?.Invoke(this, _isFavorite);
        };
        _copyName.Click += (_, _) =>
        {
            if (_detail is not null)
                Copied?.Invoke(this, AssetText.CopyText(_detail.Asset));
        };

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 6, 0, 6), WrapContents = false };
        buttons.Controls.AddRange([_favorite, _copyName]);

        // Docked top controls stack in reverse add order.
        Controls.Add(_fields);
        Controls.Add(buttons);
        Controls.Add(_subtitle);
        Controls.Add(_title);
        _title.Height = LogicalToDeviceUnits(30);
        _subtitle.Height = LogicalToDeviceUnits(20);
        Display(null, false);
    }

    /// <summary>Raised with the text the user asked to copy.</summary>
    public event EventHandler<string>? Copied;

    public event EventHandler<bool>? FavoriteToggled;

    public AssetDetail? Detail => _detail;

    public void Display(AssetDetail? detail, bool favorite)
    {
        _detail = detail;
        _isFavorite = favorite;
        _favorite.Visible = _copyName.Visible = _fields.Visible = detail is not null;
        UpdateFavoriteButton();

        _fields.BeginUpdate();
        _fields.Items.Clear();
        _fields.Groups.Clear();
        if (detail is null)
        {
            _title.Text = "";
            _subtitle.Text = "Select an asset";
            _fields.EndUpdate();
            return;
        }

        var a = detail.Asset;
        _title.Text = a.Name;
        _subtitle.Text = AssetText.Subtitle(a) is { } sub ? $"{AssetText.KindTitle(a.Kind)} · {sub}" : AssetText.KindTitle(a.Kind);

        var identity = Group("Identity");
        Add(identity, "Name", a.Name);
        if (a.Key != a.Name)
            Add(identity, "Key", a.Key);
        if (a.DisplayName is not null)
            Add(identity, "Display name", a.DisplayName);
        Add(identity, "Kind", AssetText.KindTitle(a.Kind));
        Add(identity, "DLC", a.Dlc);
        Add(identity, "Hash", a.Hash.ToString());
        Add(identity, "Hash (int32)", HashText.ToSigned(a.Hash));
        Add(identity, "Hash (hex)", HashText.ToHex(a.Hash));
        if (detail.Parent is { } parent)
            Add(identity, "Parent", parent.Name);

        if (detail.Tags.Count > 0)
        {
            var tags = Group("Tags");
            Add(tags, "Tags", string.Join(", ", detail.Tags));
        }

        var data = AssetText.DataFields(detail.Data);
        if (data.Count > 0)
        {
            var group = Group("Data");
            foreach (var f in data)
                Add(group, f.Label, f.Value);
        }

        var sources = Group(detail.Sources.Count > 1 ? $"Sources ({detail.Sources.Count}, later override earlier)" : "Source");
        foreach (var s in detail.Sources)
            Add(sources, s.Dlc, s.Path);

        _fields.EndUpdate();
    }

    public void FocusFields()
    {
        _fields.Focus();
        if (_fields.Items.Count > 0 && _fields.SelectedItems.Count == 0)
            _fields.Items[0].Selected = true;
    }

    private ListViewGroup Group(string header)
    {
        var g = new ListViewGroup(header);
        _fields.Groups.Add(g);
        return g;
    }

    private void Add(ListViewGroup group, string label, string value) =>
        _fields.Items.Add(new ListViewItem([label, value], group) { ToolTipText = value });

    private void CopySelectedField()
    {
        if (_fields.SelectedItems.Count > 0)
            Copied?.Invoke(this, _fields.SelectedItems[0].SubItems[1].Text);
    }

    private void UpdateFavoriteButton()
    {
        _favorite.Text = _isFavorite ? "★ Favorite" : "☆ Favorite";
        _favorite.ForeColor = _isFavorite ? Theme.Accent : Theme.Text;
    }
}
