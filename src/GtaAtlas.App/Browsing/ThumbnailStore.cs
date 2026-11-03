using GtaAtlas.Data;

namespace GtaAtlas.App.Browsing;

/// <summary>Identifies a thumbnail; vehicles load by model name, props by archetype hash.</summary>
public readonly record struct ThumbKey(string Kind, uint Hash, string Name)
{
    public static ThumbKey Of(AssetSummary item) => new(item.Kind, item.Hash, item.Name);
}

/// <summary>
/// Thumbnail files: <c>thumbs\{kind}\{hash:x8}.png</c>, plus <c>.fail</c> markers (with the reason) for assets the game
/// has no model for, so they aren't retried every run. Delete the folder to regenerate everything.
/// They are a cache: <see cref="Trim"/> keeps them under a size, dropping the least recently used (a file's write time
/// is its last use: <see cref="Load"/> refreshes it).
/// </summary>
public sealed class ThumbnailStore(string root)
{
    public string Root { get; } = root;

    /// <summary>Kinds the renderer can draw.</summary>
    public static bool Supports(string kind) => kind is AssetKind.Prop or AssetKind.Vehicle;

    public string PathFor(ThumbKey key) => Path.Combine(Root, key.Kind, $"{key.Hash:x8}.png");

    private string FailPathFor(ThumbKey key) => Path.ChangeExtension(PathFor(key), ".fail");

    public bool Exists(ThumbKey key) => File.Exists(PathFor(key));

    public bool IsFailed(ThumbKey key) => File.Exists(FailPathFor(key));

    /// <summary>Loads a saved thumbnail without keeping the file open; null if missing or unreadable.</summary>
    public Bitmap? Load(ThumbKey key)
    {
        try
        {
            var path = PathFor(key);
            Bitmap bitmap;
            using (var stream = File.OpenRead(path))
            using (var image = Image.FromStream(stream))
                bitmap = new Bitmap(image);
            Touch(path);
            return bitmap;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Writes the PNG atomically (temp file + move), so a crash never leaves a truncated thumbnail.</summary>
    public void Save(ThumbKey key, Bitmap bitmap)
    {
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        bitmap.Save(temp, System.Drawing.Imaging.ImageFormat.Png);
        File.Move(temp, path, overwrite: true);
        File.Delete(FailPathFor(key));
    }

    /// <summary>
    /// Deletes the least recently used thumbnails until they take at most <paramref name="targetBytes"/>, if they take
    /// more than <paramref name="maxBytes"/> (the gap avoids trimming again after every save). Leftover temp files go
    /// too; failure markers are tiny and stay. Returns how many thumbnails were deleted.
    /// </summary>
    public int Trim(long maxBytes, long targetBytes)
    {
        if (!Directory.Exists(Root))
            return 0;
        var files = new List<FileInfo>();
        long total = 0;
        foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            if (file.Extension == ".tmp")
                TryDelete(file);
            else if (file.Extension == ".png")
            {
                files.Add(file);
                total += file.Length;
            }
        }
        if (total <= maxBytes)
            return 0;

        var deleted = 0;
        foreach (var file in files.OrderBy(f => f.LastWriteTimeUtc))
        {
            if (total <= targetBytes)
                break;
            var length = file.Length;
            if (TryDelete(file))
            {
                total -= length;
                deleted++;
            }
        }
        return deleted;
    }

    /// <summary>Marks the file as just used for <see cref="Trim"/>; best effort.</summary>
    private static void Touch(string path)
    {
        try
        {
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static bool TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void MarkFailed(ThumbKey key, string reason)
    {
        var path = FailPathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, reason);
    }
}
