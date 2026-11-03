using CodeWalker.GameFiles;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>Turns one kind of game file into asset definitions. Must be thread-safe.</summary>
public abstract class Extractor
{
    public abstract bool Accepts(RpfFileEntry entry);

    public abstract IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text);

    protected static RawAsset Raw(RpfFileEntry entry, string kind, string name, uint hash) => new()
    {
        Kind = kind,
        Key = name.ToLowerInvariant(),
        Name = name,
        Hash = hash,
        Path = entry.Path,
        Dlc = SourcePaths.DlcOf(entry.Path),
    };

    protected static string FileStem(RpfFileEntry entry)
    {
        var dot = entry.NameLower.LastIndexOf('.');
        return dot > 0 ? entry.NameLower[..dot] : entry.NameLower;
    }

    protected static float[] Vec(SharpDX.Vector3 v) => [Round(v.X), Round(v.Y), Round(v.Z)];

    protected static float Round(float f) => float.IsFinite(f) ? MathF.Round(f, 3) : 0;
}

public static class ExtractorSet
{
    public static IReadOnlyList<Extractor> All { get; } =
    [
        new ArchetypeExtractor(),
        new VehicleExtractor(),
        new PedExtractor(),
        new AnimExtractor(),
        new YmapExtractor(),
    ];
}
