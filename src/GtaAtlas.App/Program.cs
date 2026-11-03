using GtaAtlas.App.Ui;
using GtaAtlas.Core;
using GtaAtlas.Data;

namespace GtaAtlas.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetColorMode(SystemColorMode.Dark);
        Application.ThreadException += (_, e) => Fail(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => AppLog.Write("app", [e.ExceptionObject.ToString() ?? ""]);

        var paths = AppPaths.Default;
        paths.EnsureCreated();
        var settings = AtlasSettings.Load(paths);
        var db = AtlasDb.Open(paths.Database);
        Application.Run(new MainForm(paths, settings, db));
    }

    private static void Fail(Exception e)
    {
        AppLog.Write("app", [e.ToString()]);
        MessageBox.Show(e.Message, "GTA Atlas", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
