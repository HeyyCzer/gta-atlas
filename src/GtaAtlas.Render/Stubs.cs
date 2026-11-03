// Replacements for upstream CodeWalker types that the linked renderer sources reference
// but whose original files drag in the WorldForm/project editor.

using CodeWalker.GameFiles;

namespace CodeWalker
{
    // Copied from CodeWalker/World/MapSelection.cs (that file depends on WorldForm).
    public enum MapSelectionMode
    {
        None = 0,
        Entity = 1,
        EntityExtension = 2,
        ArchetypeExtension = 3,
        TimeCycleModifier = 4,
        CarGenerator = 5,
        Grass = 6,
        WaterQuad = 7,
        Collision = 8,
        NavMesh = 9,
        Path = 10,
        TrainTrack = 11,
        LodLights = 12,
        MloInstance = 13,
        Scenario = 14,
        PopZone = 15,
        Heightmap = 16,
        Watermap = 17,
        Audio = 18,
        Occlusion = 19,
        CalmingQuad = 20,
        WaveQuad = 21,
    }
}

namespace CodeWalker.GameFiles
{
    // Upstream version reads GTAFolder/Settings; the renderer only calls it when no cache is passed.
    public static class GameFileCacheFactory
    {
        public static GameFileCache Create() =>
            throw new System.InvalidOperationException("Pass a GameFileCache to Renderer.");
    }
}
