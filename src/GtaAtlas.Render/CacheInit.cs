using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CodeWalker.GameFiles;
using GtaAtlas.Core.Game;

namespace GtaAtlas.Render
{
    /// <summary>
    /// <see cref="GameFileCache.Init(Action{string}, Action{string})"/> without the map steps (ymap/ybn/ynv
    /// dictionaries and the map cache files, ~1 s), which model previews never use. Upstream has no switch for them,
    /// so this replays its private init sequence by reflection; when any of those members is missing (upstream
    /// changed) it falls back to the regular Init. <c>CacheInitTests</c> compares <see cref="DlcSteps"/> with the source.
    /// </summary>
    public static class CacheInit
    {
        /// <summary>Upstream <c>InitDlc</c> body, in order. Steps in <see cref="Skipped"/> are replaced by empty data.</summary>
        public static readonly string[] DlcSteps =
        [
            "InitDlcList", "InitActiveMapRpfFiles", "InitMapDicts", "InitManifestDicts", "InitGtxds", "InitMapCaches",
            "InitArchetypeDicts", "InitStringDicts", "InitVehicles", "InitPeds", "InitAudio",
        ];

        public static readonly HashSet<string> Skipped = ["InitMapDicts", "InitMapCaches"];

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Any = Private | BindingFlags.Public;

        private static readonly Type T = typeof(GameFileCache);
        private static readonly MethodInfo[] Steps = DlcSteps.Where(s => !Skipped.Contains(s)).Select(Method).ToArray();
        private static readonly MethodInfo InitGlobal = Method("InitGlobal");
        private static readonly MethodInfo ExcludePaths = Method("GetExcludePaths");
        private static readonly FieldInfo StatusField = T.GetField("UpdateStatus", Private);
        private static readonly FieldInfo ErrorField = T.GetField("ErrorLog", Private);
        private static readonly MethodInfo[] MapDictSetters =
            new[] { "YmapDict", "YbnDict", "YnvDict", "AllYmapsDict" }.Select(n => T.GetProperty(n)?.GetSetMethod(true)).ToArray();

        /// <summary>Whether every upstream member the replay needs was found.</summary>
        public static bool Supported { get; } = !Steps.Contains(null) && InitGlobal is not null && ExcludePaths is not null
            && StatusField is not null && ErrorField is not null && !MapDictSetters.Contains(null);

        private static MethodInfo Method(string name) => T.GetMethod(name, Any, Type.EmptyTypes);

        /// <summary>Returns false (having done nothing) if upstream no longer matches.</summary>
        public static bool TryInitWithoutMaps(GameFileCache cache, GameInstall install, Action<string> status, Action<string> error,
            InitProgress progress = null)
        {
            if (!Supported)
                return false;

            if (progress != null)
            {
                var report = status;
                status = s =>
                {
                    progress.Status(s);
                    report(s);
                };
                progress.Enter(InitProgress.Scan);
            }

            StatusField.SetValue(cache, status);
            ErrorField.SetValue(cache, error);
            cache.Clear();

            cache.RpfMan = new RpfManager
            {
                ExcludePaths = (string[])ExcludePaths.Invoke(cache, null),
                EnableMods = cache.EnableMods,
                BuildExtendedJenkIndex = cache.BuildExtendedJenkIndex,
            };
            cache.RpfMan.Init(install.Folder, install.Gen9, status, error);
            progress?.Enter(InitGlobal.Name);
            Invoke(InitGlobal, cache);

            foreach (var setter in MapDictSetters)
                setter.Invoke(cache, [new Dictionary<uint, RpfFileEntry>()]);
            cache.AllCacheFiles = [];
            cache.YmapHierarchyDict = [];

            foreach (var step in Steps)
            {
                progress?.Enter(step.Name);
                Invoke(step, cache);
            }
            progress?.Complete();

            status("Scan complete");
            cache.IsInited = true;
            return true;
        }

        private static void Invoke(MethodInfo method, GameFileCache cache)
        {
            try
            {
                method.Invoke(cache, null);
            }
            catch (TargetInvocationException e) when (e.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(e.InnerException);
            }
        }
    }
}
