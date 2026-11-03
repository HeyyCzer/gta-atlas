using CodeWalker.GameFiles;
using GtaAtlas.Core.Game;

namespace GtaAtlas.Indexer;

/// <summary>Scanned view of every RPF in the install (headers only; file data is read on demand).</summary>
public sealed class GameArchive
{
    private GameArchive(RpfManager manager)
    {
        Manager = manager;
        Files = manager.AllRpfs
            .SelectMany(r => r.AllEntries)
            .OfType<RpfFileEntry>()
            .Where(e => !SourcePaths.IsMod(e.Path))
            .ToList();
    }

    public RpfManager Manager { get; }

    /// <summary>All file entries outside the mods folder.</summary>
    public IReadOnlyList<RpfFileEntry> Files { get; }

    public static GameArchive Scan(GameSession session, Action<string>? status = null, Action<string>? error = null)
    {
        var manager = new RpfManager();
        manager.Init(session.Install.Folder, session.Install.Gen9, status ?? (_ => { }), error ?? (_ => { }));
        return new GameArchive(manager);
    }

    public static T? Load<T>(RpfFileEntry entry) where T : class, PackedFile, new()
    {
        var data = entry.File.ExtractFile(entry);
        if (data is null)
            return null;
        var file = new T();
        file.Load(data, entry);
        return file;
    }

    public static string? ReadText(RpfFileEntry entry)
    {
        var data = entry.File.ExtractFile(entry);
        return data is null ? null : System.Text.Encoding.UTF8.GetString(data).TrimStart('﻿');
    }
}

internal static class Names
{
    /// <summary>Resolved string for a hash, or hex when the name is unknown.</summary>
    public static string Of(MetaHash hash) => Of(hash.Hash);

    public static string Of(uint hash) =>
        hash == 0 ? "" : JenkIndex.TryGetString(hash) is { Length: > 0 } s ? s : "0x" + hash.ToString("X8");

    public static string? OrNull(MetaHash hash) => hash.Hash == 0 ? null : Of(hash);

    public static uint Joaat(string text) => JenkHash.GenHash(text.ToLowerInvariant());
}
