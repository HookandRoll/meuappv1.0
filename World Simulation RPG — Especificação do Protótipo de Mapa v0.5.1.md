# Jota Fantasy Map Maker

## Especificação do Protótipo de Mapa v0.5.1

**Status:** Pré-produção · **Escopo:** Gerador + Editor 2D de mapa hexagonal + campanha mínima · **Plataforma:** PC · **Engine:** Godot 4 · **Linguagem:** C#

## Mudanças da v0.5 para a v0.5.1

- Texto sem LaTeX; seção 7 corrigida.
- **Rios e estradas por aresta:** `RoadEdges` e `RiverEdges` guardam tier/largura por aresta compartilhada. A arte feita à mão entra e sai pelo ponto médio da aresta. Substitui `RoadMask`, `RoadTier` e `RiverMask`.
- Regra de regeneração de rios na borda da seleção e semântica dos locks de rede.
- M0 inclui o empacotamento de campos de 16 bits nas texturas de dados.
- Caches derivados recalculados no load; Priority-Flood e lagos; unidade de elevação definida.
- Congelamento unificado (seção 25) e backlog explícito.

## 1. Objetivo

Criar um software 2D capaz de:

- criar um mundo hexagonal e gerar sua geografia proceduralmente;
- editar o mapa manualmente, com undo/redo e locks;
- visualizar propriedades por layers, com grade opcional (terreno contínuo);
- gerar e editar rios; desenhar estradas simples;
- anotar hexes, marcar POIs e a posição da party;
- salvar, carregar e exportar imagem do mundo.

O protótipo **não** simula civilizações. Ele precisa provar que o mapa é agradável e confiável, **e** útil numa sessão real de RPG.

## 2. Não objetivos

Economia, população, comércio, política, guerra, culturas, personagens, catálogo completo, multiplayer/P2P, VTT, fog of war, clima dinâmico, simulação histórica, assentamentos, IA, mods, Workshop, **importação de imagem** (pós-protótipo), polos e wrap horizontal ativo na UI.

## 3. Arquitetura

```text
WORLD DATA  ->  MAP LOGIC  ->  RENDERING
```

- O dado lógico descreve o mundo (`Biome=Grassland`, `Elevation=132`). O renderer decide a aparência. Separação obrigatória.
- **Núcleo em C# puro**, sem dependência de `Godot.*`: grid, geração, locks, undo, save. Godot cuida de render, input e UI. Isso permite testes rápidos e geração em background thread.
- **Sem invalidação automática e soberania do GM:** nada muda sozinho fora da ferramenta ou seleção atual. Regenerar é sempre ação explícita. O gerador nunca sobrescreve dados travados nem propaga efeitos para fora da seleção.

## 4. Grid hexagonal

**Pointy-top.** Armazenamento primário em array retangular offset (`odd-r`, `index = row * width + col`). Coordenadas axiais `Hex(q, r)` só servem para cálculo (distância, linha, anéis), sempre via a classe central `HexGrid`, que converte `Offset <-> Axial <-> Screen`.

> **Regra de identidade:** entidades esparsas, chaves de dicionário e caminhos (`OrderedHexPath[]`) guardam sempre o índice linear canônico (`int HexIndex`), nunca coordenada axial.

### Direções e sockets (ordem oficial)

Sockets são as **seis arestas**, numerados no sentido horário a partir do leste:

```text
S0=E   S1=SE   S2=SW   S3=W   S4=NW   S5=NE
```

Vetores axiais (q, r): E(+1,0) · SE(0,+1) · SW(-1,+1) · W(-1,0) · NW(0,-1) · NE(+1,-1).

O socket oposto de Sx é S((x+3) % 6). O socket Sx de um hex casa sempre com S((x+3) % 6) do vizinho naquela direção.

### Vértices

```text
V0=N  V1=NE  V2=SE  V3=S  V4=SW  V5=NW   (horário a partir do topo)
```

