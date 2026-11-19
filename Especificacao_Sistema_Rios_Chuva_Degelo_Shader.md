# Especificação Técnica: Sistema de Rios Realista (Chuva, Degelo, Priority-Flood e Renderização via Shader)

> **Destinatário:** Agente de Desenvolvimento / Arquiteto de Software  
> **Contexto:** Projeto `Mapmaker` (`Mapmaker.Core`, `WorldData.cs`, `WorldGenerator.cs`, Godot 4 `Terrain.gdshader`)  
> **Integração no Pipeline:** **Camada 2 (Hidrologia e Renderização Fluvial)** — opera sobre o relevo e clima gerados a partir de `Especificacao_Sistema_Placas_Tectonicas.md` (Camada 0) e `Especificacao_Ilhas_Bahias_Peninsulas_Estilo_Azgaar.md` (Camada 1).  
> **Referência:** Hidrologia do *Azgaar's Fantasy Map Generator* (`river-generator.ts`), *Priority-Flood* (Barnes et al.) e renderização de rios em jogos 4X (*Civilization VI* / *Humankind*).

---

## 1. Visão Geral e Conexão com o Projeto (`Mapmaker.Core`)

Rios nascem onde a gravidade coleta água de **chuva orográfica** e **degelo nival/glacial**, fluindo continuamente até o oceano ou lagos terminais.

### Unidades Reais do `Mapmaker.Core` (Obrigatório Respeitar)
Verificadas em `src/Core/WorldData.cs` e `src/Core/WorldGenerator.cs`:
* `world.Elevation[i]` (`short`): altitude em **metros** (`-1` ou `Water != None` para água; costa `<= 200 m`; montanhas/degelo `> 1800 m`; alpino `> 2850 m`).
* `world.Temperature[i]` (`short`): temperatura em **décimos de °C** (ex.: `45` = `4,5 °C`, `-30` = `-3,0 °C`, `225` = `22,5 °C`). Use sempre `float tempC = world.Temperature[i] * 0.1f;`.
* `world.Rainfall[i]` (`ushort`): precipitação em **mm/ano** (ex.: `360` mm = seco/taiga, `710` mm = temperado, `1360+` mm = floresta tropical).
* `world.RiverEdges[i]` (`ushort`): **já existe** em `WorldData.cs`, `UndoManager.cs` e na serialização `.world`!

### Conexão com a Camada Tectônica (Camada 0)
* **Cordilheiras Tectônicas (`TectonicRidge`):** Picos gerados por colisão de placas (Andes/Himalaia) interceptam ventos úmidos (*chuva orográfica*) e acumulam neve/gelo em altitude (*degelo*), originando as maiores bacias hidrográficas do mundo.
* **Vales de Fenda (`RiftMask`):** Depressões lineares divergentes canalizam rios principais por centenas de hexágonos até o oceano.

---

## 2. Arquitetura: Rios no Shader Passando por Cima dos Hexes + "Aviso" ao Hexágono

**Sim, separar Lógica (Hexes) e Visual (Shader/Spline) é a arquitetura ideal:**
1. **Camada Lógica (`Mapmaker.Core`):** Simula chuva, degelo, preenchimento de depressões e acúmulo de fluxo no grafo hexagonal. Grava o resultado diretamente em `world.RiverEdges[cell]` (e opcionalmente numa lista de `RiverPath` com pontos de spline), **"avisando" cada hexágono** de que ali existe rio, qual a sua largura e direção.
2. **Camada Geométrica (Meandros + Spline):** Converte a sequência de células/arestas em uma curva suave (**Catmull-Rom / B-Spline**) com meandros que passa organicamente por cima dos hexágonos, quebrando a rigidez de $60^\circ$.
3. **Camada Visual (Godot 4):** Renderiza a água corrente sobre o terreno via `Terrain.gdshader` (textura contínua SDF/Flowmap) ou *Ribbon Mesh* 2D/3D com shader de água animada.

---

## 3. Simulação Hidrológica (C# — `WorldGenerator.Rivers.cs`)

### Etapa 1: Escoamento Inicial (Chuva + Degelo)
```csharp
float[] runoff = new float[world.Count];

for (int i = 0; i < world.Count; i++)
{
    if (world.Water[i] != WaterType.None)
        continue;

    float rainMm = world.Rainfall[i];          // mm/ano (típico: 0 .. 2500+)
    float tempC = world.Temperature[i] * 0.1f; // °C reais
    short elevM = world.Elevation[i];          // metros

    // Normaliza chuva para fator base [0 .. ~2.5]
    float rainFactor = rainMm / 1000f;

    if (tempC <= -4.0f)
    {
        // Geleiras/neve perene: retenção glacial com degelo basal lento e constante
        runoff[i] = rainFactor * 0.30f + (elevM > 2200 ? 0.15f : 0f);
    }
    else if (tempC <= 5.0f && elevM >= 1500)
    {
        // Faixa ativa de degelo nival/alpino: neve acumulada derretendo + chuva orográfica
        runoff[i] = rainFactor * 1.65f + 0.25f;
    }
    else
    {
        // Chuva direta com penalidade de evaporação em desertos quentes
        float evapFactor = tempC > 26.0f && rainMm < 350f ? 0.35f : 1.0f;
        runoff[i] = rainFactor * evapFactor;
    }
}
```

