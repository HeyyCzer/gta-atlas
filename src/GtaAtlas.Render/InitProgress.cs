using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace GtaAtlas.Render
{
    /// <summary>
    /// Estimated completion (0–1) of <see cref="CacheInit"/>. Each stage has a weight from its measured share of the
    /// init time; within the RPF scan, progress follows the bytes of the top-level archives already reached (the
    /// scan reports <c>Scanning {path}...</c> per archive, nested ones under their parent's path).
    /// Written by the init thread, read by the UI: <see cref="Value"/> is a single volatile double.
    /// </summary>
    public sealed class InitProgress
    {
        public const string Scan = "Scan";

        /// <summary>Stages in run order with weights (ms measured on Legacy, warm disk; see <c>GameAssetsTests</c>) and UI labels.</summary>
        public static readonly (string Stage, double Weight, string Label)[] Stages =
        [
            (Scan, 2190, "Scanning archives"),
            ("InitGlobal", 810, "Indexing files"),
            ("InitDlcList", 90, "Reading DLCs"),
            ("InitActiveMapRpfFiles", 90, "Reading DLCs"),
            ("InitManifestDicts", 425, "Reading manifests"),
            ("InitGtxds", 720, "Reading texture lists"),
            ("InitArchetypeDicts", 1080, "Loading models"),
            ("InitStringDicts", 265, "Loading names"),
            ("InitVehicles", 0, "Finishing"),
            ("InitPeds", 0, "Finishing"),
            ("InitAudio", 0, "Finishing"),
        ];

        private static readonly double Total = Stages.Sum(s => s.Weight);

        private readonly Dictionary<string, long> _archives;
        private readonly long _archiveBytes;
        private readonly HashSet<string> _reached = new(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private long _scannedBytes; // archives already passed, excluding the current one
        private long _currentBytes;
        private volatile int _stage = -1;
        private volatile float _value;

        /// <param name="archives">Top-level RPFs relative to the game folder, with their sizes.</param>
        public InitProgress(IEnumerable<(string Path, long Size)> archives)
        {
            _archives = archives.ToDictionary(a => a.Path, a => Math.Max(1, a.Size), StringComparer.OrdinalIgnoreCase);
            _archiveBytes = Math.Max(1, _archives.Values.Sum());
        }

        public static InitProgress ForFolder(string folder) =>
            new(System.IO.Directory.EnumerateFiles(folder, "*.rpf", System.IO.SearchOption.AllDirectories)
                .Select(f => (System.IO.Path.GetRelativePath(folder, f), new System.IO.FileInfo(f).Length)));

        public double Value => _value;

        /// <summary>User-facing name of the current stage.</summary>
        public string Label
        {
            get
            {
                var stage = _stage;
                return stage < 0 ? "Starting" : stage >= Stages.Length ? "Done" : Stages[stage].Label;
            }
        }

        /// <summary>Elapsed ms per finished stage, for re-measuring <see cref="Stages"/>.</summary>
        public List<(string Stage, long Ms)> Timings { get; } = [];

        /// <summary>Feeds a cache status message; only <c>Scanning ...</c> lines move the scan stage.</summary>
        public void Status(string message)
        {
            const string prefix = "Scanning ";
            if (message is null || !message.StartsWith(prefix, StringComparison.Ordinal))
                return;
            if (_stage < 0)
                Enter(Scan);
            var path = message[prefix.Length..].TrimEnd('.');
            var end = path.IndexOf(".rpf", StringComparison.OrdinalIgnoreCase);
            var top = end < 0 ? path : path[..(end + 4)];
            if (!_archives.TryGetValue(top, out var size) || !_reached.Add(top))
                return;
            _scannedBytes += _currentBytes;
            _currentBytes = size;
            Set(0, (double)_scannedBytes / _archiveBytes);
        }

        /// <summary>Marks the start of a stage of <see cref="Stages"/>; earlier stages count as done.</summary>
        public void Enter(string stage)
        {
            var index = Array.FindIndex(Stages, s => s.Stage == stage);
            if (index < 0 || index <= _stage)
                return;
            if (_stage >= 0)
                Timings.Add((Stages[_stage].Stage, _clock.ElapsedMilliseconds));
            _clock.Restart();
            _stage = index;
            Set(index, 0);
        }

        public void Complete()
        {
            if (_stage >= 0)
                Timings.Add((Stages[_stage].Stage, _clock.ElapsedMilliseconds));
            _stage = Stages.Length;
            _value = 1;
        }

        private void Set(int stage, double within)
        {
            var before = Stages.Take(stage).Sum(s => s.Weight);
            _value = (float)Math.Clamp((before + Stages[stage].Weight * within) / Total, 0, 1);
        }
    }
}