A aresta S0 (E) fica entre V1 e V2; S1 entre V2 e V3; e assim por diante. Vértices servem de referência geométrica para contornos e shaders.

### Redes por aresta (rios e estradas)

Rios e estradas são definidos **por aresta**, não por hex. Cada aresta compartilhada entre dois hexes guarda um único valor:

- `RoadEdges` (ushort): 2 bits por socket. Os bits (2i+1, 2i) guardam o tier da estrada na aresta Si: 0 = nenhuma, 1 = Trail, 2 = Dirt, 3 = Stone.
- `RiverEdges` (ushort): mesma estrutura; o valor é a classe de largura: 0 = nenhum, 1 = riacho, 2 = rio, 3 = grande rio.
- A máscara de conexão de 6 bits é **derivada**: o bit i vale 1 quando o valor da aresta Si é diferente de 0.
- **Regra de aresta compartilhada:** gravar o valor v em Si de um hex grava o mesmo v em S((i+3) % 6) do vizinho. Cada aresta tem uma só verdade, então os dois hexes sempre se encontram com o mesmo estilo e a mesma largura na fronteira.
- **Contrato de arte:** toda peça de rede entra e sai pelo **ponto médio da aresta**, perpendicular a ela, com posição e largura fixas por tier/classe. É isso que garante alinhamento entre peças desenhadas à mão, qualquer que seja a rotação ou o vizinho. Cada tier/classe terá um guia com largura e ponto de contato.
- Hex na borda do mapa (sem vizinho) grava apenas o próprio lado.

## 5. Projeção, wrap e polos

- Mapa retangular com aparência cilíndrica; grade regular.
- `HorizontalWrap` existe no manifest, mas fica **fixo em OFF na UI** deste protótipo.
- Toda consulta de vizinhança e distância já aceita a flag `wrapHorizontal` em `HexGrid` (testada no M1), para não exigir refatoração futura.
- Quando o wrap for ativado na geração, o ruído será amostrado em coordenadas cilíndricas (cos θ, sen θ, y).
- Polos não fazem parte desta fase.

## 6. Escala física e tamanho

Salvar `HexDistance` e `DistanceUnit` (1, 5, 10, 25 km ou custom). Não altera o desenho; será usado por sistemas de viagem.

Presets: 128×64 · 256×128 · 512×256 · 1024×512. Mundo de desenvolvimento: **256×128 (32.768 hexes)**. Benchmark: **1024×512 (524.288 hexes)**.

### Metas iniciais de desempenho (ajustar após o M0)

| Métrica | 256×128 | 1024×512 |
| --- | --- | --- |
| FPS em pan/zoom | ≥ 60 | ≥ 30 (alvo 60) |
| Abrir o arquivo | < 2 s | < 5 s |
| Trocar layer de visualização | < 50 ms | < 100 ms |
| Geração completa | < 10 s | < 60 s |
| Memória de dados do mapa | — | < 200 MB |

## 7. Chunks e texturas de dados

**Chunks lógicos de 32×32 hexes (1.024 por chunk).**

- **Na CPU:** dirty flags, snapshots de undo, seleção de regiões, reconstrução de decoração e peças de rede.
- **Na GPU (terreno contínuo):** a estratégia preferida é manter **texturas de dados globais**, 1 texel por hex (1024×512 texels), sem seams de blending entre chunks.
- **Campos de 16 bits** (`Elevation`, `Temperature`, `Rainfall`) não cabem em um canal de 8 bits. O M0 define o empacotamento (proposta: RGBA8 com 2 canais para cada campo de 16 bits e canais próprios para IDs de bioma, água e relevo) e usa filtro *nearest* nos IDs.
- **A forma de atualizar a GPU é decisão do M0.** Medir: (1) atualização integral da textura, (2) texturas segmentadas, (3) via de baixo nível só se o profiling justificar. Uma textura 1024×512 RGBA8 tem cerca de 2 MB; atualizá-la inteira após uma pincelada pode bastar. O benchmark decide.