### Etapa 2: Resolução de Depressões (*Priority-Flood* — Obrigatório!)
> **Por que é crítico?** Sem preencher mínimos locais, a maioria dos rios para no meio do continente em pequenas covas do relevo e nunca chega ao mar.

O algoritmo **Priority-Flood** (Barnes et al., versão otimizada do `resolveDepressions` do Azgaar) garante um caminho monotônico decrescente até o oceano ou lago:

```csharp
float[] filledElev = new float[world.Count];
bool[] closed = new bool[world.Count];
var pq = new PriorityQueue<int, float>();

// 1. Semeia todos os hexágonos de Oceano (e bordas abertas) na fila
for (int i = 0; i < world.Count; i++)
{
    if (world.Water[i] == WaterType.Ocean)
    {
        filledElev[i] = -1f;
        closed[i] = true;
        pq.Enqueue(i, -1f);
    }
}

const float Epsilon = 0.01f; // Pequeno gradiente para garantir escoamento em planícies/lagos

// 2. Expande do mar para o interior, elevando virtualmente poços presos
while (pq.TryDequeue(out int cell, out float elev))
{
    Hex hx = HexAt(cell);
    for (int dir = 0; dir < 6; dir++)
    {
        int n = Neighbor(hx, dir);
        if (n < 0 || closed[n]) continue;

        closed[n] = true;
        float realElev = world.Water[n] == WaterType.None ? world.Elevation[n] : MathF.Max(0f, world.Elevation[n]);
        // Se o vizinho está mais baixo que a borda da bacia, preenche virtualmente + epsilon
        float nextElev = MathF.Max(realElev, elev + Epsilon);
        filledElev[n] = nextElev;
        pq.Enqueue(n, nextElev);
    }
}
```
* **Lagos e Bacias Endorreicas:** Se um lago (`WaterType.Lake`) estiver em região árida onde a evaporação total supera o fluxo de entrada, ele permanece fechado (lago salgado / mar morto); caso contrário, o ponto mais baixo de sua margem (`filledElev`) torna-se automaticamente seu **sangradouro (*outlet*)**.

### Etapa 3: Acúmulo de Fluxo Declive Abaixo (*Downhill Flow Accumulation*)
```csharp
// Ordena células terrestres da maior para a menor altitude preenchida
var landCells = new List<int>(world.Count / 2);
for (int i = 0; i < world.Count; i++)
    if (world.Water[i] == WaterType.None) landCells.Add(i);

landCells.Sort((a, b) => filledElev[b].CompareTo(filledElev[a]));

float[] flux = new float[world.Count];
Array.Copy(runoff, flux, world.Count);

int[] flowTo = new int[world.Count];
Array.Fill(flowTo, -1);

foreach (int cell in landCells)
{
    Hex hx = HexAt(cell);
    int bestNeighbor = -1;
    float bestElev = filledElev[cell];

    for (int dir = 0; dir < 6; dir++)
    {
        int n = Neighbor(hx, dir);
        if (n < 0) continue;

        if (filledElev[n] < bestElev)
        {
            bestElev = filledElev[n];
            bestNeighbor = n;
        }
    }

    if (bestNeighbor >= 0)
    {
        flowTo[cell] = bestNeighbor;
        flux[bestNeighbor] += flux[cell];
    }
}
```

### Etapa 4: "Avisando o Hexágono" — Layout de 16 Bits em `world.RiverEdges`
Sem alterar o formato binário de `WorldData.cs` (`ushort[] RiverEdges`), codificamos **direção + largura + flags** em 16 bits por célula:

* **Bits `0..11` (2 bits por direção `0..5`):** Porte do rio cruzando aquela aresta:
  * `00` (0) = sem rio
  * `01` (1) = riacho (*Stream/Creek*)
  * `10` (2) = rio (*River*)
  * `11` (3) = grande rio navegável / estuário (*Major River*)
* **Bits `12..15` (4 flags):**
  * Bit 12 (`0x1000`): Nascente (*Headwater*)
  * Bit 13 (`0x2000`): Confluência (*Confluence*)
  * Bit 14 (`0x4000`): Foz / Estuário (*Estuary*)
  * Bit 15 (`0x8000`): Navegável (*Navigable*)

```csharp
static byte FluxToTier(float f) => f >= 140f ? (byte)3 : f >= 45f ? (byte)2 : f >= 14f ? (byte)1 : (byte)0;

static void SetEdgeTier(ushort[] riverEdges, int cell, int dir, byte tier)
{
    int shift = dir * 2;
    byte current = (byte)((riverEdges[cell] >> shift) & 0b11);
    if (tier <= current) return;
    riverEdges[cell] = (ushort)((riverEdges[cell] & ~(0b11 << shift)) | (tier << shift));
}
```

