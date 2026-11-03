# GTA Atlas — Roadmap

Local desktop tool (an alternative to forge.plebmasters.de) for exploring GTA V assets:
props, vehicles, peds, clothing, animations, MLOs, IPLs, YMAPs and DLCs — with instant search and an interactive 3D preview.

## Fixed decisions

| Topic | Decision |
|---|---|
| Runtime | .NET 10, WinForms (`net10.0-windows`), dark mode via `Application.SetColorMode` |
| Game file parsing | `CodeWalker.Core` (netstandard2.0) from `external/CodeWalker` |
| 3D rendering | **Native net10 port by linking files**: `GtaAtlas.Render` does `<Compile Include>` of the upstream sources (no copying/editing) + `Stubs.cs`. Tested 2026-11-02: builds with 0 errors. Our Form implements `CodeWalker.Rendering.DXForm`, `TopLevel=false`, hosted as a child control. `.cso` shaders from `external/CodeWalker/Shaders` copied to the output |
| License | CodeWalker has no explicit LICENSE (only `Notice.txt`). grzyClothTool is GPL-3.0 → **reference only, never copy code** (otherwise the app becomes GPL) |
| Cache | SQLite (`Microsoft.Data.Sqlite`) + FTS5 for search; PNG thumbnails on disk, generated on demand |
| Initial target game | Legacy: `C:\SteamLibrary\steamapps\common\Grand Theft Auto V`. Enhanced (gen9) later |
| User data | `%LOCALAPPDATA%\GtaAtlas\` → `atlas.db`, `thumbs\`, `settings.json`, `logs\` |

## Architecture

```
GtaAtlas.slnx
├─ external/CodeWalker                   (upstream git submodule, do not edit)
│    └─ CodeWalker.Core.csproj           direct ProjectReference (netstandard2.0)
├─ src/GtaAtlas.Core       AppPaths, AtlasSettings, game detection (GameLocator), keys (GameKeys), GameSession
├─ src/GtaAtlas.Data       SQLite schema, migrations, repositories, FTS queries
├─ src/GtaAtlas.Indexer    scans RPFs via RpfManager → writes the DB
├─ src/GtaAtlas.Render     linked CodeWalker sources (below) + Stubs.cs + ModelViewport : Form, DXForm + thumbnail capture
├─ src/GtaAtlas.App        WinForms UI (shell, lists, details, viewport); `Browsing/` = logic testable without UI
├─ src/GtaAtlas.Cli        `atlas` dev/debug CLI
└─ tests/GtaAtlas.Tests    hash, parsing, query, indexer tests
```

### CodeWalker sources linked into `GtaAtlas.Render` (proof of concept validated)

```xml
<Compile Include="..\..\external\CodeWalker\CodeWalker\Rendering\**\*.cs" />           <!-- 34 files -->
<Compile Include="..\..\external\CodeWalker\CodeWalker\Utils\MapUtils.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\Utils\TextureLoader.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\World\Widget.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\GameFiles\TextureFormats.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\Properties\Settings.Designer.cs" />
```
Packages: SharpDX, SharpDX.D3DCompiler, SharpDX.Direct3D11, SharpDX.Direct2D1, SharpDX.Mathematics (4.2.0). `AllowUnsafeBlocks`, `Nullable=disable` in that project.
`Stubs.cs`: enum `CodeWalker.MapSelectionMode` (copied from `World/MapSelection.cs`, which pulls in `WorldForm`) and `GameFileCacheFactory.Create()` throwing (we always pass the cache).

Principle: **the UI never scans the game**. Lists/search/filters = SQLite only. The game is opened only to
(a) index, (b) load the selected asset in the viewport, (c) render a missing thumbnail.

### Schema (draft)

- `dlc(id, name, pack_name, title, order_index, build, released_at)` — order from `dlclist.xml` + `setup2.xml`/`content.xml`
- `asset(id, kind, name, hash, display_name, dlc_id, rpf_path, entry_path, extra_json)`
  - `kind`: prop, vehicle, ped, clothing, anim_dict, anim_clip, mlo, ipl, ymap
- `asset_tag(asset_id, tag)` — derived tags (vehicle class, make, prop type, clothing component, etc.)
- `asset_fts` — FTS5 over name/display_name/tags/hash as text (decimal, int32, hex)
- `vehicle(asset_id, make, class, type, modkit, seats, ...)`, `ped(...)`, `clothing(...)`, `mlo(...)` — kind-specific columns
- `rpf_file(path, size, mtime, indexed_at)` — for incremental reindex
- `meta(key, value)` — schema version, indexed game version

## Phases

Each phase ends in something usable and verifiable. Mark `[x]` when done and note deviations under "Session notes".

### Phase 0 — Foundation
- [x] `GtaAtlas.slnx` solution + projects, `Directory.Build.props` (nullable, LangVersion latest, x64); empty `external/Directory.Build.props` isolates upstream
- [x] `CodeWalker.Core` via ProjectReference; `GtaAtlas.Render` with linked sources + stubs (see above)
- [x] `settings.json` (game path, gen9, cached AES key) + automatic detection (Rockstar registry, Steam libraries via `libraryfolders.vdf`, Epic manifests)
- [x] Load keys (`GTA5Keys.LoadFromPath`) and open/extract from `x64a.rpf` → integration test
- [x] `git init` + `.gitignore`; CodeWalker as submodule
- [x] xUnit v3 tests (MTP via `global.json`); game tests skip without an install (`GTA_ATLAS_GAME_DIR` forces the path)

**Done when:** console/test lists entries of `x64a.rpf`.

### Phase 1 — Indexer + DB
- [x] Schema + migrations in `GtaAtlas.Data` (`PRAGMA user_version`; generic `asset` + per-kind `data` JSON, `asset_source` with every occurrence, FTS5 trigram)
- [x] Map DLCs: `dlclist.xml` + `setup2.xml` (order, deviceName, timeStamp). Packs missing from the list go last
- [x] Props: archetypes from every `.ytyp` (name, hash, bbox, lodDist, flags, assetType, dicts)
- [x] Vehicles: `vehicles.meta` + GXT2 names (display name, make, class, type, modkits). `carcols`/`carvariations` → Phase 5
- [x] Peds: `peds.meta`/`peds.ymt` (name, pedType, clipsets)
- [x] Clothing (freemode M/F): one asset per drawable (`C/6/137/M`, `P/7/8/M`), textures in the JSON with GXT name (`textLabel` from the shop meta) + dev name (XML comment). Global index validated: 679/679 collections match a runtime dump (DurtyFree/gta-v-data-dumps) and total 5995 = plebmasters. Rules: order = setup2 `order`; a collection that only exists in an overlay RPF does not exist; a re-ship in a patch keeps its position and uses the newer content; Legacy ignores `*_g9ec` collections
- [x] Animations: `.ycd` → dictionaries and clips (name, duration), including cutscenes (tag `cutscene`). Own reader `YcdClipReader` (names/durations only), validated against `YcdFile` in a test
- [x] MLOs: `CMloArchetypeDef` archetypes (rooms, portals, entity sets)
- [x] YMAPs / IPLs: every `.ymap` (extents, parent, flags, counts). IPL = tag `ipl` when `flags & 1` (script-managed) and not LOD (`contentFlags & 2`)
- [x] Hashes: unsigned in the DB; search accepts decimal, int32 and hex (`HashText`)
- [x] Reindex: fingerprint (SHA256 of path/size/mtime of every RPF + `GameIndexer.Version`) → skip if equal. Always a full rebuild (~30 s), simpler and more correct than per-file incremental
- [x] Automatic tags (vehicle make, class, type; ped type; prop behaviour and size — revised 2026-11-03, see notes)
- [x] `atlas` CLI (`files`, `index`, `search`, `stats`) for dev/debug

**Done when:** `atlas.db` generated; counts per kind/DLC roughly match plebmasters.

### Phase 2 — UI shell + navigation
- [x] Dark main window, own owner-drawn controls (`Ui/Controls`: `NavSidebar`, `SearchBox`, `Dropdown` with filter, `Segmented`, `ChipBar`, `PillButton`, `Toast`); Segoe UI Variable fonts + Segoe Fluent Icons; dark title bar via DWM
- [x] Sidebar by section (Browse / World / Library) with counts. Header: title + result count + ms, All | ★ Favorites toggle (per category), grid | list
- [x] Search (150 ms debounce, async, stale results discarded), DLC filter (newest first, with text filter) and gender (Clothes only), chips of the most common tags
- [x] **Virtualized** card grid (`AssetGridView`, pages of 200 on demand with LRU in `ResultPager`): image area (per-kind placeholder until Phase 4), name, subtitle, DLC, copy on hover
- [x] Toggle grid ↔ compact list (Ctrl+L)
- [x] Detail in a modal window (`DetailForm`, double-click/Enter): preview on the left (where the 3D viewport goes), on the right hashes, identity, tags, properties (`AssetText.DataFields`) and sources; clicking a row copies it; ←/→ navigates the results; Esc closes
- [x] Shortcuts: Ctrl+F, Enter/↓ in search goes to the grid, Enter opens detail, Esc goes back/clears, Ctrl+C copies the name (clip = `dict clip`), Ctrl+D favorite, Ctrl+L grid/list, arrows/PgUp/PgDn/Home/End
- [x] DLCs page: own table (`DlcTableView`), sortable by column (`DlcTable`), newest first by default; double-click opens All filtered by the pack
- [x] Favorites persisted in the DB, filtered within each category (no separate tab)
- [x] First-run / Reindex screen: pick/detect folder → index with a progress bar (`IndexForm`); errors in `logs\index.log`
- [x] App icon (`Resources/atlas.ico` 16–256 px, generated by `make-icon.py`): teal globe + pin, no Rockstar branding
- [x] Natural sort: numbers by value (`C/0/2/F` before `C/0/10/F`) via a `sort_name` column (`NaturalSort.Key`, zero-padded digits) in the browse indexes

**Done when:** search over ~50k props answers in < 50 ms, no 3D preview yet.

### Phase 3 — 3D viewport (renderer core)
- [x] `ModelViewport : Form, DXForm` (`GtaAtlas.Render`) with `new Renderer(this, cache)`; `TopLevel=false`, hosted in the `DetailForm`'s `PreviewPane`. Hidden until the first model loads (Load → `Renderer.Start`); explicit `Close()` in Dispose releases the device (a child form does not close with its parent)
- [x] `.cso` shaders copied to `Shaders\` in the output (transitive via `GtaAtlas.Render`)
- [x] Orbit (drag), pan (right/middle button), zoom (wheel), reset (double-click / R); background = `Theme.Surface` (`Background`, via reflection on `DXManager.clearcolour`; HDR off because the PostProcessor clears with a fixed blue)
- [x] `GameAssets`: one `GameFileCache` per run, started in the background in `MainForm.OnShown` + a thread pumping `ContentThreadProc`. Init 11 s → ~5 s: `LoadVehicles/Peds/Audio=false` and `CacheInit` skips ymap dictionaries and map caches (reflection over the private `InitDlc` sequence, falls back to the normal `Init`; `CacheInitTests` compares against the source)
- [x] Props (`RenderArchetype`) and vehicles (`GameAssets.LoadVehicle` → `name_hi.yft`/`name.yft` + `RenderFragment`, without `vehicles.meta`) in the detail; ←/→ switches instantly once the cache is ready. Navigation arrows in the footer (nothing paints over the DX window)
- [x] Textured vehicles confirmed in the app
- [x] Bar above the viewport: LOD picker (only levels the model has; L cycles) + Wireframe (W), Bounds (B), Skeleton (S) toggles. LOD via `SelectionModelDrawFlags` applied to each rendered drawable (`RenderedDrawables`), rules in `ModelLods`. Vehicle: High = `_hi.yft`, Medium/Low/VeryLow = levels of the normal `.yft`
- [x] GPU resources released when switching assets: upstream's `RenderableCache` unloads what went unused for 1 s; measured browsing 60 vehicles: GPU 10–50 MB, working set steady at ~1.1 GB
- [x] Cache loading progress in the sidebar (title + %, bar, stage; disappears when done; failure becomes a toast). `InitProgress`: measured per-stage weights (scan 2.2 s, global index 0.8, manifests 0.4, gtxd 0.7, archetypes 1.1, strings 0.3); within the scan it advances by bytes of the top-level RPFs reached so far. `Cache.Init` fallback = indeterminate bar
- [ ] Optional: parallel RPF scan (~2 s serial; needs heavy reflection into `RpfManager`)

**Done when:** opening any prop/vehicle shows a textured, orbitable model.

### Phase 4 — Thumbnails
- [x] Offscreen capture: `ThumbnailRenderer` (384×256) = hidden window with its own device on a dedicated STA thread (with a message loop; the window must be pumped/destroyed by the thread that created it). Waits for geometry + textures on the GPU (4 stable frames, 8 s timeout), resolves MSAA → staging → bytes. Transparent background by *difference matting*: renders over black and over white, alpha = 1 − (white − black) (`ThumbnailMatte`), keeps edges and glass
- [x] Background queue prioritizing visible cards (`ThumbnailQueue`: stack, last painted card first, cap 400; generate-all is a lower-priority FIFO); PNG cache in `thumbs\{kind}\{hash:x8}.png` (`ThumbnailStore`), `.fail` with the reason for missing/empty models. Pauses while the 3D detail is open
- [x] Per-kind placeholder until it exists; "Generate thumbnails" button (props/vehicles of the current result, progress in the sidebar, Stop)
- [x] Automatic framing from the bounding box (`Framing.Fit`: distance to fit the 8 corners + projection recentering); props seen from −Y, vehicles from +Y (¾ front); light comes from the camera side
- [x] Disk limit: `settings.json` → `ThumbnailCacheMb` (default 500). LRU by write time (touched on every read); on start and every 200 saves deletes the oldest down to 90%

### Phase 5 — Full vehicles and peds

Reference pattern: `..\grzyClothTool\CodeWalker\CodeWalker\CustomPedsForm.cs` (ped assembly, drawables/textures per component, anims). GPL → read and reimplement, do not copy.

- [ ] Vehicles: paint (carcols palette), extras, liveries, wheels
- [ ] Vehicle mods (modkit): list and apply parts in the viewport
- [ ] Peds: assemble components (`.ydd` per slot), drawable/texture variations
- [ ] Clothing: preview on a freemode ped (mp_m/mp_f) with the component applied; show `C/{comp}/{drawable}/{tex}` / `P/...`

### Phase 6 — Animations
- [ ] `.ycd` clip player on a ped (freemode skeleton by default; pick a ped)
- [ ] Timeline: play/pause, scrub, speed, loop
- [ ] Search by dictionary and clip; copy `dict` + `clip`

### Phase 7 — Map: MLOs, IPLs, YMAPs
- [ ] MLO preview (rooms + entities, entity sets on/off)
- [ ] YMAP/IPL: render the ymap's entities in isolation; show coordinates
- [ ] 2D map with location (optional: game map tile)
- [ ] "Locations": where a prop appears in the world (reverse index archetype → ymap/position, built by the indexer)

### Phase 8 — DLCs and diffs
- [ ] Per-DLC page: title, pack name, counts per kind (like the plebmasters TOC)
- [ ] "New in X" lists filtered by `dlc_id`
- [ ] Diff between DLCs: added/overridden assets (same name in a later pack = modified)
- [ ] Diff between game versions: DB snapshot per build (`version.txt`) and comparison between snapshots

### Phase 9 — Extras / polish
- [ ] Export scripting code (Lua/JS/C#) — e.g. `CreateObject(\`hash\`, ...)`
- [ ] Export lists as CSV/JSON
- [ ] Enhanced (gen9) support: separate path, separate DB
- [ ] Support a `mods` folder / custom FiveM resources (index a loose folder)
- [ ] Installer / single-file publish
- [x] Release pipeline: tag-triggered GitHub Actions workflow + `scripts/bump-version.ps1`

## Known risks

- **Reflection into CodeWalker privates** (`CacheInit`, `DXManager.clearcolour`): breaks if upstream renames them; `CacheInitTests` warns and the code falls back to the normal path.
- **DXManager assumes a top-level Form**: `ClientSizeChanged` arrives before `InitScene` (guarded in `BuffersResized`); the render loop sleeps 100 ms when the app is not the active window.
- **Memory**: the app's `GameFileCache` is ~400 MB managed after init (archetypes of every DLC), 2 GB LRU file cache. The indexer does not use the cache.
- **SharpDX is discontinued** but works on .NET 10. Migrate to Vortice.Windows only if it causes problems.
- **Readable names** depend on GXT2 / external data; not every prop has a display name.
- **Clothes**: "pretty" names (e.g. "Blue Bangles") come from the shop meta + GXT; some may be missing.

## Resuming in a new session

1. Read this file and `CLAUDE.md`.
2. Find the first phase with `[ ]` items and continue from there.
3. When a block is done: mark `[x]`, add a line under "Session notes".

## Session notes

- 2026-11-02 — Project created. CodeWalker cloned into `external/CodeWalker` (shallow). Decided on .NET 10 + WinForms + Legacy first.
- 2026-11-02 — Looked at `..\grzyClothTool`: uses CodeWalker net48 as a DLL in a net10 app + a hosted `CustomPedsForm : Form, DXForm`. Switched the plan from "port the renderer" to "reference the DLL + own DXForm".
- 2026-11-02 — Port test: `Rendering/**` + 5 files + 2 stubs build on net10 with 0 errors, without editing upstream. Decided on a native port by linking files (no net48 DLL).
- 2026-11-02 — Phase 0 done. 14 tests green (includes integration with the real game). Finding: `x64a.rpf` has no nested RPFs. Reading the keys from the exe takes ~2 s; cached in `settings.json`.
- 2026-11-02 — Phase 1 slice 1: props/MLOs/vehicles/peds. Scanning all RPFs 4 s, full index 29 s, 161k assets. mp2026_01 matches plebmasters (objects 1266, peds 9, MLOs 5; vehicles 14 vs 13 — the extra one is the `trflat2` trailer). Search 25–50 ms. The indexer does not use `GameFileCache` (it merges and loses the origin). `*_g9ec` DLCs are Enhanced packs also present in Legacy.
- 2026-11-02 — Phase 1 slice 2: anims + ymaps. mp2026_01 matches plebmasters 100% except the trailer (anims 3216 including cutscenes, YMAPs 19, IPLs 9). Performance: the full `YcdFile` was 90% of the time → lean reader (1564 s → 71 s CPU). DB rebuilt in a new file without journal/indexes and swapped at the end: write 90 s → 7 s, 497 → 253 MB. Full index 32 s (466k assets). Schema not released yet → edit v1 directly and delete the local DB, no new migrations.
- 2026-11-02 — Phase 1 done (clothing). Findings: (1) `dlclist.xml` mixes `<Item>`/`<item>` — the parser only got half; (2) the real load order = setup2 `order` (confirmed by clothing numbering), but patchdays with a low order re-ship content from later DLCs via `overlay=true` RPFs → general rule: the DLC that introduces an asset = first source outside an overlay (`DlcOverlays` reads content.xml); (3) `*_g9ec` packs are Enhanced: in Legacy they are not part of clothing numbering and other assets get the `enhanced` tag. Validation reference: `DurtyFree/gta-v-data-dumps` (`pedComponentVariations_free.json`, same author as plebmasters) — also useful to validate other phases.
- 2026-11-02 — Phase 2 done (only natural sort of clothing missing). Measured on 471k assets: browse by kind 100 ms → 5–9 ms (expression indexes `(kind, name GLOB '0x*', name)` + count without the dlc JOIN); hash-only names (`0x…`) go last; favorites 632 → 16 ms (subquery starting from `favorite`). Search with a 1–2 letter word still ~250 ms (LIKE full scan) — the UI is async, acceptable. `GetFolderPath` ignores `LOCALAPPDATA` → `GTA_ATLAS_HOME` overrides the data folder (to test first run: `$env:GTA_ATLAS_HOME="..."`). `atlas search --repeat n` measures a warm query.
- 2026-11-02 — UI redesign (user request, visual reference forge.plebmasters): everything owner-drawn with a single theme in `Theme`; side detail panel → modal; favorites per category; sortable DLCs. Icon glyphs need `Theme.Icon*` (the text font shows "·"). `TextBox.PlaceholderText` instead of painting underneath (the TextBox covers it).
- 2026-11-02 — Smooth scroll (grid, list, detail, DLCs, dropdown): `ScrollAnimator` (time-based exponential easing, target accumulates wheel ticks) driven by `FrameClock` (thread on `DwmFlush` = 1 frame per vsync; `Forms.Timer` has ~15.6 ms resolution and stuttered at 144 Hz) with synchronous painting. The grid painted in ~8.5 ms → row cache in bitmaps (32bppRgb, GDI text works) + per-kind placeholder tile: ~2 ms/frame at 2576 px, no frames > 7 ms measured. Hover/selection drawn live on top.
- 2026-11-02 — Phase 2 done: natural sort. First real migration (v2: `sort_name` filled by the SQLite function `natural_key`) instead of editing v1, so favorites are not lost; ~7 s once on the 471k-asset DB, no reindex. v1 now only creates tables; indexes come in v2.
- 2026-11-03 — Phase 3, viewport: the CodeWalker renderer runs on .NET 10 without changes (roadmap risk resolved). `GameFileCache` init profile (11 s): vehicles 3.2 s, RPF scan 2.1 s, archetypes 1.3 s, global dicts 1.2 s, maps 0.9 s, gtxd 0.8 s. Cut vehicles (direct yft) and maps (reflection) → ~5 s, 630 → ~400 MB. Fixed: sidebar count for tag-based categories (IPLs). Visual validation with SendKeys + screenshot (throwaway script); simulated clicks (`mouse_event`) did not trigger the sidebar (fixed 2026-11-03: DPI awareness was missing).
- 2026-11-03 — Phase 3, viewport tools. Bug found: `SelectionFlagsTestAll` without flags drew all LODs on top of each other (`AllModels`). `adder_hi.yft` only has High; the lower LODs live in `adder.yft` → a vehicle loads both. Props can come from `.yft` (e.g. `prop_bench_01a`), not only ydr/ydd. Visual validation: script with SendKeys; clicking the sidebar works with `SetProcessDPIAware` + physical coordinates. The solution's app builds to `bin\x64\Debug` (`bin\Debug` comes from standalone project builds and may be stale).
- 2026-11-03 — Cache progress bar: progress is nearly linear in time (3% at 0.6 s, 39% at 2.5 s, 76% at 5 s, ready at ~6 s). Upstream status during the stages is raw file paths → the UI uses a label per stage (`InitProgress.Label`). To recalibrate weights: force a failure in `GameAssetsTests` to see the `stages:` line (output of passing tests does not show in MTP).
- 2026-11-03 — Phase 4 done. Thumbnail: prop ~140 ms, vehicle ~220 ms; generate-all ~9 vehicles/s. 384×256 PNG with alpha: prop ~14 KB, vehicle ~37 KB (all props ≈ 2.2 GB → hence the LRU limit). Format evaluated: JPEG loses transparency, WebP would need SkiaSharp (GDI+ cannot encode it) — stays PNG; swapping is isolated in `ThumbnailStore`. Findings: closing a window created on a thread without a message loop hangs (a test hung) → own STA thread; vehicle bodies come out white (paint comes from carcols, Phase 5); helicopters come out small (bbox includes the rotor). Working set with the detail open ~1.5 GB (2nd device + renderables cache).
- 2026-11-03 — Post-Phase 4 polish. The detail sidebar misaligned when scrolling: `TextRenderer` ignores the `Graphics` `TranslateTransform` (backgrounds scrolled, text did not) → manual offset per rectangle; don't use transforms in controls that mix GDI and GDI+. Generated tags redone: props lose ytyp/assetType (kept in Properties) and gain `scriptable`, `fragment`, `timed`, `animated`, `lod`, `size: small|medium|large|huge`; peds without `Hash …`; MLOs without ytyp. `scriptable` = ytyp in `PERMANENT_ITYP_FILE` (gta5.meta) or `DLC_ITYP_REQUEST` (content.xml) → always resident, `CreateObject` works anywhere (15,750 props). DurtyFree's `ObjectList.ini` (21,631, the basis of plebmasters' "Scriptable") is a runtime dump: it matches on 15,163; the rest are interiors that were loaded during the dump (they only spawn with the interior loaded), `w_*` weapons (from `weaponarchetypes.meta`, not indexed yet) and DLCs newer than the list. gta5.meta uses `<xi:include>` without declaring the prefix → the XML parser fails unless handled.
- 2026-11-03 — Release setup: `.github/workflows/release.yml` (tag `vX.Y.Z` → check against `<Version>` in `Directory.Build.props`, build, test, publish app + CLI zips, release notes from commits since the previous tag + GitHub-generated PR list) and `scripts/bump-version.ps1`. Must run on Windows (`net10.0-windows`, WinForms/D3D tests). PowerShell pitfall: a function named `Git` shadows `git.exe` (functions win, case-insensitive) → infinite recursion with no output; use `Invoke-Git`. README + repo description/topics added.
