# GTA Atlas

Explorador local de assets do GTA V (props, veículos, peds, roupas, animações, MLOs, IPLs, YMAPs, DLCs) com preview 3D.
Plano e progresso: [ROADMAP.md](ROADMAP.md) — sempre ler antes de começar e atualizar ao terminar.

- C# / .NET 10 / WinForms (`net10.0-windows`, x64)
- `external/CodeWalker` = submodule upstream (dexyfex/CodeWalker). Não editar. `CodeWalker.Core` via ProjectReference; renderer = fontes do upstream linkados via `<Compile Include>` em `GtaAtlas.Render` + `Stubs.cs`. Viewport = nosso `Form : DXForm` hospedado.
- `..\grzyClothTool` é GPL-3.0: usar só como referência, nunca copiar código.
- Jogo Legacy: `C:\SteamLibrary\steamapps\common\Grand Theft Auto V`; Enhanced: `...\Grand Theft Auto V Enhanced`
- UI nunca varre o jogo: listas/busca vêm do SQLite; jogo só é lido para indexar, preview e thumbnails.
- Dados do usuário em `%LOCALAPPDATA%\GtaAtlas\`.

## Comandos

- Build: `dotnet build GtaAtlas.slnx`
- Testes: `dotnet test --solution GtaAtlas.slnx` (xUnit v3 / Microsoft.Testing.Platform)
- Clonar: `git clone --recurse-submodules` (ou `git submodule update --init`)

## Convenções de trabalho

- Commitar a cada etapa concluída (build + testes verdes), não acumular.
- Testes automatizados para o que importa: parsing, indexer, queries, regras de diff, stubs vs upstream. UI e render só onde der para testar sem GPU.
- Testes que dependem do jogo usam `TestEnv.RequireGame()` (pulam sem install) e `[Collection(GameCollection.Name)]` (chaves do CodeWalker são estáticas).