Chunks continuam sendo unidade lógica mesmo que o renderer use texturas globais.

## 8. Dados densos (arrays compactos)

Existem em todo hex, em *Struct-of-Arrays* planos (`T[]` de tamanho largura × altura), sem objeto C# por hex. Os eixos são ortogonais, para evitar estados contraditórios (ex.: oceano com bioma deserto).

| Campo | Tipo | Bytes | Observação |
| --- | --- | --- | --- |
| `Elevation` | short | 2 | Metros (escala abstrata do mundo). 0 = nível do mar; negativo = submerso. |
| `WaterType` | byte | 1 | None, Ocean, Lake, Wetland. |
| `ReliefType` | byte | 1 | Flat, Rolling, Hills, Mountains, Peak. Derivado na geração, pintável pelo GM. |
| `BiomeType` | byte | 1 | None/Aquatic, Beach, Grassland, TemperateForest, Rainforest, Savanna, Steppe, Desert, Taiga, Tundra, Alpine. |
| `Temperature` | short | 2 | Décimos de °C (235 = 23,5 °C). |
| `Rainfall` | ushort | 2 | Precipitação anual em mm. |
| `RoadEdges` | ushort | 2 | Tier por aresta (seção 4). **Fonte da verdade.** |
| `RiverEdges` | ushort | 2 | Largura por aresta. **Cache** derivado das entidades `River`. |
| `Flags` | byte | 1 | HasPOI, HasNote, IsCoast (cache) etc. |
| `LockMask` | ushort | 2 | 1 bit por campo travável (9 de 16 usados). |

**Total: 16 bytes por hex** (cerca de 8 MB em 1024×512; 16 KB por chunk). **TraversalCost não é armazenado:** é derivado de bioma + relevo + `RoadEdges` + `RiverEdges` + água; só overrides manuais ficam no dicionário esparso.

**Fonte da verdade × caches.** `RiverEdges`, `IsCoast` e o índice `HexToRivers` são caches: **não são gravados no save e são recalculados no load** (rios a partir das entidades `River`; costa a partir de elevação e água). Snapshots de undo guardam a fonte (entidades e campos densos), e os caches são recalculados após desfazer.

## 9. Dados esparsos

Ficam fora dos arrays, em dicionários indexados por `int HexIndex` ou `EntityID`:

- Entidades `River` e o índice derivado `HexToRivers` (consultado só quando `RiverEdges != 0`).
- POIs, **notas de texto**, **marcador da party** e overrides manuais de custo de movimento.
- Todo registro de campanha carrega `Visibility` (`GM_ONLY` ou `PUBLIC`).

## 10. Locks

Por hex ou por campo, via `ushort LockMask`. Ao regenerar (mundo, região ou campo), campos com o bit ligado nunca são alterados; os destravados são recalculados.

**Locks de rede:** travar `RiverEdges` ou `RoadEdges` em um hex protege **todas as arestas daquele hex**. Como a aresta é compartilhada, ela fica congelada se **qualquer um** dos dois hexes estiver travado. Rios que passam por hex travado não são removidos nem redirecionados; funcionam como pontos fixos na regeneração.

## 11. Regeneração e soberania do GM

O usuário pode regenerar o mundo inteiro, uma **região selecionada** ou um **campo específico**, em três modos:

1. **Tudo:** refaz terra/água, elevação e o que depende dela dentro da seleção (respeitando locks).
2. **Manter terra/água:** preserva `WaterType == Ocean` e regenera relevo, clima, rios e biomas na seleção.
3. **Manter terra/água + elevação:** preserva `Elevation` e `ReliefType`; regenera clima, rios e biomas.

Também é possível regenerar um único campo (só `Rainfall`, só `Biomes`).

> **Regra de ouro (GM > simulação):** nenhum hex fora da seleção é modificado. Se uma edição manual fizer um rio subir o morro ou ficar desconectado, o software aceita a decisão do GM; a view de Debug pode sinalizar a anomalia.

