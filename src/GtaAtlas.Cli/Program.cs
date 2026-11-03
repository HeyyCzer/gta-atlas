using System.Diagnostics;
using System.Text.RegularExpressions;
using GtaAtlas.Core;
using GtaAtlas.Core.Game;
using GtaAtlas.Data;
using GtaAtlas.Indexer;

var paths = AppPaths.Default;
var command = args.FirstOrDefault() ?? "help";
var rest = args.Skip(1).ToList();

switch (command)
{
    case "files":
        Files(rest);
        break;
    case "cat":
        Cat(rest);
        break;
    case "index":
        Index(rest.Contains("--force"));
        break;
    case "search":
        Search(rest);
        break;
    case "stats":
        Stats();
        break;
    default:
        Console.WriteLine("""
            atlas files <regex>              list game files whose path matches
            atlas index [--force]            build the asset index
            atlas search <text> [--kind k]   query the index
            atlas stats                      counts per kind and DLC
            """);
        break;
}

GameSession OpenSession() => GameSession.Open(paths, AtlasSettings.Load(paths));

void Files(List<string> a)
{
    var regex = new Regex(a.FirstOrDefault() ?? ".", RegexOptions.IgnoreCase);
    var archive = GameArchive.Scan(OpenSession(), Console.Error.WriteLine);
    foreach (var e in archive.Files.Where(e => regex.IsMatch(e.Path)))
        Console.WriteLine($"{e.Path}  [{SourcePaths.DlcOf(e.Path)}]");
}

void Cat(List<string> a)
{
    var archive = GameArchive.Scan(OpenSession(), Console.Error.WriteLine);
    var entry = archive.Files.FirstOrDefault(e => e.Path.Equals(a.FirstOrDefault(), StringComparison.OrdinalIgnoreCase));
    if (entry is null)
    {
        Console.Error.WriteLine("File not found.");
        return;
    }
    if (entry.NameLower.EndsWith(".ymt") || entry.NameLower.EndsWith(".ytyp") || entry.NameLower.EndsWith(".ymap"))
    {
        var data = entry.File.ExtractFile(entry);
        Console.WriteLine(CodeWalker.GameFiles.MetaXml.GetXml(entry, data, out _));
        return;
    }
    Console.WriteLine(GameArchive.ReadText(entry));
}

void Index(bool force)
{
    var sw = Stopwatch.StartNew();
    var db = AtlasDb.Open(paths.Database);
    var progress = new Progress<IndexProgress>(p => Console.WriteLine($"[{sw.Elapsed:mm\\:ss}] {p.Stage}{(p.Total > 0 ? $" {p.Done}/{p.Total}" : "")}"));
    var result = GameIndexer.Run(OpenSession(), db, progress, force, Console.Error.WriteLine);
    Console.WriteLine(result.Skipped ? "Index is up to date." : $"Indexed {result.Assets} assets in {sw.Elapsed:mm\\:ss}.");
}

void Search(List<string> a)
{
    var kindAt = a.IndexOf("--kind");
    string? kind = null;
    if (kindAt >= 0 && kindAt + 1 < a.Count)
    {
        kind = a[kindAt + 1];
        a.RemoveRange(kindAt, 2);
    }
    var queries = new AssetQueries(AtlasDb.Open(paths.Database));
    var sw = Stopwatch.StartNew();
    var result = queries.Search(new SearchQuery { Text = string.Join(' ', a), Kind = kind, Limit = 30 });
    Console.WriteLine($"{result.Total} results ({sw.ElapsedMilliseconds} ms)");
    foreach (var r in result.Items)
        Console.WriteLine($"{r.Kind,-10} {r.Name,-40} {r.DisplayName,-30} {r.Dlc,-16} {HashText.ToHex(r.Hash)}");
}

void Stats()
{
    var queries = new AssetQueries(AtlasDb.Open(paths.Database));
    foreach (var (kind, count) in queries.CountsByKind().OrderBy(k => k.Key))
        Console.WriteLine($"{kind,-12} {count}");
    Console.WriteLine();
    foreach (var d in queries.Dlcs())
        Console.WriteLine($"{d.OrderIndex,3} {d.Name,-24} {string.Join(", ", d.CountsByKind.Select(k => $"{k.Key}={k.Value}"))}");
}
