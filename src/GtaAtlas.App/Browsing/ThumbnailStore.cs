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
            using var stream = File.OpenRead(PathFor(key));
            using var image = Image.FromStream(stream);
            return new Bitmap(image);
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

    public void MarkFailed(ThumbKey key, string reason)
    {
        var path = FailPathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, reason);
    }
}
