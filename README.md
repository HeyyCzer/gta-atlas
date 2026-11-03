<p align="center">
  <img src="docs/icon.png" width="96" alt="GTA Atlas icon">
</p>

<h1 align="center">GTA Atlas</h1>

<p align="center">
  Offline explorer for GTA V assets: props, vehicles, peds, clothing, animations, MLOs, IPLs, YMAPs and DLCs,<br>
  with instant search and an interactive 3D preview. It reads your own game install, so you don't need a website or an internet connection.
</p>

---

## Features

- **Everything in one index.** About 470k assets taken straight from your game files: props (`.ytyp` archetypes), vehicles, peds, freemode clothing (one entry per drawable, with texture names), animation dictionaries and clips (including cutscenes), MLO interiors, YMAPs and IPLs.
- **Instant search.** SQLite with FTS5 returns results in milliseconds. Search by name, display name, tag or hash (decimal, signed int32 or hex).
- **Filters.** Filter by category, by the DLC that introduced the asset (load order resolved like the game does, overlays included), by gender for clothing, and by tags such as `scriptable`, `size: large`, `fragment` or vehicle class and make.
- **`scriptable` props.** A prop gets this tag when its `.ytyp` is always loaded (`PERMANENT_ITYP_FILE` / `DLC_ITYP_REQUEST`), so `CreateObject` works anywhere on the map. Props from map sections and interiors only spawn while that area is streamed in.
- **3D viewport.** Uses the CodeWalker renderer. Orbit, pan and zoom; pick the LOD; toggle wireframe, bounds and skeleton.
- **Thumbnails.** Rendered offscreen on a transparent background, queued with the visible cards first, and stored in a disk cache with a size limit.
- **Copy-friendly.** Click any value (name, joaat, signed hash, hex, texture dict…) to copy it. `Ctrl+C` copies the name of the selected asset.
- **Favorites.** Star assets with `Ctrl+D`. Favorites are filtered within each category.
- **DLC table.** Every pack in load order. Double-click a pack to browse its content.

## Requirements

- Windows 10/11 x64 with a DirectX 11 GPU
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- GTA V **Legacy** installed (Steam, Epic or Rockstar). The Enhanced edition is detected but not supported yet.

## Getting started

1. Download `GtaAtlas.zip` from the [latest release](../../releases/latest), extract it and run `GtaAtlas.exe`.
2. On first run the game folder is detected automatically; you can also pick it yourself. Indexing takes about 30–40 s and only runs again when the game files change.
3. Search and browse. Double-click (or press `Enter` on) an asset to open its details and the 3D preview.

The app only reads the game. It never modifies game files. Everything it writes goes to `%LOCALAPPDATA%\GtaAtlas\` (`atlas.db`, `thumbs\`, `settings.json`, `logs\`).

### Keyboard shortcuts

| Key | Action |
|---|---|
| `Ctrl+F` | Focus search |
| `Enter` / `↓` in search | Jump to results |
| `Enter` / double-click | Open details |
| `←` / `→` in details | Previous / next asset |
| `Ctrl+C` | Copy name (`dict clip` for animations) |
| `Ctrl+D` | Toggle favorite |
| `Ctrl+L` | Grid / list view |
| `L`, `W`, `B`, `S` in the viewport | Cycle LOD, wireframe, bounds, skeleton |
| `R` / double-click in the viewport | Reset camera |
| `Esc` | Close / clear |

### Command line

`GtaAtlas.Cli.zip` contains `atlas.exe`, which uses the same index:

```
atlas index [--force]                      build the asset index
atlas search <text> [--kind k] [--repeat n] query the index
atlas stats                                counts per kind and DLC
atlas files <regex>                        list game files whose path matches
```

## Building from source

```powershell
git clone --recurse-submodules https://github.com/HeyyCzer/gta-atlas.git
cd gta-atlas
dotnet build GtaAtlas.slnx
dotnet run --project src/GtaAtlas.App
dotnet test --solution GtaAtlas.slnx
```

Requires the .NET 10 SDK. If you set `GTA_ATLAS_HOME`, the app uses that folder for its data instead of `%LOCALAPPDATA%\GtaAtlas`. Set `GTA_ATLAS_GAME_DIR` to point the tests at a specific game install. Tests that need the game skip themselves when it isn't found.

| Project | Role |
|---|---|
| `src/GtaAtlas.Core` | Settings, game detection, AES keys, game session |
| `src/GtaAtlas.Data` | SQLite schema, migrations, queries, FTS |
| `src/GtaAtlas.Indexer` | Reads RPFs and extracts assets into the database |
| `src/GtaAtlas.Render` | CodeWalker renderer (upstream sources linked, not copied), viewport, thumbnails |
| `src/GtaAtlas.App` | WinForms UI |
| `src/GtaAtlas.Cli` | `atlas` command-line tool |
| `external/CodeWalker` | Upstream [CodeWalker](https://github.com/dexyfex/CodeWalker) as a git submodule, unmodified |

The UI never scans the game. Lists and search come from SQLite; the game is only opened to index, to load the asset in the viewport and to render missing thumbnails. See [ROADMAP.md](ROADMAP.md) for the plan and progress notes (in Portuguese).

### Releasing

```powershell
./scripts/bump-version.ps1 patch -Push   # or minor / major
```

The script bumps `<Version>` in `Directory.Build.props`, commits, tags `vX.Y.Z` and pushes. The [Release workflow](.github/workflows/release.yml) then builds, tests and publishes `GtaAtlas.zip` and `GtaAtlas.Cli.zip` with generated release notes.

## Credits

- [CodeWalker](https://github.com/dexyfex/CodeWalker) by dexyfex: file formats and the 3D renderer.
- [DurtyFree/gta-v-data-dumps](https://github.com/DurtyFree/gta-v-data-dumps): reference data used to validate the indexer.

GTA Atlas is a fan-made tool and is not affiliated with or endorsed by Rockstar Games or Take-Two Interactive. Grand Theft Auto and GTA V are trademarks of Take-Two Interactive. You need your own legal copy of the game; no game assets are distributed.
