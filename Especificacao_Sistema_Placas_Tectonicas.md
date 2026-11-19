# Especificação Técnica: Sistema de Placas Tectônicas em Grade Hexagonal (`Mapmaker.Core`)

> **Destinatário:** Agente de Desenvolvimento / Arquiteto de Software  
> **Contexto:** Projeto `Mapmaker` (`Mapmaker.Core`, `HexGrid`, `WorldGenerator.ContinentalSculpt.cs`, `WorldData.cs`)  
> **Integração no Pipeline:** **Camada 0 (Fundação Geológica)** — alimenta diretamente `Especificacao_Ilhas_Bahias_Peninsulas_Estilo_Azgaar.md` (Camada 1) e `Especificacao_Sistema_Rios_Chuva_Degelo_Shader.md` (Camada 2).

---

## 1. Visão Geral e Objetivo

Este documento especifica um motor determinístico de **Placas Tectônicas em Grade Hexagonal** nativo para o `Mapmaker`. Em vez de gerar continentes e montanhas apenas por ruído ou pinceladas arbitrárias, o mundo é subdividido em placas litosféricas reais que se movem, se empurram, colidem e se afastam — explicando geologicamente onde surgem **cordilheiras**, **arcos de ilhas vulcânicas**, **fossas**, **baías de fenda (rifts)** e **falhas costeiras**.

```
[ Camada 0: Placas Tectônicas ] ──► Define crosta (continental/oceânica), cordilheiras, rifts e arcos vulcânicos
              │
              ▼
[ Camada 1: Ilhas, Baías e Penínsulas ] ──► Esculpe continentes, costas, penínsulas e arquipélagos sobre as placas
              │
              ▼
[ Camada 2: Clima, Degelo e Rios ] ──► Ventos batem nas cordilheiras tectônicas -> chuva/degelo -> rios para o mar
```

---

## 2. Regras do Modelo Tectônico

### 2.1. Quantidade de Placas ($X$) Baseada no Tamanho do Mapa
O número de placas escala suavemente com a área do mapa (`Count = Width * Height`), evitando mapas pequenos superlotados ou mapas gigantes vazios:

```csharp
int area = _width * _height;
// Ex.: 128x96 (~12k hexes) -> ~9 placas; 256x192 (~49k) -> ~14 placas; 512x256 (~131k) -> ~20 placas
int plateCount = Math.Clamp((int)MathF.Round(MathF.Sqrt(area) * 0.06f) + 4, 6, 28);
```
* A proporção recomendada de tipos de crosta é **~40% Continentais** (crosta leve e espessa, elevação base positiva) e **~60% Oceânicas** (crosta densa e fina, elevação base negativa), ajustável conforme `settings.LandPercent` e `settings.Continents`.

---

### 2.2. Subdivisão do Mundo (Tesselaçao de Voronoi Distorcida em `HexGrid`)
Em vez de um Voronoi geométrico de linhas retas (que criaria fronteiras artificiais):
1. Sorteie $X$ sementes bem distribuídas (usando *Poisson / Best-Candidate Sampling* respeitando `_wrap` horizontal).
2. Execute um **Multi-Source Dijkstra / Priority-Queue Flood Fill** a partir das $X$ sementes:
   * O custo de avançar para o vizinho `n` é modulado por ruído contínuo (`FieldNoise`) e pela resistência da placa:
     $$\text{stepCost} = 1.0 + 0.65 \cdot \text{FieldNoise}(col, row, \text{freq}, \text{seed})$$
   * Isso produz fronteiras orgânicas, curvas e irregulares entre placas, sem polígonos retos.
3. Cada hexágono recebe `plateId[cell]` $\in [0, X - 1]$.

---

### 2.3. Rede de Influência entre Placas (3 a 5 Vizinhos Próximos)
Após a tesselação, constrói-se o **Grafo de Adjacência de Placas**:
* Identificam-se todas as placas que compartilham fronteira com a placa $P_i$.
* Cada placa $P_i$ seleciona deterministicamente de **3 a 5 placas próximas** (priorizando vizinhas diretas em fronteira e, se tiver menos de 3, as mais próximas por distância de centroide) como sua **lista de influência** (`InfluencingPlates`).
* Essa rede controla como o empurrão/resistência das placas vizinhas amplifica ou freia a velocidade efetiva da placa $P_i$.

---

### 2.4. Movimento em 6 Direções Hexagonais (Máximo 2 Adjacentes, Nunca Contrárias)

No `HexGrid`, as 6 direções cardeais/axiais (`dir = 0..5`) correspondem a ângulos de $0^\circ, 60^\circ, 120^\circ, 180^\circ, 240^\circ, 300^\circ$.