### Rios na borda da seleção

- **Arestas compartilhadas entre hex selecionado e hex não selecionado são congeladas.** Nunca são alteradas, nem a máscara do lado de fora.
- Rio totalmente dentro da seleção: é regenerado.
- Rio que cruza a borda: o trecho de fora permanece; o trecho de dentro é refeito, usando as arestas congeladas da borda como pontos de entrada e saída.
- Se não houver caminho válido (ex.: o ponto de saída está mais alto), o rio termina no último hex alcançável com `EndType = Custom`, e a view de Debug marca a anomalia. O sistema não corrige sozinho.
- O mesmo vale para rios que atravessam hexes travados.

## 12. Undo / redo

Padrão **Command**, cobrindo toda edição manual **e regeneração**: pintura, elevação, bioma, água, apagar, rios, estradas, notas, POIs e locks.

Antes de uma operação destrutiva, grava-se um snapshot com:

1. Os dados densos dos **chunks afetados**, incluindo os vizinhos imediatos quando uma aresta compartilhada na borda for alterada.
2. Cópia profunda das **entidades esparsas tocadas** (rios modificados, notas, POIs).

Como cada chunk consome cerca de 16 KB, um snapshot de 16 chunks custa cerca de 256 KB, o que permite dezenas de níveis de Ctrl+Z.

## 13. Pipeline de geração

```text
SEED -> LAND/WATER -> ELEVATION -> DEPRESSION FILL (temp) -> TEMPERATURE -> RAINFALL -> HYDROLOGY -> BIOMES
```

Cada etapa executa separadamente, usa a `Seed`, respeita `LockMask` e opera só no conjunto de hexes alvo (mundo inteiro ou seleção ativa).

- **Ruído por coordenada global:** toda função de ruído amostra a posição absoluta do hex, nunca a posição relativa à seleção. Regenerar uma região emenda sem costura com o entorno.
- **Determinismo:** `Seed + WorldSettings + GeneratorVersion` produz o mesmo mundo dentro da versão suportada do aplicativo. Preferir ruído implementado pelo projeto e quantizar para os tipos inteiros dos arrays ao final de cada etapa importante; as etapas seguintes consomem os valores quantizados.
- **Identidade bit a bit entre CPUs e sistemas operacionais não é gate do protótipo.** O `.world` salva os arrays gerados. Determinismo multiplataforma estrito só será perseguido se surgir um caso de uso real.

### Etapas

- **Terra e água:** `Seed`, `LandPercentage`, `ContinentalScale`, `TerrainRoughness`. Sem tectônica.
- **Elevação e relevo:** `short` com sinal para todo hex (terra ≥ 0, oceano < 0) e derivação inicial de `ReliefType`. Ferramentas manuais: Raise, Lower, Set, Smooth, Flatten.
- **Temperatura:** latitude + queda por altitude (provisório: 6,5 °C por 1000 m) + temperatura base do mundo.
- **Precipitação (v1 simples):** proximidade do oceano + latitude + elevação. Rain shadow e vento ficam no backlog.
- **Biomas:** tabela determinística derivada de `Temperature`, `Rainfall`, `Elevation` e `WaterType`. Sempre pintáveis à mão.

### Escultura continental

O modo **Escultura continental** aplica domos, bacias, cordilheiras, vales e estreitos à grade inteira antes das etapas de relevo, clima e biomas. A regeneração regional reconstrói esse campo com a mesma seed e grava os campos gerados na região solicitada.

