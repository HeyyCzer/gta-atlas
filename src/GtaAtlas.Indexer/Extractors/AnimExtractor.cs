using CodeWalker.GameFiles;
using GtaAtlas.Data;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>Clip dictionaries (.ycd) and their clips. Cutscene dictionaries are tagged "cutscene".</summary>
public sealed class AnimExtractor : Extractor
{
    public const string CutsceneTag = "cutscene";

    public override bool Accepts(RpfFileEntry entry) => entry.NameLower.EndsWith(".ycd") && entry is RpfResourceFileEntry;

    public override IEnumerable<RawAsset> Extract(RpfFileEntry entry, GameText text)
    {
        var dictName = FileStem(entry);
        var cutscene = entry.Path.Contains("\\anim\\cutscene\\", StringComparison.OrdinalIgnoreCase);
        var data = entry.File.ExtractFile(entry);
        var clips = data is null
            ? []
            : ReadClips((RpfResourceFileEntry)entry, data)
                .Select(c => (Name: ClipName(c), Duration: Round(c.Duration)))
                .Where(c => c.Name.Length > 0)
                .DistinctBy(c => c.Name)
                .ToList();

        yield return Raw(entry, AssetKind.AnimDict, dictName, Names.Joaat(dictName)) with
        {
            Data = new { clips = clips.Count },
            Tags = cutscene ? [CutsceneTag] : [],
        };

        foreach (var (name, duration) in clips)
        {
            yield return Raw(entry, AssetKind.Anim, name, Names.Joaat(name)) with
            {
                Key = $"{dictName}/{name}".ToLowerInvariant(),
                Parent = (AssetKind.AnimDict, dictName),
                Data = new { duration },
                Tags = cutscene ? [CutsceneTag] : [],
            };
        }
    }

    /// <summary>Fast reader; falls back to CodeWalker's full parser for the few files it cannot walk.</summary>
    private static List<YcdClipReader.Clip> ReadClips(RpfResourceFileEntry entry, byte[] data)
    {
        try
        {
            return YcdClipReader.Read(entry, data);
        }
        catch (ArgumentException)
        {
            var ycd = new YcdFile();
            ycd.Load(data, entry);
            return (ycd.ClipMapEntries ?? [])
                .Where(e => e?.Clip is not null)
                .Select(e => new YcdClipReader.Clip(e.Hash.Hash, e.Clip.Name, e.Clip switch
                {
                    ClipAnimation a => a.Rate > 0 ? (a.EndTime - a.StartTime) / a.Rate : a.EndTime - a.StartTime,
                    ClipAnimationList l => l.Duration,
                    _ => 0f,
                }))
                .ToList();
        }
    }

    /// <summary>"pack:/idle_a.clip" → "idle_a".</summary>
    internal static string CleanClipName(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";
        var s = raw;
        var slash = s.LastIndexOfAny(['/', '\\', ':']);
        if (slash >= 0)
            s = s[(slash + 1)..];
        if (s.EndsWith(".clip", StringComparison.OrdinalIgnoreCase))
            s = s[..^5];
        return s;
    }

    private static string ClipName(YcdClipReader.Clip clip)
    {
        var name = CleanClipName(clip.Name);
        return name.Length > 0 ? name : Names.Of(clip.Hash);
    }
}