### Etapa 5: Erosão Suave (*Downcutting*) e Umidade Fluvial
* **Erosão:** Opcionalmente reduza `world.Elevation[cell]` em `20..120 m` proporcionalmente ao `tier`, esculpindo vales fluviais naturais.
* **Umidade / Oásis:** Células de deserto/estepe com `world.RiverEdges[cell] != 0` recebem bônus efetivo de umidade para gerar vales férteis (efeito Rio Nilo).

---

## 4. Geração de Meandros e Renderização no Godot 4

### Camada Geométrica (Spline Contínua)
1. Extraia as cadeias de células de cada bacia (tronco principal = ramo com maior `flux` em cada confluência; demais ramos = afluentes).
2. Converta os centros/arestas para coordenadas de mundo (`map_position`).
3. Aplique deslocamento lateral perpendicular (**Meandros**), mais suave em montanhas e mais acentuado em planícies (`ReliefType.Flat`).
4. Interpole com **Catmull-Rom** ou **B-Spline**, gerando largura contínua proporcional a $\sqrt{\text{flux}}$.

### Camada Visual: Duas Opções para o `Mapmaker`

#### Opção A: Textura de Fluxo/SDF no `Terrain.gdshader` (Integrada ao terreno)
O C# rasteriza as splines em uma `ImageTexture` (`river_data`, resolução 2× ou 4× a grade de hexes):
* **R:** Máscara/SDF da calha (`0.0` fora, `1.0` no centro do rio).
* **G, B:** Direção normalizada do fluxo `(dir.x * 0.5 + 0.5, dir.y * 0.5 + 0.5)`.
* **A:** Largura/profundidade relativa.

No `Terrain.gdshader` (respeitando `map_position`, `map_size` e `horizontal_wrap` já existentes no shader):
```glsl
uniform sampler2D river_data : filter_linear, repeat_disable;
uniform float river_time_speed = 0.35;

// Dentro do fragment(), usando map_position convertido para [0..1] do mapa:
vec2 world_uv = map_position / vec2(float(map_size.x) * ROOT3, float(map_size.y) * 1.5);
if (horizontal_wrap) {
    world_uv.x = fract(world_uv.x);
}

vec4 rdata = texture(river_data, world_uv);
float river_mask = rdata.r;
if (river_mask > 0.08) {
    vec2 flow = (rdata.gb - 0.5) * 2.0;
    float shimmer = sin(dot(map_position * 0.35 - flow * TIME * river_time_speed * 10.0, vec2(1.0, 0.7))) * 0.5 + 0.5;
    vec3 river_col = mix(SHALLOW * 0.85, SHALLOW * 1.18, shimmer * 0.35);
    float bank_blend = smoothstep(0.08, 0.32, river_mask);
    color = mix(color, river_col, bank_blend);
}
```
* **Prós:** Um único draw call, zero problemas de Z-fighting, respeita exatamente a máscara de terra/mar do shader.

#### Opção B: *Ribbon Mesh* 2D por cima dos Hexágonos (Melhor nitidez em zoom alto)
* Gera um `MeshInstance2D` (`ArrayMesh` com `PrimitiveType.Triangles`) construindo uma fita (*quad strip*) ao longo de cada spline de rio.
* Largura da fita cresce suavemente com $\sqrt{\text{flux}}$; `UV.x` vai de `0..1` entre as margens e `UV.y` cresce ao longo do rio (facilitando animação de correnteza e espuma nas bordas).
* **Prós:** Nitidez vetorial infinita mesmo no zoom máximo, sem depender de resolução de textura em mapas 4096×2048.

> **Recomendação para o `Mapmaker`:** Como o projeto suporta mapas até `4096×2048` com zoom detalhado (`detail_zoom`), a **Opção B (*Ribbon Mesh* por chunk/região com shader de água)** oferece a melhor nitidez visual, enquanto `world.RiverEdges` garante que toda a lógica do jogo e os hexágonos saibam exatamente onde o rio passa.

---

## 5. Critérios de Aceite

- [ ] Unidades reais respeitadas (`Temperature * 0.1f` em °C, `Rainfall` em mm/ano, `Elevation` em metros).
- [ ] *Priority-Flood* implementado: rios não morrem presos em pequenas depressões continentais e lagos cheios geram sangradouros (*outlets*).
- [ ] Rios nascem predominantemente em zonas de chuva orográfica e faixas de degelo alpino/montanhoso.
- [ ] `world.RiverEdges` preenchido com porte (2 bits/aresta) e flags, preservando compatibilidade com `UndoManager` e arquivos `.world`.
- [ ] Renderização contínua com meandros e largura progressiva da nascente até a foz, respeitando `HorizontalWrap`.
