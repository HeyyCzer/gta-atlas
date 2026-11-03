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
├─ src/GtaAtlas.App        UI WinForms (shell, listas, detalhes, viewport); `Browsing/` = lógica testável sem UI
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
- [x] Janela principal dark, controles owner-draw próprios (`Ui/Controls`: `NavSidebar`, `SearchBox`, `Dropdown` com filtro, `Segmented`, `ChipBar`, `PillButton`, `Toast`); fontes Segoe UI Variable + ícones Segoe Fluent Icons; barra de título escura via DWM
- [x] Sidebar por seções (Browse / World / Library) com contagens. Cabeçalho: título + nº de resultados + ms, alternância Todos | ★ Favoritos (por categoria), grid | lista
- [x] Busca (debounce 150 ms, async, resultado velho descartado), filtros DLC (mais recente primeiro, com filtro de texto) e gênero (só Clothes), chips das tags mais comuns
- [x] Grid de cards **virtualizado** (`AssetGridView`, páginas de 200 sob demanda com LRU em `ResultPager`): área de imagem (placeholder por kind até a Fase 4), nome, subtítulo, DLC, copiar no hover
- [x] Alternar grid ↔ lista compacta (Ctrl+L)
- [x] Detalhe em janela modal (`DetailForm`, duplo-clique/Enter): preview à esquerda (lugar do viewport 3D), à direita hashes, identidade, tags, propriedades (`AssetText.DataFields`) e fontes; clique em linha copia; ←/→ navega no resultado; Esc fecha
- [x] Atalhos: Ctrl+F, Enter/↓ na busca vai pro grid, Enter abre detalhe, Esc volta/limpa, Ctrl+C copia nome (clip = `dict clip`), Ctrl+D favorito, Ctrl+L grid/lista, setas/PgUp/PgDn/Home/End
- [x] Página DLCs: tabela própria (`DlcTableView`), ordenável por coluna (`DlcTable`), padrão mais recente primeiro; duplo-clique abre All filtrado pelo pack
- [x] Favoritos persistidos no DB, filtrados dentro de cada categoria (sem aba própria)
- [x] Tela de primeira execução / Reindex: escolher/detectar pasta → indexar com barra de progresso (`IndexForm`); erros em `logs\index.log`
- [x] Ícone do app (`Resources/atlas.ico` 16–256 px, gerado por `make-icon.py`): globo teal + marcador, sem marca Rockstar
- [x] Ordenação natural: números por valor (`C/0/2/F` antes de `C/0/10/F`) via coluna `sort_name` (`NaturalSort.Key`, dígitos com zero à esquerda) nos índices de browse

**Pronto quando:** busca em ~50k props responde em < 50 ms, sem preview 3D ainda.

