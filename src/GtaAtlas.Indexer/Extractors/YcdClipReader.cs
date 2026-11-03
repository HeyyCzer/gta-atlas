using CodeWalker.GameFiles;

namespace GtaAtlas.Indexer.Extractors;

/// <summary>
/// Reads only clip names and durations from a .ycd. <see cref="YcdFile"/> decodes every animation
/// sequence too, which made it ~90% of indexing time. Offsets follow CodeWalker's ClipDictionary,
/// ClipMapEntry and ClipBase/ClipAnimation/ClipAnimationList readers.
/// </summary>
internal static class YcdClipReader
{
    private const long SystemBase = 0x50000000;

    public sealed record Clip(uint Hash, string? Name, float Duration);

    public static List<Clip> Read(RpfResourceFileEntry entry, byte[] data)
    {
        var r = new ResourceDataReader(entry, data);

        // ClipDictionary: ClipsPointer at 0x28, ClipsMapCapacity at 0x30.
        r.Position = SystemBase + 0x28;
        var clipsPointer = r.ReadUInt64();
        var capacity = r.ReadUInt16();

        var clips = new List<Clip>();
        if (clipsPointer == 0 || capacity == 0)
            return clips;

        foreach (var bucket in r.ReadUlongsAt(clipsPointer, capacity, false))
        {
            // Each bucket is a linked list of ClipMapEntry { hash, pad, clip*, next* }.
            var visited = 0;
            for (var entryPtr = bucket; entryPtr != 0 && visited++ < 4096;)
            {
                r.Position = (long)entryPtr;
                var hash = r.ReadUInt32();
                r.ReadUInt32();
                var clipPtr = r.ReadUInt64();
                entryPtr = r.ReadUInt64();
                if (clipPtr != 0)
                    clips.Add(ReadClip(r, clipPtr, hash));
            }
        }
        return clips;
    }

    private static Clip ReadClip(ResourceDataReader r, ulong clipPtr, uint hash)
    {
        // ClipBase: Type at 0x10, NamePointer at 0x18. Subclass data starts at 0x50.
        r.Position = (long)clipPtr + 0x10;
        var type = (ClipType)r.ReadUInt32();
        r.ReadUInt32();
        var namePtr = r.ReadUInt64();

        float duration = 0;
        if (type == ClipType.Animation)
        {
            // ClipAnimation: AnimationPointer 0x50, StartTime 0x58, EndTime 0x5C, Rate 0x60.
            r.Position = (long)clipPtr + 0x58;
            var start = r.ReadSingle();
            var end = r.ReadSingle();
            var rate = r.ReadSingle();
            duration = rate > 0 ? (end - start) / rate : end - start;
        }
        else if (type == ClipType.AnimationList)
        {
            // ClipAnimationList: Duration at 0x60.
            r.Position = (long)clipPtr + 0x60;
            duration = r.ReadSingle();
        }

        var name = namePtr != 0 ? r.ReadStringAt(namePtr) : null;
        return new Clip(hash, name, duration);
    }
}