Agradecemos a Azgaar e à equipe do **Fantasy Map Generator** pelas receitas de mapas de elevação que inspiraram este modo. [Azgaar’s Fantasy Map Generator](https://github.com/Azgaar/Fantasy-Map-Generator)

## 14. Hidrologia e rios

Na **geração**, um passo temporário de preenchimento de depressões (*Priority-Flood*) cria uma superfície auxiliar para que os rios sigam elevação decrescente até oceano, lago ou pântano. Depois da geração, o GM edita rios e elevação livremente, sem recálculo automático.

**Lagos (provisório):** bacia preenchida com área maior que `LakeMinSize` hexes vira `WaterType = Lake`; bacias menores servem só como superfície auxiliar e não alteram o mapa.

### Rio como entidade

```text
River {
    int RiverID,
    int[] OrderedHexPath,   // índices canônicos, da nascente ao hex final (inclusive)
    RiverEndType EndType,   // Ocean, Lake, Wetland, Custom (término definido pelo GM ou por falha de caminho)
    byte WidthClass,        // 1 = riacho, 2 = rio, 3 = grande rio
    int? ParentRiverID      // preenchido se for afluente
}
```

- A **entidade é a fonte da verdade.** `RiverEdges` é derivado: cada par consecutivo do caminho grava `WidthClass` na aresta compartilhada entre os dois hexes (incluindo a aresta da foz, até o hex de água).
- Nascente e término manual têm uma só aresta ativa no hex; o renderer desenha a peça de nascente ou de ponta.
- Confluência: o afluente termina no hex do rio principal; o hex passa a ter arestas dos dois.
- Criar, editar ou apagar um `River` recalcula `RiverEdges` do seu caminho e marca os chunks como *dirty*.
- Para saber quais rios passam por um hex (Inspector, ferramentas), consulta-se `HexToRivers`.

## 15. Estradas

Manuais no protótipo: Trail (1), Dirt Road (2), Stone Road (3).

- Estradas não têm fluxo nem hierarquia, então ficam direto no array denso `RoadEdges` (tier por aresta), sem entidade.
- Cada passo desenhado entre dois hexes grava o tier da ferramenta ativa na aresta compartilhada (seção 4). Desenhar sobre uma aresta existente **substitui** o tier: a aresta tem um valor só.
- Apagar zera a aresta nos dois hexes.
- **Limitação conhecida:** não há tier por hex. O que muda de estilo é sempre a aresta.

## 16. Renderização (Godot 4)

Modelo **Hybrid Procedural Shader + MultiMesh + peças de rede por aresta**.

### Terreno contínuo e troca de layers

- Os campos densos de terreno vão para texturas de dados globais (seção 7). A forma exata de atualização fica aberta ao M0.
- O **Terrain Shader** deve: derivar a posição lógica do hex sem criar Nodes por hex; fazer blending suave entre biomas vizinhos; desenhar costa e praia contínuas; desenhar o grid proceduralmente (`Show Hex Grid ON/OFF`); alternar entre modos de análise (Terrain, Elevation, Temperature, Rainfall, Biomes, Locks) por uniforms sobre as mesmas texturas.
- Troca de layer só por uniform: alvo visualmente instantâneo (< 16 ms); gate geral < 100 ms em 1024×512.
- Seleção e picking por **matemática de coordenada** (`ScreenToHex`), sem `Node2D`/`Area2D` por hex.

### Relevo e vegetação

Preferir `MultiMeshInstance2D` (ou lote equivalente) por chunk, alimentado por `ReliefType` e `BiomeType`. Nodes individuais continuam aceitáveis para elementos realmente esparsos. O benchmark decide.

### Rios e estradas (renderer substituível)

A fonte lógica é sempre `RiverEdges`/`River` e `RoadEdges`. O visual pode trocar sem alterar o save.

- **Candidato A (preferido, para arte feita à mão): peças modulares por aresta.** Por tier/classe: um *braço* (do centro ao ponto médio da aresta), rotacionado para cada um dos 6 sockets; uma peça central de junção; peças de nascente e de ponta. O hex é montado percorrendo as arestas ativas. Como todo braço termina no ponto médio da aresta com largura fixa (contrato da seção 4), o alinhamento entre hexes é garantido por construção. Alternativa dentro do mesmo candidato: peças completas por padrão de arestas, com mais controle artístico e mais arte.
- **Candidato B: malhas procedurais (ribbons)** por chunk, a partir de curvas entre o centro e os pontos médios das arestas.
- O M5 testa o candidato A primeiro, com arte placeholder. O objetivo é provar continuidade, edição e desempenho, não congelar uma técnica de desenho.

### Nodes / Sprites individuais

Só para elementos esparsos (dezenas ou poucas centenas no mapa): POIs, ícones de notas, marcador da party e labels.

## 17. Layers

Ordem gráfica:

1. **Base Shader** (terreno, água, costa, biomas, modo de análise)
2. **Relief / Vegetation** (MultiMesh por chunk)
3. **Rivers** (peças por aresta)
4. **Roads** (peças por aresta)
5. **Hex Grid Overlay**
6. **POIs, Notes e Party Marker**
7. **Debug / Selection Overlay**

Cada layer tem `Visible` e `Opacity`.

## 18. Editor

Ferramentas: Select, Paint Relief, Paint Biome, Raise, Lower, Set Elevation, Smooth, Flatten, Paint Water, Erase/Reset, Draw River, Draw Road (com seleção de tier), Lock, Unlock, **Note, POI, Place Party**.

Brushes: tamanho, força, forma (Single Hex, Radius; depois Line, Region).

Seleção múltipla (retângulo ou brush) com operações em lote: lock, unlock, regenerate (3 modos ou campo isolado), paint biome/relief, set elevation, delete road, delete river.

**Inspector do hex:** coordenadas (Offset e Axial), elevação, água, relevo, bioma, temperatura, chuva, rios que cruzam o hex, arestas de estrada (tier por aresta), custo de travessia (derivado ou override), locks ativos, **notas e POIs**.

## 19. Campanha mínima

- **Notas por hex:** título e texto livre (Markdown ou texto simples), no dicionário esparso.
- **POIs:** ícone, nome, descrição curta, cor e `Visibility`.
- **Marcador da party:** posição atual, movível com clique.
- **Busca rápida:** filtro em memória por título/conteúdo de notas e nomes de POIs.

## 20. Save (`.world`)

Contêiner único ZIP, lido e gravado por streams (`System.IO.Compression.ZipArchive`):

```text
MyWorld.world (ZIP)
├── manifest.json          (versão, seed, dimensões, escala)
├── dense_data.bin         (campos densos fonte, binário contínuo)
├── sparse_entities.json   (rios, POIs, notas, party, overrides)
└── preview.png
```

- **Campos gravados:** `Elevation`, `WaterType`, `ReliefType`, `BiomeType`, `Temperature`, `Rainfall`, `RoadEdges`, `Flags` (sem caches) e `LockMask`. `RiverEdges`, `IsCoast` e `HexToRivers` são recalculados no load.
- **Sem SQLite e sem pasta temporária no protótipo:** 8 MB em RAM (1 a 2 MB comprimidos) e entidades pequenas cabem em JSON.
- **Save atômico:** grava `MyWorld.world.tmp`, faz flush e substitui o arquivo válido via `File.Replace`/`File.Move(overwrite)`. Nunca sobrescreve direto.
- **Manifest:** `WorldSaveVersion, WorldName, Seed, GeneratorVersion, Width, Height, HorizontalWrap, HexDistance, DistanceUnit, CreatedAt, ModifiedAt`.
- **Migração:** `WorldSaveVersion` existe desde o M1; os migradores só são obrigatórios a partir do M7.

## 21. Exportação de imagem (PNG)

Opções: com grade, sem grade, layers visíveis selecionadas e filtro **Player-Safe** (omite POIs e notas `GM_ONLY`).

- Presets de resolução seguros (2K, 4K, 8K, até 16K), respeitando o limite de textura da GPU.
- Em resolução máxima para mapas grandes: renderização em quadrantes (*tiled viewport rendering*), costurados em stream antes de codificar o PNG.

## 22. Engenharia e testes

- **Threading:** geração em `Task.Run` sobre os arrays densos; ao terminar, a thread principal só envia os buffers às texturas e reconstrói peças dos chunks.
- **Progresso:** etapa atual + porcentagem (Land, Elevation, DepressionFill, Climate, Hydrology, Biomes).
- **Debug views:** bordas de chunk, coordenadas, sockets, arestas de rede, direção e anomalias de rio, valores de elevação, IDs de bioma, campos travados.
- **Testes automatizados (projeto Core, sem Godot):**
  - conversão Offset ↔ Axial e identidade canônica;
  - vizinhança e distância (wrap ON e OFF);
  - **consistência de aresta compartilhada** (valor gravado em Si aparece em S((i+3) % 6) do vizinho) e empacotamento/desempacotamento dos 2 bits por aresta;
  - reprodutibilidade por seed após quantização;
  - ruído por coordenada global (sub-região igual à geração global na mesma área);
  - round-trip de save/load, com `RiverEdges` e demais caches reconstruídos idênticos;
  - respeito a `LockMask` e às arestas congeladas da borda de seleção;
  - **regeneração de rios que cruzam a borda** (trecho de fora intacto, término `Custom` quando sem caminho);
  - undo/redo de múltiplos chunks e rios cortados.

### Estrutura de código

```text
src/
  Core/        HexCoord, HexGrid, WorldData, WorldSettings, ChunkDirtyTracker   (C# puro)
  Generation/  WorldGenerator, Noise, LandStep, ElevationStep, PriorityFlood,
               ClimateStep, HydrologyStep, BiomeStep                            (C# puro)
  Entities/    River, Poi, HexNote, PartyMarker                                 (C# puro)
  Editor/      Brush, EditorCommand, ChunkSnapshot, UndoManager, LockSystem     (C# puro)
  Save/        WorldSerializer, WorldManifest, SaveMigration                    (C# puro)
  GodotApp/    MapRenderer, GlobalDataTextures, TerrainShader, ChunkMultiMesh,
               EdgePieceRenderer, CameraController, MapInput, Ui/               (Godot 4 C#)
```

## 23. Marcos

### M0 — Dá para renderizar?

Pergunta: um mundo 1024×512 (524.288 hexes) roda com boa responsividade usando texturas de dados globais + shader contínuo + MultiMesh?

Implementar: pointy-top, 1024×512, pan, zoom, picking matemático, texturas de dados candidatas **com o empacotamento de campos de 16 bits**, shader de terreno com blending de 2–3 biomas, costa e grade procedural, e um lote de teste de relevo/vegetação. Medir primeiro a atualização integral da textura; segmentação ou baixo nível só se o profiling exigir.

**Gate:** metas de FPS (≥ 30, alvo 60) e de troca de layer (< 100 ms; alvo < 16 ms por uniform) da seção 6. Se falhar, ajustar o pipeline de GPU antes do M1.

### M1 — Dá para guardar?

Implementar: `WorldData` (16 B/hex), `HexGrid` (com testes de wrap e de aresta compartilhada), layers básicas, inspector de leitura e save/load atômico (`manifest.json` + `dense_data.bin` + `sparse_entities.json`).

**Gate:** round-trip preserva 100% dos dados em < 5 s para 1024×512, sem file locks.

### M2 — Dá para gerar?

Implementar: pipeline quantizado (terra/água, elevação/relevo, temperatura, chuva, biomas) em worker thread com barra de progresso.

**Gate:** mesma `Seed + WorldSettings + GeneratorVersion` reproduz o mesmo resultado no ambiente suportado; regenerar sub-região sem locks não cria costuras. Identidade bit a bit entre plataformas não é requisito.

### M3 — Dá para editar e sobrescrever o gerador?

Implementar: brushes, pintura de relevo/bioma/água, ferramentas de elevação, seleção regional, `LockMask`, `UndoManager` por snapshots, 3 modos de regeneração e regeneração de campo único.

**Gate:** editar → travar → regenerar região → desfazer → refazer, com zero mutação fora da seleção.

### M4 — Dá para ficar bonito?

Implementar: transições de bioma no shader, costa suavizada, modo natural (grade OFF) vs. estratégico (grade ON) e decoração de relevo/vegetação em lote.

**Gate:** com a grade desligada o mapa parece uma ilustração contínua, com desempenho próximo ao do M0.

### M5 — Dá para pôr redes?

Implementar: hidrologia (Priority-Flood + entidades `River` com afluentes), sincronização de `RiverEdges`, desenho/apagar de rios e estradas **por aresta**, e o renderer inicial de **peças modulares por aresta** (candidato A) com arte placeholder.

**Gate:** rios gerados nascem em terra alta e chegam ao mar/lago; peças de hexes vizinhos se encontram alinhadas no ponto médio da aresta, com mesma largura e estilo; o GM edita rios, estradas e elevação sem o sistema desfazer suas alterações; regenerar rios na borda da seleção segue a seção 11; tudo sobrevive a Undo/Redo e Save/Load.

### M6 — Um GM consegue usar de verdade?

Implementar só: notas por hex, POIs, marcador da party, busca em memória e `Visibility`.

**Gate:** o GM abre um mundo, acha uma região pela busca, consulta/adiciona notas e POIs e move o grupo sem ferramenta externa.

### M7 — Outra pessoa consegue usar?

Implementar: PNG (quadrantes e Player-Safe), `SaveMigration`, autosave/backup simples, atalhos, polimento de UI e tratamento de erros.

**Gate:** usuário externo cumpre toda a Definition of Done sem ajuda e sem perda de dados.

### Pós-protótipo — M8: importar um mundo existente

Importação de imagem para geração assistida. Só começa depois do M7.

## 24. Definition of Done

O protótipo está concluído quando o usuário consegue:

1. criar um mundo, definir seed, tamanho e escala física;
2. gerar continentes, elevação, temperatura, chuva, rios e biomas;
3. alternar visualizações de análise instantaneamente e ligar/desligar a grade;
4. clicar em qualquer hex e inspecionar/editar suas propriedades;
5. pintar regiões, proteger alterações com locks e ter suas edições respeitadas acima das regras do gerador;
6. regenerar uma propriedade ou região (em cada modo) sem afetar o resto do mundo, e desfazer a regeneração, inclusive rios;
7. desenhar estradas por aresta e editar rios, com peças alinhadas entre hexes vizinhos;
8. escrever notas num hex, criar POIs, buscar textos e mover o marcador da party;
9. salvar, fechar e carregar o mesmo mundo rapidamente e sem perda de dados;
10. exportar PNG do mapa (visão do GM ou dos jogadores);
11. cumprir as metas de desempenho da seção 6.

## 25. Congelamento de escopo e backlog

A v0.5.1 é a **baseline de implementação**. O escopo fica congelado pela Definition of Done. Ideias novas só entram se corrigirem um bloqueio direto dos gates M0–M7 ou um risco de corrupção de dados; as demais vão para o backlog.

- Decisões marcadas como **preferida**, **candidata** ou **provisória** são deliberadamente testáveis e só mudam por benchmark, teste automatizado, limitação real da engine ou problema de usabilidade.
- O resultado do código tem prioridade sobre especulação de arquitetura.

### Backlog pós-protótipo

- Vento por bandas de latitude e rain shadow.
- `.world` somente leitura para compartilhar com os jogadores (remoção física de `GM_ONLY`).
- Calendário configurável, timeline central e cálculo de viagem.
- Importação de imagem (M8).
- Notas com carga sob demanda ou SQLite, se a campanha crescer.
- Wrap horizontal ativo e polos.
- Determinismo multiplataforma estrito.
- Assentamentos (decidir se um hex pode ter mais de um).

---

**Fim — Especificação do Protótipo de Mapa v0.5.1**
