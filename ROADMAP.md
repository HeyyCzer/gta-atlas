# GTA Atlas — Roadmap

Ferramenta desktop local (alternativa ao forge.plebmasters.de) para explorar assets do GTA V:
props, veículos, peds, roupas, animações, MLOs, IPLs, YMAPs e DLCs — com busca instantânea e preview 3D interativo.

## Decisões fixas

| Tema | Decisão |
|---|---|
| Runtime | .NET 10, WinForms (`net10.0-windows`), dark mode via `Application.SetColorMode` |
| Parsing de arquivos do jogo | `CodeWalker.Core` (netstandard2.0) de `external/CodeWalker` |
| Renderização 3D | **Port nativo net10 por link de arquivos**: `GtaAtlas.Render` faz `<Compile Include>` dos fontes do upstream (sem copiar/editar) + `Stubs.cs`. Testado 2026-11-02: compila com 0 erros. Nosso Form implementa `CodeWalker.Rendering.DXForm`, `TopLevel=false`, hospedado como controle filho. Shaders `.cso` de `external/CodeWalker/Shaders` copiados pro output |
| Licença | CodeWalker sem LICENSE explícito (só `Notice.txt`). grzyClothTool é GPL-3.0 → **só referência, não copiar código** (senão o app vira GPL) |
| Cache | SQLite (`Microsoft.Data.Sqlite`) + FTS5 para busca; thumbnails PNG em disco, gerados sob demanda |
| Jogo alvo inicial | Legacy: `C:\SteamLibrary\steamapps\common\Grand Theft Auto V`. Enhanced (gen9) depois |
| Dados do usuário | `%LOCALAPPDATA%\GtaAtlas\` → `atlas.db`, `thumbs\`, `settings.json`, `logs\` |

## Arquitetura

```
GtaAtlas.sln
├─ external/CodeWalker                   (upstream, não editar; ideal virar git submodule)
│    └─ CodeWalker.Core.csproj           ProjectReference direto (netstandard2.0)
├─ src/GtaAtlas.Core       AppPaths, AtlasSettings, detecção do jogo (GameLocator), chaves (GameKeys), GameSession
├─ src/GtaAtlas.Data       schema SQLite, migrations, repositórios, queries FTS
├─ src/GtaAtlas.Indexer    varre RPFs via GameFileCache/RpfManager → grava no DB (incremental)
├─ src/GtaAtlas.Render     fontes linkados do CodeWalker (abaixo) + Stubs.cs + ViewportForm : Form, DXForm + cenas + captura de thumbnails
├─ src/GtaAtlas.App        UI WinForms (shell, listas, detalhes, viewport)
└─ tests/GtaAtlas.Tests    testes de hash, parsing, queries
```

### Fontes do CodeWalker linkados em `GtaAtlas.Render` (prova de conceito validada)

```xml
<Compile Include="..\..\external\CodeWalker\CodeWalker\Rendering\**\*.cs" />           <!-- 34 arquivos -->
<Compile Include="..\..\external\CodeWalker\CodeWalker\Utils\MapUtils.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\Utils\TextureLoader.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\World\Widget.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\GameFiles\TextureFormats.cs" />
<Compile Include="..\..\external\CodeWalker\CodeWalker\Properties\Settings.Designer.cs" />
```
Pacotes: SharpDX, SharpDX.D3DCompiler, SharpDX.Direct3D11, SharpDX.Direct2D1, SharpDX.Mathematics (4.2.0). `AllowUnsafeBlocks`, `Nullable=disable` nesse projeto.
`Stubs.cs`: enum `CodeWalker.MapSelectionMode` (copiado de `World/MapSelection.cs`, que puxa `WorldForm`) e `GameFileCacheFactory.Create()` lançando exceção (sempre passamos o cache).

Princípio: **a UI nunca varre o jogo**. Lista/busca/filtro = só SQLite. O jogo é aberto apenas para
(a) indexar, (b) carregar o asset selecionado no viewport, (c) gerar thumbnail ausente.

### Schema (rascunho)

- `dlc(id, name, pack_name, title, order_index, build, released_at)` — ordem vinda de `dlclist.xml` + `setup2.xml`/`content.xml`
- `asset(id, kind, name, hash, display_name, dlc_id, rpf_path, entry_path, extra_json)`
  - `kind`: prop, vehicle, ped, clothing, anim_dict, anim_clip, mlo, ipl, ymap
- `asset_tag(asset_id, tag)` — tags derivadas (classe do veículo, fabricante, tipo do prop, componente da roupa, etc.)
- `asset_fts` — FTS5 sobre name/display_name/tags/hash em texto (decimal, int32, hex)
- `vehicle(asset_id, make, class, type, modkit, seats, ...)`, `ped(...)`, `clothing(...)`, `mlo(...)` — colunas específicas
- `rpf_file(path, size, mtime, indexed_at)` — para reindex incremental
- `meta(key, value)` — versão do schema, versão do jogo indexada

## Fases

Cada fase termina em algo utilizável e verificável. Marque `[x]` ao concluir e anote desvios em "Notas de sessão".

### Fase 0 — Fundação
- [x] Solução `GtaAtlas.slnx` + projetos, `Directory.Build.props` (nullable, LangVersion latest, x64); `external/Directory.Build.props` vazio isola o upstream
- [x] `CodeWalker.Core` via ProjectReference; `GtaAtlas.Render` com fontes linkados + stubs (ver seção acima)
- [x] `settings.json` (path do jogo, gen9, AES key em cache) + detecção automática (registro Rockstar, bibliotecas Steam via `libraryfolders.vdf`, manifests Epic)
- [x] Carregar chaves (`GTA5Keys.LoadFromPath`) e abrir/extrair de `x64a.rpf` → teste de integração
- [x] `git init` + `.gitignore`; CodeWalker como submodule
- [x] Testes xUnit v3 (MTP via `global.json`); testes de jogo pulam se não houver install (`GTA_ATLAS_GAME_DIR` força path)

**Pronto quando:** console/test lista entradas de `x64a.rpf`.

### Fase 1 — Indexer + DB
- [x] Schema + migrations em `GtaAtlas.Data` (`PRAGMA user_version`; `asset` genérico + `data` JSON por kind, `asset_source` com todas as ocorrências, FTS5 trigram)
- [x] Mapear DLCs: `dlclist.xml` + `setup2.xml` (order, deviceName, timeStamp). Packs fora da lista entram no fim
- [x] Props: archetypes de todos os `.ytyp` (nome, hash, bbox, lodDist, flags, assetType, dicts)
- [x] Veículos: `vehicles.meta` + nomes GXT2 (display name, fabricante, classe, tipo, modkits). `carcols`/`carvariations` → Fase 5
- [x] Peds: `peds.meta`/`peds.ymt` (nome, pedType, clipsets)
- [x] Roupas (freemode M/F): 1 asset por drawable (`C/6/137/M`, `P/7/8/M`), texturas no JSON com nome GXT (`textLabel` do shop meta) + nome de dev (comentário XML). Índice global validado: 679/679 collections batem com dump de runtime (DurtyFree/gta-v-data-dumps) e total 5995 = plebmasters. Regras: ordem = setup2 `order`; collection que só existe em RPF overlay não existe; reenvio em patch mantém posição e usa conteúdo mais novo; Legacy ignora collections `*_g9ec`
- [x] Animações: `.ycd` → dicionários e clips (nome, duração), incluindo cutscenes (tag `cutscene`). Leitor próprio `YcdClipReader` (só nomes/durações), validado contra `YcdFile` em teste
- [x] MLOs: archetypes `CMloArchetypeDef` (rooms, portals, entity sets)
- [x] YMAPs / IPLs: todo `.ymap` (extents, parent, flags, contagens). IPL = tag `ipl` quando `flags & 1` (script-managed) e não LOD (`contentFlags & 2`)
- [x] Hashes: unsigned no DB; busca aceita decimal, int32 e hex (`HashText`)
- [x] Reindex: fingerprint (SHA256 de path/size/mtime de todos os RPFs + `GameIndexer.Version`) → pula se igual. Rebuild sempre total (~30s), mais simples e correto que incremental por arquivo
- [x] Tags automáticas (fabricante, classe, tipo de veículo, pedType, ytyp, assetType)
- [x] CLI `atlas` (`files`, `index`, `search`, `stats`) para dev/debug

**Pronto quando:** `atlas.db` gerado; contagens por kind/DLC batem aproximadamente com o plebmasters.

### Fase 2 — Shell da UI + navegação
- [ ] Janela principal dark: sidebar (Props, Vehicles, Peds, Clothes, Animations, MLOs, IPLs, YMAPs, DLCs, Favoritos)
- [ ] Barra superior: busca (debounce, FTS), filtros (DLC, categoria, gênero), chips de tags populares
- [ ] Grid de cards **virtualizado** (owner-draw, só desenha visíveis) com nome, badge de DLC, tags, botão copiar
- [ ] Alternar grid ↔ lista compacta
- [ ] Painel/modal de detalhe: name, hash/int32/hex com copiar, display name, DLC, descrição, campos específicos
- [ ] Atalhos: Ctrl+F, Enter abre, Ctrl+C copia nome, setas navegam
- [ ] Favoritos persistidos no DB
- [ ] Tela de primeira execução: escolher path → indexar com barra de progresso
- [ ] Ícone do app (`.ico` multi-resolução 16–256 px) usado no exe, janela e taskbar; identidade visual própria (nada de logo Rockstar/GTA)

**Pronto quando:** busca em ~50k props responde em < 50 ms, sem preview 3D ainda.

### Fase 3 — Viewport 3D (núcleo do renderer)
- [ ] `ViewportForm : Form, DXForm` (net10) usando `new Renderer(this, gameFileCache)`; `TopLevel=false`, `FormBorderStyle.None`, adicionado como filho do painel de detalhe
- [ ] Shaders `.cso` copiados para a pasta de saída (`Shaders\`), como o CodeWalker espera
- [ ] Orbit/pan/zoom, reset câmera, fundo configurável
- [ ] Um único `GameFileCache` compartilhado no app (carregamento lento → iniciar em background no startup)
- [ ] Carregar `.ydr`/`.ydd`/`.yft` pelo `GameFileCache` e exibir
- [ ] Props no detalhe com viewport
- [ ] Toggle wireframe / bounds / skeleton; seletor de LOD
- [ ] Liberação de recursos GPU ao trocar asset (sem vazamento)

**Pronto quando:** abrir qualquer prop/veículo mostra modelo texturizado e orbitável.

### Fase 4 — Thumbnails
- [ ] Captura offscreen: ViewportForm oculto 256×256, renderizar asset e ler o backbuffer (staging texture → PNG)
- [ ] Fila em background priorizando cards visíveis; cache PNG em `thumbs\{kind}\{hash}.png`
- [ ] Placeholder enquanto não existe; botão "gerar todos" opcional
- [ ] Enquadramento automático pelo bounding box

### Fase 5 — Veículos e Peds completos

Referência de padrão: `..\grzyClothTool\CodeWalker\CodeWalker\CustomPedsForm.cs` (montagem de ped, drawables/texturas por componente, anims). GPL → ler e reimplementar, não copiar.

- [ ] Veículos: pintura (paleta carcols), extras, liveries, rodas
- [ ] Vehicle mods (modkit): listar e aplicar peças no viewport
- [ ] Peds: montar componentes (`.ydd` por slot), variações de drawable/textura
- [ ] Roupas: preview sobre ped freemode (mp_m/mp_f) com o componente aplicado; mostrar `C/{comp}/{drawable}/{tex}` / `P/...`

### Fase 6 — Animações
- [ ] Player de clip `.ycd` em ped (skeleton freemode por padrão; escolher ped)
- [ ] Timeline: play/pause, scrub, velocidade, loop
- [ ] Busca por dicionário e clip; copiar `dict` + `clip`

### Fase 7 — Mapa: MLOs, IPLs, YMAPs
- [ ] Preview de MLO (rooms + entidades, entity sets on/off)
- [ ] YMAP/IPL: renderizar entidades do ymap isoladamente; mostrar coordenadas
- [ ] Mapa 2D com localização (opcional: tile do mapa do jogo)
- [ ] "Locations": onde um prop aparece no mundo (índice reverso archetype → ymap/posição, gerado no indexer)

### Fase 8 — DLCs e diffs
- [ ] Página por DLC: título, pack name, contagens por tipo (igual ao TOC do plebmasters)
- [ ] Listas "novos em X" filtradas por `dlc_id`
- [ ] Diff entre DLCs: assets adicionados/sobrescritos (mesmo nome em pack posterior = modificado)
- [ ] Diff entre versões do jogo: snapshot do DB por build (`version.txt`) e comparação entre snapshots

### Fase 9 — Extras / polimento
- [ ] Exportar código de scripting (Lua/JS/C#) — ex. `CreateObject(\`hash\`, ...)`
- [ ] Exportar listas CSV/JSON
- [ ] Suporte Enhanced (gen9): path separado, DB separado
- [ ] Suporte a pasta `mods`/resources FiveM customizados (indexar pasta solta)
- [ ] Instalador / publish single-file

## Riscos conhecidos

- **Runtime do renderer em .NET 10 ainda não testado** (só compilação). `Settings.Default` (ApplicationSettingsBase) usa valores default dos atributos; validar câmera/shaders no primeiro viewport. Fallback se algo travar: compilar CodeWalker net48 como DLL (padrão grzyClothTool).
- **DXManager assume Form top-level** (assina `ResizeBegin/End`, `FormClosing`). Como filho, repassar resize manualmente se necessário.
- **Memória**: `GameFileCache` completo usa vários GB; no indexer, carregar por tipo e liberar. No app, cache LRU só do que está em preview.
- **SharpDX está descontinuado** mas funciona em .NET 10. Migrar para Vortice.Windows só se der problema.
- **Nomes legíveis** dependem de GXT2 / dados externos; nem todo prop tem display name.
- **Clothes**: nomes "bonitos" (ex. "Blue Bangles") vêm de shop meta + GXT; parte pode faltar.

## Como retomar em uma nova sessão

1. Ler este arquivo e `CLAUDE.md`.
2. Achar a primeira fase com itens `[ ]` e continuar dela.
3. Ao terminar um bloco: marcar `[x]`, adicionar linha em "Notas de sessão".

## Notas de sessão

- 2026-11-02 — Projeto criado. CodeWalker clonado em `external/CodeWalker` (shallow). Decididos .NET 10 + WinForms + Legacy primeiro.
- 2026-11-02 — Visto `..\grzyClothTool`: usa CodeWalker net48 como DLL em app net10 + `CustomPedsForm : Form, DXForm` hospedado. Trocado plano de "portar renderer" para "referenciar DLL + DXForm próprio".
- 2026-11-02 — Teste de port: `Rendering/**` + 5 arquivos + 2 stubs compilam em net10 com 0 erros, sem editar upstream. Decidido port nativo por link de arquivos (sem DLL net48).
- 2026-11-02 — Fase 0 concluída. 14 testes verdes (inclui integração com jogo real). Achado: `x64a.rpf` não tem RPFs aninhados. Leitura de chaves a partir do exe leva ~2s; cache em `settings.json`.
- 2026-11-02 — Fase 1 fatia 1: props/MLOs/veículos/peds. Scan de todos os RPFs 4s, index total 29s, 161k assets. mp2026_01 bate com plebmasters (objects 1266, peds 9, MLOs 5; veículos 14 vs 13 — o extra é o trailer `trflat2`). Busca 25–50 ms. Indexer não usa `GameFileCache` (mescla e perde origem). DLCs `*_g9ec` são packs do Enhanced também presentes no Legacy.
- 2026-11-02 — Fase 1 fatia 2: anims + ymaps. mp2026_01 bate 100% com plebmasters exceto trailer (anims 3216 incluindo cutscenes, YMAPs 19, IPLs 9). Performance: `YcdFile` completo era 90% do tempo → leitor enxuto (1564s→71s CPU). DB reconstruído em arquivo novo sem journal/índices e trocado no fim: escrita 90s→7s, 497→253 MB. Index total 32s (466k assets). Schema ainda não lançado → editar v1 direto e apagar DB local, sem migrations novas.
- 2026-11-02 — Fase 1 concluída (roupas). Achados: (1) `dlclist.xml` mistura `<Item>`/`<item>` — parser pegava só metade; (2) ordem de carga real = setup2 `order` (confirmado pela numeração de roupas), mas patchdays com order baixo reenviam conteúdo de DLCs posteriores via RPFs `overlay=true` → regra geral: DLC que introduz um asset = primeira fonte fora de overlay (`DlcOverlays` lê content.xml); (3) packs `*_g9ec` são do Enhanced: no Legacy não entram na numeração de roupas e demais assets ganham tag `enhanced`. Referência de validação: `DurtyFree/gta-v-data-dumps` (`pedComponentVariations_free.json`, mesmo autor do plebmasters) — útil também para validar outras fases.