#### Regras Rígidas de Direção:
1. Cada placa escolhe uma **direção primária** $d_1 \in \{0, 1, 2, 3, 4, 5\}$.
2. Opcionalmente (ex.: 65% de chance), escolhe uma **direção secundária** $d_2$ que **deve ser adjacente** a $d_1$:
   $$d_2 \in \{ (d_1 + 1) \bmod 6,\ (d_1 + 5) \bmod 6 \}$$
3. **Nunca podem ser contrárias** ($|d_1 - d_2| \neq 3$) nem não-adjacentes ($|d_1 - d_2| \neq 2, 4$), garantindo que o vetor resultante seja fisicamente coerente sem cancelamento interno.

```csharp
public enum PlateCrust : byte { Oceanic, Continental }
public enum PlateSpeedTier : byte { Low = 1, Medium = 2, High = 3 }

public sealed class TectonicPlate
{
    public int Id { get; init; }
    public int SeedCell { get; init; }
    public PlateCrust Crust { get; init; }

    // 1 ou 2 direções hexagonais adjacentes (PrimaryDir e opcionalmente SecondaryDir)
    public int PrimaryDir { get; init; }          // 0..5
    public int? SecondaryDir { get; init; }        // null, ou (PrimaryDir + 1) % 6, ou (PrimaryDir + 5) % 6

    // Velocidade base e efetiva (calculada pela influência das 3-5 placas próximas)
    public PlateSpeedTier BaseSpeed { get; init; }
    public PlateSpeedTier EffectiveSpeed { get; set; }
    public Vector2 Velocity { get; set; }          // Vetor 2D normalizado * fator de velocidade

    public List<int> InfluencingPlates { get; } = new();
}
```

#### Cálculo do Vetor e da Velocidade Efetiva (Influência das Placas Vizinhas):
1. Converte-se `PrimaryDir` (e `SecondaryDir`, se existir) para um vetor unitário 2D $\hat{u}_i$:
   $$\hat{u}_i = \text{Normalize}(\vec{e}_{d_1} + (d_2 \text{ existe } ?\ 0.75 \cdot \vec{e}_{d_2} : \vec{0}))$$
2. Para cada placa $P_i$, mede-se o **impulso líquido** vindo das 3 a 5 placas em `InfluencingPlates`:
   * Se uma placa vizinha $P_j$ empurra na mesma direção ($\hat{u}_j \cdot \hat{u}_i > 0.3$), ela **acelera** $P_i$ (empurrão de manto / *ridge push*).
   * Se uma placa vizinha $P_j$ colide frontalmente na direção oposta ($\hat{u}_j \cdot \hat{u}_i < -0.3$), ela gera **resistência/travamento** ou **pressão acumulada**.
3. O resultado ajusta `EffectiveSpeed` entre **Baixa (0.55f)**, **Média (1.00f)** e **Alta (1.55f)**, definindo $\vec{V}_i = \hat{u}_i \times \text{SpeedMagnitude}(\text{EffectiveSpeed})$.

---

## 3. Classificação das Fronteiras e Interações Geológicas

Para cada hexágono $c$ na fronteira entre a placa $A$ (`plateId[c]`) e um hexágono vizinho $n$ da placa $B$ (`plateId[n] != A`):

1. Calcula-se o vetor normal unitário $\hat{N}_{A \to B}$ apontando do centro de $c$ para o centro de $n$.
2. Calcula-se a velocidade relativa:
   $$\vec{V}_{\text{rel}} = \vec{V}_A - \vec{V}_B$$
3. Decompõe-se $\vec{V}_{\text{rel}}$ em duas componentes:
   * **Componente Normal (Convergência / Divergência):**
     $$C = \vec{V}_{\text{rel}} \cdot \hat{N}_{A \to B}$$
     * Se $C > +\tau$: **Convergente (Colisão)**
     * Se $C < -\tau$: **Divergente (Afastamento / Rift)**
   * **Componente Tangencial (Cisalhamento / Transformante):**
     $$S = |\vec{V}_{\text{rel}} - C \cdot \hat{N}_{A \to B}|$$
     * Se $|C| \le \tau$ e $S > \tau_{\text{shear}}$: **Transformante (Falha Lateral)**

### Tabela de Efeitos Geológicos por Par de Placas

