using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer.Extractors;

namespace GtaAtlas.Indexer;

public sealed record IndexProgress(string Stage, int Done = 0, int Total = 0);

public sealed record IndexResult(bool Skipped, int Assets, int Errors);

public static class GameIndexer
{
    /// <summary>Bump when extractors change so existing databases get rebuilt.</summary>
    public const int Version = 1;

    public const string FingerprintKey = "fingerprint";

    public static IndexResult Run(GameSession session, AtlasDb db, IProgress<IndexProgress>? progress = null, bool force = false,
        Action<string>? error = null, IReadOnlyList<Extractor>? extractors = null)
    {
        var fingerprint = Fingerprint(session.Install);
        if (!force && db.GetMeta(FingerprintKey) == fingerprint)
            return new IndexResult(true, 0, 0);

        var errors = 0;
        void Error(string message)
        {
            Interlocked.Increment(ref errors);
            error?.Invoke(message);
        }

        progress?.Report(new IndexProgress("Scanning archives"));
        var archive = GameArchive.Scan(session, error: Error);

        progress?.Report(new IndexProgress("Reading DLC list"));
        var dlcs = DlcCatalog.Build(archive, Error);

        progress?.Report(new IndexProgress("Loading game text"));
        var text = GameText.Load(archive);

        extractors ??= ExtractorSet.All;
        var work = archive.Files
            .Select(e => (Entry: e, Extractor: extractors.FirstOrDefault(x => x.Accepts(e))))
            .Where(w => w.Extractor is not null)
            .ToList();

        var raws = new ConcurrentBag<RawAsset>();
        var done = 0;
        progress?.Report(new IndexProgress("Extracting", 0, work.Count));
        Parallel.ForEach(work, w =>
        {
            try
            {
                foreach (var raw in w.Extractor!.Extract(w.Entry, text))
                    raws.Add(raw);
            }
            catch (Exception e)
            {
                Error($"{w.Entry.Path}: {e.Message}");
            }

            var n = Interlocked.Increment(ref done);
            if (n % 1000 == 0)
                progress?.Report(new IndexProgress("Extracting", n, work.Count));
        });

        progress?.Report(new IndexProgress("Merging"));
        dlcs = DlcCatalog.WithUnlisted(dlcs, raws.Select(r => r.Dlc));
        var assets = AssetMerger.Merge(raws, dlcs);

        progress?.Report(new IndexProgress("Writing database", 0, assets.Count));
        IndexWriter.ReplaceAll(db, dlcs, assets);
        db.SetMeta(FingerprintKey, fingerprint);
        db.SetMeta("indexed_at", DateTime.UtcNow.ToString("O"));
        db.SetMeta("game_version", FileVersionInfo.GetVersionInfo(session.Install.ExePath).FileVersion ?? "");
        db.SetMeta("game_folder", session.Install.Folder);

        return new IndexResult(false, assets.Count, errors);
    }

    /// <summary>Changes whenever any archive in the install changes (game update, DLC added) or the indexer version bumps.</summary>
    public static string Fingerprint(GameInstall install)
    {
        var sb = new StringBuilder($"v{Version}|{install.Gen9}|");
        var files = Directory.EnumerateFiles(install.Folder, "*.rpf", SearchOption.AllDirectories)
            .Select(f => (Rel: Path.GetRelativePath(install.Folder, f), Info: new FileInfo(f)))
            .Where(f => !SourcePaths.IsMod(f.Rel))
            .OrderBy(f => f.Rel, StringComparer.OrdinalIgnoreCase);
        foreach (var (rel, info) in files)
            sb.Append(rel.ToLowerInvariant()).Append('|').Append(info.Length).Append('|').Append(info.LastWriteTimeUtc.Ticks).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}
