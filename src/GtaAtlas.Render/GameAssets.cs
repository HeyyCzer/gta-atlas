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
                LoadVehicles = false, // ~3 s of vehicles.meta/carcols; the preview loads the yft directly (LoadVehicle)
                BuildExtendedJenkIndex = false,
            };
            error ??= _ => { };
            Ready = Task.Run(() =>
            {
                var progress = InitProgress.ForFolder(install.Folder);
                Progress = progress;
                if (!CacheInit.TryInitWithoutMaps(Cache, install, s => Status = s, error, progress))
                {
                    Progress = null; // upstream init reports no stages
                    Cache.Init(s => Status = s, error);
                }
            });
            _pump = new Thread(Pump) { IsBackground = true, Name = "GameAssets content" };
            _pump.Start();
        }

        public GameFileCache Cache { get; }

        /// <summary>Completes when the cache is initialised; faults if the scan failed.</summary>
        public Task Ready { get; }

        /// <summary>Latest progress message from the cache scan.</summary>
        public string Status { get; private set; } = "Starting";

        /// <summary>Estimated init completion; null when unknown (fallback init path).</summary>
        public InitProgress Progress { get; private set; }

        /// <summary>User-facing name of the init stage, e.g. "Loading models".</summary>
        public string StageLabel =>
            Ready.IsFaulted ? "Game files failed to load"
            : Ready.IsCompleted ? "Game files ready"
            : Progress?.Label ?? "Loading game files";

        /// <summary><see cref="StageLabel"/> with the percentage when known, e.g. "Loading models 62%".</summary>
        public string Describe() =>
            Progress is { } p && !Ready.IsCompleted ? $"{StageLabel} {p.Value * 100:0}%" : StageLabel;

        public static GameAssets Start(GameInstall install, Action<string> error = null) => new(install, error);

        /// <summary>
        /// Loads a vehicle's models: <c>name_hi.yft</c> (high detail only) and <c>name.yft</c> (all LODs, lower top level),
        /// blocking until the content thread has read them. Null if the game has neither.
        /// Call off the UI thread, after <see cref="Ready"/>.
        /// </summary>
        public VehicleModels LoadVehicle(string name, int timeoutMs = 10000)
        {
            name = name.ToLowerInvariant();
            var hi = LoadFragment(name + "_hi", timeoutMs);
            var lods = LoadFragment(name, timeoutMs);
            return hi == null && lods == null ? null : new VehicleModels(hi, lods);
        }

        /// <summary>Loads <c>model.yft</c>; null if missing or without a drawable.</summary>
        public FragType LoadFragment(string model, int timeoutMs = 10000)
        {
            var hash = JenkHash.GenHash(model.ToLowerInvariant());
            if (Cache.GetYftEntry(hash) == null)
                return null;
            var yft = Wait(() => Cache.GetYft(hash), y => y is not { Loaded: false }, timeoutMs);
            return yft?.Fragment?.Drawable != null ? yft.Fragment : null;
        }

        /// <summary>Loads an archetype's drawable (ydr or ydd entry), blocking like <see cref="LoadVehicle"/>; null if missing.</summary>
        public DrawableBase LoadDrawable(Archetype archetype, int timeoutMs = 10000)
        {
            // No file to wait for (TryGetDrawable tries the ydd entry, then ydr, then yft).
            if (archetype.DrawableDict == 0 && Cache.GetYdrEntry(archetype.Hash) == null && Cache.GetYftEntry(archetype.Hash) == null)
                return null;
            return Wait(() => Cache.TryGetDrawable(archetype), d => d != null, timeoutMs);
        }

        /// <summary>Polls <paramref name="get"/> until <paramref name="done"/>; re-requesting matters, as the cache drops requests not renewed within 0.5 s.</summary>
        private static T Wait<T>(Func<T> get, Func<T, bool> done, int timeoutMs)
        {
            var deadline = Environment.TickCount64 + timeoutMs;
            var value = get();
            while (!done(value) && Environment.TickCount64 < deadline)
            {
                Thread.Sleep(2);
                value = get();
            }
            return value;
        }

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

    /// <summary>A vehicle's <c>_hi</c> model and its regular, multi-LOD one; either may be missing.</summary>
    public sealed record VehicleModels(FragType Hi, FragType Lods);
}
