using System.Text.Json;

namespace GtaAtlas.Core;

public sealed class AtlasSettings
{
    public string? GameFolder { get; set; }
    public bool Gen9 { get; set; }

    /// <summary>Base64 AES key extracted from the game exe; cached because scanning the exe takes seconds.</summary>
    public string? AesKey { get; set; }

    /// <summary>Disk space for thumbnails; least recently shown ones are deleted past it.</summary>
    public int ThumbnailCacheMb { get; set; } = 500;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AtlasSettings Load(AppPaths paths)
    {
        if (!File.Exists(paths.SettingsFile))
            return new AtlasSettings();

        try
        {
            return JsonSerializer.Deserialize<AtlasSettings>(File.ReadAllText(paths.SettingsFile), JsonOptions) ?? new AtlasSettings();
        }
        catch (JsonException)
        {
            return new AtlasSettings();
        }
    }

    public void Save(AppPaths paths)
    {
        paths.EnsureCreated();
        File.WriteAllText(paths.SettingsFile, JsonSerializer.Serialize(this, JsonOptions));
    }
}
