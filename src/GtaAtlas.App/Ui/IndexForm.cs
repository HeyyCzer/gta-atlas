using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

namespace GtaAtlas.App.Ui;

/// <summary>Picks the game folder and builds the index, with progress. Shown on first run and from "Reindex".</summary>
internal sealed class IndexForm : Form
{
    private readonly AppPaths _paths;
    private readonly AtlasDb _db;
    private readonly AtlasSettings _settings;

    private readonly TextBox _folder = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Button _detect = new() { Text = "Detect", AutoSize = true };
    private readonly Label _edition = new() { AutoSize = true, ForeColor = Theme.Muted };
    private readonly CheckBox _force = new() { Text = "Rebuild even if the game has not changed", AutoSize = true };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Height = 18 };
    private readonly Label _stage = new() { AutoSize = true, ForeColor = Theme.Muted, Text = "Ready." };
    private readonly Button _start = new() { Text = "Index", AutoSize = true };
    private readonly Button _close = new() { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel };
    private bool _running;

    public IndexForm(AppPaths paths, AtlasDb db, AtlasSettings settings, bool firstRun)
    {
        _paths = paths;
        _db = db;
        _settings = settings;

        Text = firstRun ? "GTA Atlas — first run" : "GTA Atlas — reindex";
        Icon = AppIcon.Get();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Theme.UiFont;
        ClientSize = new Size(620, 230);
        CancelButton = _close;
        AcceptButton = _start;

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(590, 0),
            Text = firstRun
                ? "Choose your GTA V folder. Atlas reads the game archives once to build a local index (about a minute); browsing never touches the game files."
                : "Rebuild the local index from the game archives.",
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14), ColumnCount = 3, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(intro, 0, 0);
        layout.SetColumnSpan(intro, 3);
        layout.Controls.Add(_folder, 0, 1);
        layout.Controls.Add(_browse, 1, 1);
        layout.Controls.Add(_detect, 2, 1);
        layout.Controls.Add(_edition, 0, 2);
        layout.Controls.Add(_force, 0, 3);
        layout.Controls.Add(_progress, 0, 4);
        layout.SetColumnSpan(_progress, 3);
        layout.Controls.Add(_stage, 0, 5);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(10) };
        buttons.Controls.AddRange([_close, _start]);
        Controls.Add(layout);
        Controls.Add(buttons);

        _force.Visible = !firstRun;
        _folder.Text = settings.GameFolder ?? GameLocator.FindPreferred(settings.Gen9)?.Folder ?? "";
        _folder.TextChanged += (_, _) => UpdateEdition();
        _browse.Click += (_, _) => Browse();
        _detect.Click += (_, _) =>
        {
            var found = GameLocator.FindAll();
            if (found.Count == 0)
                MessageBox.Show(this, "No GTA V install found. Use Browse.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            else
                _folder.Text = found[0].Folder;
        };
        _start.Click += async (_, _) => await RunAsync();
        FormClosing += (_, e) => e.Cancel = _running;
        UpdateEdition();
    }

    public IndexResult? Result { get; private set; }

    private void UpdateEdition()
    {
        var install = GameInstall.TryFromFolder(_folder.Text.Trim());
        _edition.Text = install is null ? "No GTA5.exe or GTA5_Enhanced.exe in this folder." : install.Gen9 ? "Enhanced edition" : "Legacy edition";
        _edition.ForeColor = install is null ? Color.IndianRed : Theme.Muted;
        _start.Enabled = install is not null && !_running;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "GTA V install folder", UseDescriptionForTitle = true, InitialDirectory = _folder.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _folder.Text = dialog.SelectedPath;
    }

    private async Task RunAsync()
    {
        if (GameInstall.TryFromFolder(_folder.Text.Trim()) is not { } install)
            return;

        _settings.GameFolder = install.Folder;
        _settings.Gen9 = install.Gen9;
        _settings.Save(_paths);

        SetRunning(true);
        var ok = false;
        var errors = new List<string>();
        var progress = new Progress<IndexProgress>(p =>
        {
            if (p.Stage.StartsWith(' '))
                return; // per-extractor timings, CLI only
            _stage.Text = p.Total > 0 ? $"{p.Stage} {p.Done:N0}/{p.Total:N0}" : p.Stage;
            if (p.Total > 0)
            {
                _progress.Style = ProgressBarStyle.Continuous;
                _progress.Maximum = p.Total;
                _progress.Value = Math.Min(p.Done, p.Total);
            }
            else
            {
                _progress.Style = ProgressBarStyle.Marquee;
            }
        });

        try
        {
            var force = _force.Checked;
            Result = await Task.Run(() =>
            {
                var session = GameSession.Open(_paths, _settings);
                return GameIndexer.Run(session, _db, progress, force, e => { lock (errors) errors.Add(e); });
            });
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = _progress.Maximum;
            _stage.Text = Result.Skipped
                ? "Index is already up to date."
                : $"Indexed {Result.Assets:N0} assets" + (Result.Errors > 0 ? $" ({Result.Errors} files could not be read)." : ".");
            ok = true;
            AppLog.Write("index", errors);
        }
        catch (Exception e)
        {
            _progress.Style = ProgressBarStyle.Continuous;
            _progress.Value = 0;
            _stage.Text = "Indexing failed.";
            AppLog.Write("index", [.. errors, e.ToString()]);
            MessageBox.Show(this, e.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            SetRunning(false);
        }

        if (ok)
            DialogResult = DialogResult.OK;
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _folder.Enabled = _browse.Enabled = _detect.Enabled = _force.Enabled = _close.Enabled = !running;
        UpdateEdition();
    }
}