### Fase 3 — Viewport 3D (núcleo do renderer)
- [x] `ModelViewport : Form, DXForm` (`GtaAtlas.Render`) com `new Renderer(this, cache)`; `TopLevel=false`, hospedado no `PreviewPane` do `DetailForm`. Fica oculto até o 1º modelo carregar (Load → `Renderer.Start`); `Close()` explícito no Dispose libera o device (form filho não fecha com o pai)
- [x] Shaders `.cso` copiados para `Shaders\` no output (transitivo via `GtaAtlas.Render`)
- [x] Orbit (arrastar), pan (botão direito/meio), zoom (roda), reset (duplo-clique / R); fundo = `Theme.Surface` (`Background`, via reflection em `DXManager.clearcolour`; HDR off porque o PostProcessor limpa com azul fixo)
- [x] `GameAssets`: um `GameFileCache` por execução, iniciado em background no `MainForm.OnShown` + thread que bombeia `ContentThreadProc`. Init 11 s → ~5 s: `LoadVehicles/Peds/Audio=false` e `CacheInit` pula dicionários de ymap e map caches (reflection na sequência privada do `InitDlc`, fallback pro `Init` normal; `CacheInitTests` compara com o fonte)
- [x] Props (`RenderArchetype`) e veículos (`GameAssets.LoadVehicle` → `name_hi.yft`/`name.yft` + `RenderFragment`, sem `vehicles.meta`) no detalhe; ←/→ troca na hora com cache pronto. Setas de navegação no rodapé (nada pinta sobre a janela DX)
- [x] Veículos texturizados confirmados no app
- [x] Barra acima do viewport: seletor de LOD (só níveis que o modelo tem; L alterna) + toggles Wireframe (W), Bounds (B), Skeleton (S). LOD via `SelectionModelDrawFlags` aplicado a cada drawable renderizado (`RenderedDrawables`), regras em `ModelLods`. Veículo: High = `_hi.yft`, Medium/Low/VeryLow = níveis do `.yft` normal
- [x] Liberação de recursos GPU ao trocar asset: o `RenderableCache` do upstream descarrega o que ficou 1 s sem uso; medido navegando 60 veículos: GPU 10–50 MB, working set estável ~1,1 GB
- [x] Progresso do carregamento do cache na sidebar (título + %, barra, etapa; some ao terminar; falha vira toast). `InitProgress`: pesos por etapa medidos (scan 2,2 s, índice global 0,8, manifests 0,4, gtxd 0,7, archetypes 1,1, strings 0,3); dentro do scan avança por bytes dos RPFs top-level já alcançados. Fallback `Cache.Init` = barra indeterminada
- [ ] Opcional: scan de RPFs em paralelo (~2 s seriais; exige reflection pesada no `RpfManager`)

**Pronto quando:** abrir qualquer prop/veículo mostra modelo texturizado e orbitável.

### Fase 4 — Thumbnails
- [x] Captura offscreen: `ThumbnailRenderer` (384×256) = janela oculta com device próprio numa thread STA dedicada (com message loop; a janela precisa ser bombeada/destruída pela thread que a criou). Espera geometria + texturas na GPU (4 frames estáveis, timeout 8 s), resolve MSAA → staging → bytes. Fundo transparente por *difference matting*: renderiza sobre preto e sobre branco, alpha = 1 − (branco − preto) (`ThumbnailMatte`), preserva bordas e vidro
- [x] Fila em background priorizando cards visíveis (`ThumbnailQueue`: pilha, último card pintado primeiro, cap 400; gerar-todos é FIFO de menor prioridade); cache PNG em `thumbs\{kind}\{hash:x8}.png` (`ThumbnailStore`), `.fail` com o motivo para modelos inexistentes/vazios. Pausa enquanto o detalhe 3D está aberto
- [x] Placeholder por kind enquanto não existe; botão "Generate thumbnails" (props/veículos do resultado atual, progresso na sidebar, Stop)
- [x] Enquadramento automático pelo bounding box (`Framing.Fit`: distância para caber os 8 cantos + recentragem da projeção); props vistos de −Y, veículos de +Y (frente ¾); luz vem do lado da câmera
- [x] Limite de disco: `settings.json` → `ThumbnailCacheMb` (padrão 500). LRU pela data de escrita (tocada a cada leitura); ao abrir e a cada 200 salvos apaga os mais antigos até 90%

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

- **Reflection em privados do CodeWalker** (`CacheInit`, `DXManager.clearcolour`): quebra se o upstream renomear; `CacheInitTests` avisa e o código cai no caminho normal.
- **DXManager assume Form top-level**: `ClientSizeChanged` chega antes do `InitScene` (guardado em `BuffersResized`); render loop dorme 100 ms quando o app não é a janela ativa.
- **Memória**: `GameFileCache` do app ~400 MB gerenciados após init (archetypes de todas as DLCs), cache de arquivos LRU de 2 GB. Indexer não usa o cache.
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
- 2026-11-02 — Fase 2 concluída (falta só ordenação natural de roupas). Medido em 471k assets: browse por kind 100 ms → 5–9 ms (índices de expressão `(kind, name GLOB '0x*', name)` + count sem JOIN de dlc); nomes só-hash (`0x…`) vão pro fim; favoritos 632 → 16 ms (subquery partindo de `favorite`). Busca com palavra de 1–2 letras ainda ~250 ms (LIKE full scan) — UI é async, aceitável. `GetFolderPath` ignora `LOCALAPPDATA` → `GTA_ATLAS_HOME` sobrepõe a pasta de dados (testar primeira execução: `$env:GTA_ATLAS_HOME="..."`). `atlas search --repeat n` mede query quente.
- 2026-11-02 — Redesign da UI (pedido do usuário, referência visual forge.plebmasters): tudo owner-draw com tema único em `Theme`; painel lateral de detalhe → modal; favoritos por categoria; DLCs ordenáveis. Glyphs de ícone precisam de `Theme.Icon*` (fonte de texto mostra "·"). `TextBox.PlaceholderText` em vez de pintar por baixo (o TextBox cobre).
- 2026-11-02 — Smooth scroll (grid, lista, detalhe, DLCs, dropdown): `ScrollAnimator` (easing exponencial por tempo, alvo acumula giros da roda) dirigido por `FrameClock` (thread em `DwmFlush` = 1 frame por vsync; `Forms.Timer` tem ~15,6 ms de resolução e travava a 144 Hz) com pintura síncrona. Grid pintava em ~8,5 ms → cache de linhas em bitmap (32bppRgb, GDI text funciona) + tile placeholder por kind: ~2 ms/frame em 2576 px, sem frames > 7 ms medidos. Hover/seleção desenhados ao vivo por cima.
- 2026-11-02 — Fase 2 concluída: ordenação natural. Primeira migration real (v2: `sort_name` preenchido por função SQLite `natural_key`) em vez de editar v1, para não perder favoritos; ~7 s uma vez no DB de 471k assets, sem reindex. v1 agora só cria tabelas; índices vêm na v2.
- 2026-11-03 — Fase 3, viewport: renderer do CodeWalker roda em .NET 10 sem ajustes (risco do roadmap resolvido). Profile do init do `GameFileCache` (11 s): veículos 3,2 s, RPF scan 2,1 s, archetypes 1,3 s, dicts globais 1,2 s, mapas 0,9 s, gtxd 0,8 s. Cortados veículos (yft direto) e mapas (reflection) → ~5 s, 630 → ~400 MB. Corrigido: contagem da sidebar para categorias por tag (IPLs). Validação visual com SendKeys + screenshot (script descartável); cliques simulados (`mouse_event`) não acionavam a sidebar (corrigido em 2026-11-03: faltava DPI awareness).
- 2026-11-03 — Fase 3, ferramentas do viewport. Bug achado: `SelectionFlagsTestAll` sem flags desenhava todos os LODs sobrepostos (`AllModels`). `adder_hi.yft` só tem High; os LODs menores ficam em `adder.yft` → veículo carrega os dois. Props podem vir de `.yft` (ex. `prop_bench_01a`), não só ydr/ydd. Validação visual: script com SendKeys; clique na sidebar funciona com `SetProcessDPIAware` + coordenadas físicas. App da solução sai em `bin\x64\Debug` (o `bin\Debug` é de builds avulsos do projeto, pode estar velho).
- 2026-11-03 — Barra de progresso do cache: avanço quase linear no tempo (3% em 0,6 s, 39% em 2,5 s, 76% em 5 s, pronto ~6 s). Status do upstream durante as etapas são caminhos de arquivo crus → UI usa rótulo por etapa (`InitProgress.Label`). Recalibrar pesos: forçar falha no `GameAssetsTests` para ver a linha `stages:` (saída de teste aprovado não aparece no MTP).
- 2026-11-03 — Fase 4 concluída. Thumbnail: prop ~140 ms, veículo ~220 ms; gerar-todos ~9 veículos/s. PNG 384×256 com alpha: prop ~14 KB, veículo ~37 KB (todos os props ≈ 2,2 GB → por isso o limite LRU). Avaliado formato: JPEG perde a transparência, WebP precisaria de SkiaSharp (GDI+ não codifica) — fica PNG; trocar isola-se no `ThumbnailStore`. Achados: Close de janela criada em thread sem message loop trava (teste travou) → thread STA própria; corpo do veículo sai branco (pintura vem do carcols, Fase 5); helicópteros saem pequenos (bbox inclui rotor). Working set com detalhe aberto ~1,5 GB (2º device + cache de renderables).
- 2026-11-03 — Polimento pós-Fase 4. Sidebar do detalhe desalinhava ao rolar: `TextRenderer` ignora `TranslateTransform` do `Graphics` (fundos rolavam, texto não) → offset manual por retângulo; não usar transform em controles que misturam GDI e GDI+. Tags geradas refeitas: props perdem ytyp/assetType (ficam em Properties) e ganham `scriptable`, `fragment`, `timed`, `animated`, `lod`, `size: small|medium|large|huge`; peds sem `Hash …`; MLOs sem ytyp. `scriptable` = ytyp em `PERMANENT_ITYP_FILE` (gta5.meta) ou `DLC_ITYP_REQUEST` (content.xml) → sempre residente, `CreateObject` funciona em qualquer lugar (15.750 props). `ObjectList.ini` do DurtyFree (21.631, base do "Scriptable" do plebmasters) é dump em runtime: bate em 15.163; o resto são interiores que estavam carregados no dump (só spawnam com o interior carregado), armas `w_*` (vêm de `weaponarchetypes.meta`, ainda não indexadas) e DLCs mais novos que a lista. gta5.meta usa `<xi:include>` sem declarar prefixo → parser XML falha sem tratar.
