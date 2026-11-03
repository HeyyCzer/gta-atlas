# GTA Atlas

Local explorer for GTA V assets (props, vehicles, peds, clothing, animations, MLOs, IPLs, YMAPs, DLCs) with a 3D preview.
Plan and progress: [ROADMAP.md](ROADMAP.md) — always read it before starting and update it when done.

- C# / .NET 10 / WinForms (`net10.0-windows`, x64)
- `external/CodeWalker` = upstream submodule (dexyfex/CodeWalker). Do not edit. `CodeWalker.Core` via ProjectReference; renderer = upstream sources linked via `<Compile Include>` in `GtaAtlas.Render` + `Stubs.cs`. Viewport = our hosted `Form : DXForm`.
- `..\grzyClothTool` is GPL-3.0: use it only as a reference, never copy code.
- Legacy game: `C:\SteamLibrary\steamapps\common\Grand Theft Auto V`; Enhanced: `...\Grand Theft Auto V Enhanced`
- The UI never scans the game: lists/search come from SQLite; the game is only read to index, preview and render thumbnails.
- User data in `%LOCALAPPDATA%\GtaAtlas\`.

## Commands

- Build: `dotnet build GtaAtlas.slnx`
- App: `dotnet run --project src/GtaAtlas.App` (data in `GTA_ATLAS_HOME` if set)
- Tests: `dotnet test --solution GtaAtlas.slnx` (xUnit v3 / Microsoft.Testing.Platform)
- Clone: `git clone --recurse-submodules` (or `git submodule update --init`)
- Release: `./scripts/bump-version.ps1 patch|minor|major [-Push]` → tag triggers `.github/workflows/release.yml`

## Working conventions

- Commit after each finished step (build + tests green), don't pile up changes.
- Automated tests for what matters: parsing, indexer, queries, diff rules, stubs vs upstream. UI and rendering only where testable without a GPU.
- Tests that depend on the game use `TestEnv.RequireGame()` (skip without an install) and `[Collection(GameCollection.Name)]` (CodeWalker keys are static).
- Docs (README, ROADMAP, this file) are written in English.
