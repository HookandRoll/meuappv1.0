# Especificação de Novos Biomas, Relevos Especiais e Integração de Artes

Este documento descreve todos os **novos biomas**, **relevos geomorfológicos** (vulcões, escarpas, cânions, badland rocks) e **vegetações/ícones** cujas artes (em 3 variações cada: `.png`, `_v2.png`, `_v3.png`) já foram geradas e salvas nas pastas do projeto (`tiles/`, `relief/`, `vegetation/`, `Icons/`), prontas para quando você quiser ativá-las no código.

---

## 1. Catálogo das Novas Artes Criadas

Seguindo o padrão da arquitetura do `MapArtwork.cs` e do `Terrain.gdshader` (**3 variações por elemento** para evitar repetição visual no mapa), foram geradas **75 novas imagens**:

### A. Texturas de Solo dos Novos Biomas (`tiles/` — 512 × 512 px, seamless RGBA)

| Arquivo Base (`.png`, `_v2.png`, `_v3.png`) | Nome em Português | Enum Sugerido (`BiomeType`) | Cor Base Fallback (`biome_color`) | Estilo Visual da Textura |
| :--- | :--- | :--- | :--- | :--- |
| [glacier.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/glacier.png) | **Geleira / Calota Polar** | `Glacier` | `vec3(0.80, 0.87, 0.90)` | Gelo compacto azul-claro e branco com estrias de fluxo glacial e fendas de pressão sutis |
| [seasonal_forest.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/seasonal_forest.png) | **Floresta Tropical Sazonal** | `SeasonalForest` | `vec3(0.29, 0.36, 0.17)` | Solo florestal verde-oliva profundo com manchas de folhagem sazonal âmbar/dourada |
| [shrubland.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/shrubland.png) | **Chaparral / Cerrado / Mediterrâneo** | `Shrubland` | `vec3(0.49, 0.43, 0.27)` | Terra ocre-terracota seca mesclada com manchas de arbustos verde-sálvia e seixos |
| [badlands.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/badlands.png) | **Badlands / Deserto Rochoso** | `Badlands` | `vec3(0.58, 0.37, 0.25)` | Estratos sedimentares erodidos em faixas terracota, ferrugem, ocre e argila seca |
| [temperate_rainforest.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/temperate_rainforest.png) | **Floresta Temperada Chuvosa** | `TemperateRainforest` | `vec3(0.18, 0.31, 0.19)` | Solo úmido verde-esmeralda escuro coberto de musgo espesso e samambaias |
| [mangrove.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/mangrove.png) | **Manguezal** | `Mangrove` | `vec3(0.24, 0.31, 0.22)` | Lodo costeiro verde-escuro com canais sinuosos de água salobra turquesa-escura |
| [swamp.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/swamp.png) | **Pântano Arborizado** | `Swamp` | `vec3(0.23, 0.29, 0.19)` | Terra alagada escura com espelhos de água parada esverdeada e lentilhas-d'água |
| [bog.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/bog.png) | **Turfeira Fria** | `Bog` | `vec3(0.40, 0.31, 0.20)` | Tapete de musgo esfagno acobreado/ferrugem, turfa escura e poças de água fria |
| [oasis.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/oasis.png) | **Oásis / Mata Ciliar** | `Oasis` | `vec3(0.32, 0.43, 0.23)` | Solo fértil verde-viva com veios d'água azul-turquesa e transição suave para areia dourada |
| [volcanic.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/volcanic.png) | **Terras Vulcânicas / Cinzas** | `Volcanic` | `vec3(0.22, 0.21, 0.23)` | Rocha basáltica negra e cinza vulcânica com fissuras sutis incandescentes de lava |
| [salt_flat.png](file:///d:/Documentos/ChatGPT/Mapmaker/tiles/salt_flat.png) | **Deserto de Sal / Salar** | `SaltFlat` | `vec3(0.86, 0.83, 0.77)` | Crosta de sal branco-marfim com rede poligonal natural de ressecamento |

---

### B. Sobreposições de Relevo e Geomorfologia (`relief/` — 256 × 256 px, RGBA transparente)

Feitas no mesmo estilo de nanquim, hachuras e iluminação lateral (`lum < 0.25` para traço escuro, `0.25..0.75` para encosta que recebe a cor do bioma no shader):

| Arquivo Base (`.png`, `_v2.png`, `_v3.png`) | Feature Geográfica | Enum Sugerido (`ReliefType` ou Subtipo) | Quando usar / gerar |
| :--- | :--- | :--- | :--- |
| [volcano.png](file:///d:/Documentos/ChatGPT/Mapmaker/relief/volcano.png) | **Vulcão** | `Volcano` | Picos em zonas de subducção/rifte tectônico ou hexes com bioma `Volcanic` |
| [escarpment.png](file:///d:/Documentos/ChatGPT/Mapmaker/relief/escarpment.png) | **Escarpa / Paredão** | `Escarpment` | Borda de um **Planalto** (ex.: hex a `1200m` vizinho de hex a `100m`, com $\Delta H_{\text{queda}} \ge 500\text{ m}$) |
| [canyon.png](file:///d:/Documentos/ChatGPT/Mapmaker/relief/canyon.png) | **Cânion / Garganta** | `Canyon` | Hex atravessado por rio (`RiverEdges != 0`) dentro de um Planalto ou Badlands com paredes mais altas ao redor |
| [badland_rocks.png](file:///d:/Documentos/ChatGPT/Mapmaker/relief/badland_rocks.png) | **Badland Rocks / Mesas** | `BadlandRocks` | Relevo de morros/chapadas tabulares (`Hills` ou `Mountains`) dentro dos biomas `Badlands`, `Desert` ou `Shrubland` |

---

### C. Sobreposições de Vegetação Isométrica (`vegetation/` — 256 × 256 px, RGBA transparente)

Compostas a partir do pacote *Yughues Isometric Plant Pack* com sombras projetadas no solo:

| Arquivo Base (`.png`, `_v2.png`, `_v3.png`) | Bioma Associado | Composição Visual |
| :--- | :--- | :--- |
| [oasis_palms.png](file:///d:/Documentos/ChatGPT/Mapmaker/vegetation/oasis_palms.png) | `Oasis` | Lagoa azul-turquesa na base cercada por palmeiras, arbustos tropicais e gramíneas |
| [mangrove_trees.png](file:///d:/Documentos/ChatGPT/Mapmaker/vegetation/mangrove_trees.png) | `Mangrove` | Árvores costeiras com raízes aéreas arqueadas sobre água salobra e juncos |
| [seasonal_trees.png](file:///d:/Documentos/ChatGPT/Mapmaker/vegetation/seasonal_trees.png) | `SeasonalForest` | Árvores folhosas em tons verde-oliva e dourado sazonal com bambus e arbustos |
| [shrubland_bushes.png](file:///d:/Documentos/ChatGPT/Mapmaker/vegetation/shrubland_bushes.png) | `Shrubland` | Arbustos baixos retorcidos verde-sálvia e tufos de capim seco |
| [swamp_trees.png](file:///d:/Documentos/ChatGPT/Mapmaker/vegetation/swamp_trees.png) | `Swamp` / `Bog` | Árvores pantanosas escuras com musgo, juncos e espelho d'água sombrio |

---

### D. Ícones de Detalhe (`Icons/` — 128 × 128 px, RGBA transparente)

Para a camada de detalhes ao dar zoom (`MapDetailIcons.cs`):

| Arquivo Base (`.png`, `_v2.png`, `_v3.png`) | Enum Sugerido (`Icon`) | Onde aparece (`Habitat`) |
| :--- | :--- | :--- |
| [vulcao.png](file:///d:/Documentos/ChatGPT/Mapmaker/Icons/vulcao.png) | `Vulcao` | Montanhas/picos vulcânicos e bioma `Volcanic` |
| [rocha_badlands.png](file:///d:/Documentos/ChatGPT/Mapmaker/Icons/rocha_badlands.png) | `RochaBadlands` | Biomas `Badlands`, `Desert`, `Shrubland` |
| [escarpa.png](file:///d:/Documentos/ChatGPT/Mapmaker/Icons/escarpa.png) | `Escarpa` | Bordas de planaltos e transição de altitude brusca |
| [canion.png](file:///d:/Documentos/ChatGPT/Mapmaker/Icons/canion.png) | `Canion` | Vales profundos e rios encaixados em zonas secas/planaltos |
| [oasis.png](file:///d:/Documentos/ChatGPT/Mapmaker/Icons/oasis.png) | `Oasis` | Desertos com água subterrânea ou próximo a rios secos |

---

## 2. Regras Climáticas e Geográficas para `ChooseBiome` (Quando Implementar)

No `WorldGenerator.cs`, a temperatura é guardada em **décimos de °C** (ex.: `225` = `22,5 °C`, `-120` = `-12,0 °C`), a chuva em **mm/ano** (`0` a `5000`) e a elevação em **metros**.

### A. Regras de Clima (`ChooseBiome`)

```csharp
private static BiomeType ChooseBiome(short elevation, short temperature, ushort rainfall, bool forests = true) =>
    (elevation, temperature, rainfall) switch
    {
        // 1. Extremo frio e picos congelados -> Geleira
        (>= 3600, <= -35, _) or (_, <= -125, _) => BiomeType.Glacier,

        // 2. Alta montanha fria -> Alpino e Tundra
        (>= 2300, <= 35, _) => BiomeType.Alpine,
        (_, <= -55, _) => BiomeType.Tundra,

        // 3. Cinturão boreal frio (<= 4.5 °C)
        (_, <= 45, >= 1150) => BiomeType.Bog, // Turfeira fria muito úmida
        (_, <= 45, >= 400) when forests => BiomeType.Taiga,

        // 4. Desertos e Badlands (muito secos)
        (_, < 110, < 220) => BiomeType.Badlands, // Deserto frio / rochoso
        (_, >= 110, < 260) => BiomeType.Desert,

        // 5. Semiárido: Estepe vs Chaparral/Cerrado (Shrubland)
        (_, >= 120 and < 225, >= 360 and < 680) => BiomeType.Shrubland,
        (_, _, < 480) => BiomeType.Steppe,

        // 6. Cinturão Tropical Quente (>= 20.0 °C)
        (_, >= 225, >= 1550) when forests => BiomeType.Rainforest,
        (_, >= 200, >= 1050) when forests => BiomeType.SeasonalForest, // Corrige o buraco entre Savana e Rainforest!
        (_, >= 200, < 1050) => BiomeType.Savanna,

        // 7. Cinturão Temperado Úmido
        (_, >= 45 and < 175, >= 1850) when forests => BiomeType.TemperateRainforest,
        (_, _, >= 1000) when forests => BiomeType.TemperateForest,

        // 8. Padrão: Campo / Pradaria
        _ => BiomeType.Grassland
    };
```

### B. Regras Contextuais (Costa, Rios, Bacias e Tectônica)

Após o `ChooseBiome` básico, 4 biomas dependem do contexto do hex vizinho:

1. **Manguezal (`BiomeType.Mangrove`):**
   - Hex costeiro (`(Flags[i] & WorldData.CoastFlag) != 0`), baixa altitude (`Elevation[i] <= 90`), clima tropical/subtropical (`Temperature[i] >= 195`) e úmido (`Rainfall[i] >= 950` ou foz de rio `RiverEdges[i] != 0`).
2. **Oásis (`BiomeType.Oasis`):**
   - Hex classificado como `Desert` ou `Badlands` que é cortado por um rio (`RiverEdges[i] != 0`) ou é vizinho direto de um `WaterType.Lake` de água doce.
3. **Pântano (`BiomeType.Swamp`) e Turfeira (`BiomeType.Bog`):**
   - Hexes planos (`ReliefType.Flat`, `Elevation[i] <= 260`) com alta umidade/acumulação hídrica ou vizinhos de `WaterType.Wetland`:
     - Se `Temperature[i] <= 55` $\rightarrow$ `Bog` (Turfeira fria).
     - Se `Temperature[i] > 55` $\rightarrow$ `Swamp` (Pântano arborizado).
4. **Salar (`BiomeType.SaltFlat`) e Vulcânico (`BiomeType.Volcanic`):**
   - **Salar:** Bacias endorreicas planas (`ReliefType.Flat`) em clima árido (`Rainfall[i] < 280`).
   - **Vulcânico:** Hexes próximos a limites convergentes/riftes em `world.Tectonics` com `Elevation[i] >= 2400`.

---

## 3. Passo a Passo de Onde Alterar no Código Quando For Implementar

1. **`src/Core/WorldData.cs`:**
   - Adicionar os novos valores ao enum `BiomeType` e, se desejar relevos dedicados no pincel, ao enum `ReliefType` (`Escarpment`, `Canyon`, `Volcano`, `BadlandRocks`).
   - Atualizar `Biomes.IsForest(BiomeType)` para incluir `SeasonalForest`, `TemperateRainforest`, `Mangrove` e `Swamp`.
2. **`src/GodotApp/MapArtwork.cs`:**
   - Adicionar os novos nomes na chamada `Load("tiles", 512, false, ...)`:
     `"glacier"`, `"seasonal_forest"`, `"shrubland"`, `"badlands"`, `"temperate_rainforest"`, `"mangrove"`, `"swamp"`, `"bog"`, `"oasis"`, `"volcanic"`, `"salt_flat"`.
   - Adicionar em `Load("relief", 256, true, ...)`:
     `"volcano"`, `"escarpment"`, `"canyon"`, `"badland_rocks"`.
   - Adicionar em `Load("vegetation", 256, true, ...)`:
     `"oasis_palms"`, `"mangrove_trees"`, `"seasonal_trees"`, `"shrubland_bushes"`, `"swamp_trees"`.
3. **`src/GodotApp/Terrain.gdshader`:**
   - Atualizar `SHALLOW_OCEAN_ART` e `DEEP_OCEAN_ART` (ou manter os 4 tiles de água depois do último bioma terrestre para a fórmula `base_art = water != 0 ? NUM_BIOMES + water : biome - 1` continuar direta).
   - Adicionar as cores fallback em `biome_color(float id)` e os índices em `vegetation_index(int biome, int water)`.
4. **`src/GodotApp/MapView.cs` e `src/GodotApp/MapEditorPanel.cs`:**
   - Adicionar os rótulos em português em `MapView.BiomeLabel(BiomeType)` e os novos botões na paleta de biomas (`_palette` em `MapEditorPanel.cs`).
5. **`src/GodotApp/MapDetailIcons.cs`:**
   - Adicionar `"vulcao"`, `"rocha_badlands"`, `"escarpa"`, `"canion"`, `"oasis"` ao array `FileNames` e mapeá-los no `Habitat(WorldData, int)`.
