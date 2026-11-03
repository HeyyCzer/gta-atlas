using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GtaAtlas.Core.Game;

/// <summary>Finds GTA V installs from Rockstar, Steam and Epic metadata.</summary>
public static partial class GameLocator
{
    private const string LegacyFolderName = "Grand Theft Auto V";
    private const string EnhancedFolderName = "Grand Theft Auto V Enhanced";

    /// <summary>All distinct installs found, Legacy first.</summary>
    public static IReadOnlyList<GameInstall> FindAll()
    {
        var installs = CandidateFolders()
            .Select(GameInstall.TryFromFolder)
            .OfType<GameInstall>()
            .DistinctBy(i => Path.GetFullPath(i.Folder).TrimEnd('\\').ToUpperInvariant())
            .OrderBy(i => i.Gen9)
            .ToList();
        return installs;
    }

    public static GameInstall? FindPreferred(bool gen9) =>
        FindAll().FirstOrDefault(i => i.Gen9 == gen9);

    private static IEnumerable<string> CandidateFolders()
    {
        foreach (var key in new[] { "Grand Theft Auto V", "GTAV Enhanced", "Grand Theft Auto V Enhanced" })
        {
            if (ReadRegistry(@"SOFTWARE\WOW6432Node\Rockstar Games\" + key, "InstallFolder") is { } rockstar)
                yield return rockstar;
        }

        foreach (var library in SteamLibraries())
        {
            yield return Path.Combine(library, "steamapps", "common", LegacyFolderName);
            yield return Path.Combine(library, "steamapps", "common", EnhancedFolderName);
        }

        foreach (var epic in EpicInstallLocations())
            yield return epic;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var steam = ReadRegistry(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                    ?? ReadRegistry(@"SOFTWARE\Valve\Steam", "InstallPath", RegistryHive.CurrentUser);
        if (steam is null)
            return [];

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf))
            return [steam];

        return ParseSteamLibraryFolders(File.ReadAllText(vdf)).Prepend(steam).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Extracts every library "path" value from a Steam libraryfolders.vdf.</summary>
    internal static IEnumerable<string> ParseSteamLibraryFolders(string vdf) =>
        VdfPathRegex().Matches(vdf).Select(m => m.Groups[1].Value.Replace(@"\\", @"\"));

    private static IEnumerable<string> EpicInstallLocations()
    {
        var manifests = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests))
            yield break;

        foreach (var file in Directory.EnumerateFiles(manifests, "*.item"))
        {
            string? location = null;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.TryGetProperty("DisplayName", out var name) &&
                    name.GetString()?.Contains(LegacyFolderName, StringComparison.OrdinalIgnoreCase) == true &&
                    root.TryGetProperty("InstallLocation", out var loc))
                {
                    location = loc.GetString();
                }
            }
            catch (Exception e) when (e is IOException or JsonException)
            {
            }

            if (location is not null)
                yield return location;
        }
    }

    private static string? ReadRegistry(string subKey, string value, RegistryHive hive = RegistryHive.LocalMachine)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(subKey);
            return key?.GetValue(value) as string;
        }
        catch (Exception e) when (e is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex VdfPathRegex();
}