| Fronteira | Placa A + Placa B | Fenômeno Geológico | Efeito no `Mapmaker` |
| :--- | :--- | :--- | :--- |
| **Convergente** ($C > 0$) | **Continental + Continental** | Orogênese / Dobramento (*Himalaia*) | Elevação extrema (`TectonicRidge` muito alto), platô interior largo, sem fossa oceânica. |
| **Convergente** ($C > 0$) | **Oceânica + Continental** | Subducção costeira (*Andes*) | Cordilheira vulcânica paralela à costa no lado continental + fossa profunda no lado oceânico. |
| **Convergente** ($C > 0$) | **Oceânica + Oceânica** | Subducção oceânica (*Japão / Antilhas*) | Fossa na placa que afunda + **Arco de Ilhas Vulcânicas** (`VolcanicArcMask`) na placa superior. |
| **Divergente** ($C < 0$) | **Continental + Continental** | *Rift Valley* (*África Oriental / Mar Vermelho*) | Vale profundo central (`RiftMask`) com bordas levemente elevadas; pode abrir mares estreitos/baías longas. |
| **Divergente** ($C < 0$) | **Oceânica + Oceânica** | Dorsal Meso-Oceânica (*Atlântico / Islândia*) | Crista submarina moderada; pontos de velocidade alta geram ilhas vulcânicas isoladas. |
| **Transformante** ($S > 0$) | **Qualquer par** | Falha Lateral (*San Andreas*) | `TransformMask`: colinas escalonadas, falhas costeiras retas, penínsulas alongadas e baías estreitas. |

> **Intensidade pela Velocidade:** Colisões entre duas placas de velocidade **Alta** geram picos massivos (`Peak`) e arcos extensos; colisões **Baixas** geram colinas suaves (`Hills` / `Rolling`) e cadeias antigas erodidas.

---

## 4. Propagação de Estresse para o Interior das Placas

Fronteiras tectônicas não têm apenas 1 hexágono de largura; cordilheiras e rifts se estendem por vários hexágonos para dentro das placas:

1. Inicialize filas BFS a partir de todos os hexágonos de fronteira carregando a intensidade $|C|$ ou $S$.
2. Propague para o interior com decaimento por distância hexagonal $d$:
   * **Himalaia (Cont-Cont):** alcance largo ($R \approx 6 \dots 12$ hexes dependendo da escala), perfil em sino com ruído de crista.
   * **Andes (Ocean-Cont):** alcance médio assimétrico (deslocado 2–4 hexes para dentro do continente), gerando planície costeira estreita + cordilheira abrupta.
   * **Arco de Ilhas (Ocean-Ocean):** faixa estreita deslocada 2–3 hexes para o lado da placa superior, modulada por ruído para formar ilhas separadas (não uma parede contínua).
   * **Rift (Divergente):** faixa estreita (2–5 hexes) escavando o score abaixo do nível do mar quando a divergência é média/alta.

---

## 5. Contrato de Saída (`TectonicField`) para as Próximas Camadas

O módulo `WorldGenerator.Tectonics.cs` deve produzir um objeto imutável `TectonicField` consumido pelas demais camadas:

```csharp
public sealed class TectonicField
{
    public int[] PlateIds { get; init; }          // ID da placa (0..X-1) por hexágono
    public TectonicPlate[] Plates { get; init; }  // Metadados (tipo, direção, velocidade)
    public float[] BaseCrustScore { get; init; }  // Score base: positivo em placas continentais, negativo em oceânicas
    public float[] TectonicRidge { get; init; }   // Intensidade de cordilheiras [0..1] (alimenta Elevation e Rain Shadow)
    public float[] RiftMask { get; init; }        // Intensidade de vales/fendas divergentes [0..1] (baías/mares/vales fluviais)
    public float[] VolcanicArcMask { get; init; } // Intensidade de arcos de ilhas vulcânicas [0..1]
    public float[] TransformMask { get; init; }   // Intensidade de falhas laterais [0..1]
}
```

### Como as outras camadas consomem `TectonicField`:
* **Camada 1 (Ilhas, Baías e Penínsulas):** Usa `BaseCrustScore` como fundação dos continentes, `VolcanicArcMask` para plantar cadeias de ilhas, `RiftMask` para guiar `Trough`/`Strait` e abrir baías/mares internos, e `TectonicRidge` para alinhar `Range` e penínsulas montanhosas.
* **Camada 2 (Clima e Rios):** `TectonicRidge` cria as barreiras orográficas (chuva pesada de um lado, deserto do outro) e zonas de neve/degelo; `RiftMask` canaliza grandes bacias hidrográficas rumo ao mar.

---

## 6. Critérios de Aceite

- [ ] A quantidade de placas escala automaticamente com o tamanho do mapa (`Width * Height`).
- [ ] Cada placa respeita rigorosamente a regra de movimento: 1 ou 2 direções hexagonais adjacentes, nunca contrárias.
- [ ] A velocidade efetiva (`Low`, `Medium`, `High`) é influenciada por 3 a 5 placas próximas.
- [ ] `HorizontalWrap == true` mantém continuidade perfeita das placas e fronteiras através da costura leste-oeste.
- [ ] Geração 100% determinística a partir de `WorldManifest.Seed`.
- [ ] Opcionalmente, um modo de visualização/camada de debug permite inspecionar placas, vetores de velocidade e tipos de fronteira.
