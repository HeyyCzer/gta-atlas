using System;
using System.Threading;
using System.Threading.Tasks;
using CodeWalker.GameFiles;
using GtaAtlas.Core.Game;

namespace GtaAtlas.Render
{
    /// <summary>
    /// The app-wide CodeWalker <see cref="GameFileCache"/>: initialised once in the background (slow: scans every RPF
    /// and loads all archetypes) and pumped by a content thread that loads the files renderers ask for.
    /// Keys must already be loaded (<see cref="GameSession.Open"/>).
    /// </summary>
    public sealed class GameAssets : IDisposable
    {
        private const long CacheBytes = 2L << 30; // upstream default
        private const double CacheSeconds = 10;

        private readonly Thread _pump;
        private volatile bool _running = true;

        private GameAssets(GameInstall install, Action<string> error)
        {
            Cache = new GameFileCache(CacheBytes, CacheSeconds, install.Folder, install.Gen9, "", false, "")
            {
                EnableDlc = true, // with no SelectedDlc, Init picks the latest one
                LoadAudio = false,
                LoadPeds = false,
                BuildExtendedJenkIndex = false,
            };
            Ready = Task.Run(() => Cache.Init(s => Status = s, error ?? (_ => { })));
            _pump = new Thread(Pump) { IsBackground = true, Name = "GameAssets content" };
            _pump.Start();
        }

        public GameFileCache Cache { get; }

        /// <summary>Completes when the cache is initialised; faults if the scan failed.</summary>
        public Task Ready { get; }

        /// <summary>Latest progress message from the cache scan.</summary>
        public string Status { get; private set; } = "Starting";

        public static GameAssets Start(GameInstall install, Action<string> error = null) => new(install, error);

        private void Pump()
        {
            while (_running)
            {
                if (!Cache.IsInited)
                {
                    Thread.Sleep(20);
                    continue;
                }
                Cache.BeginFrame();
                if (!Cache.ContentThreadProc())
                    Thread.Sleep(5);
            }
        }

        public void Dispose()
        {
            _running = false;
            _pump.Join(1000);
        }
    }
}
