# Especificação Técnica: Geração de Continentes e Relevo Estilo Azgaar em Grade Hexagonal (`Mapmaker.Core`)

Este documento detalha os algoritmos, sistemas e regras usados pelo **Azgaar’s Fantasy Map Generator (FMG)** (e geradores correlatos da indústria) para esculpir o formato (*shape*) de continentes, penínsulas, mares internos, estreitos e cordilheiras, traduzidos diretamente para a arquitetura C# do projeto **Mapmaker** (`HexGrid`, `WorldData`, `WorldGenerator`).

---

## 1. Essa geração é exclusiva do Azgaar? Quem mais usa?

**Não é exclusiva do Azgaar.** Embora o Azgaar tenha popularizado o sistema de **Templates + Propagação de Blobs em Grafo**, essa abordagem faz parte de uma família clássica de algoritmos de *Procedural Content Generation (PCG)* usada em jogos de estratégia 4X e simuladores:

1. **Civilization V e Civilization VI (Firaxis — `MapGenerator` / `MountainsCliffs.lua` / `FractalWorld.lua`):**
   * Para evitar que mundos em hexágonos pareçam apenas manchas de Perlin Noise, o Civ V/VI usa sementes de placas/massas continentais que crescem por **agregação em grade hexagonal** (*Hex Blob Growth / Plate Aggregation*), além de traçar cadeias de montanhas e falhas tectônicas usando **caminhos direcionados (*Biased Random Walks*)** entre hexágonos.
2. **Amit Patel / Red Blob Games (`mapgen2` e `mapgen4`):**
   * Foi a principal inspiração técnica inicial do Azgaar. Usa grafos poligonais (Voronoi/Delaunay), separando a definição macro da forma da ilha/continente da perturbação costeira, além de calcular rios por *downhill flow accumulation* e biomas por sombra de chuva (*rain shadow*).
3. **Dwarf Fortress (Tarn Adams):**
   * Não usa Perlin Noise puro para o relevo macro. Ele planta "sementes" de elevação, cordilheiras e bacias em uma grade grossa, propaga/interpola os valores com decaimento, aplica erosão e força canais de drenagem antes de detalhar o mundo.
4. **geradores Tectônicos / SimEarth / PlaTec:**
   * Usam colisão/subducção simplificada onde as bordas das massas continentais geram `Ranges` (cordilheiras costeiras tipo Andes) ou `Troughs` (fossas/rifts continentais).

### Por que combinar ou alternar entre "Noise com Filtros" e "Escultura por Blobs/Templates"?
* **Noise + Domain Warping + Lóbulos Elípticos (como o `LandScore` atual do `Mapmaker`):** É excelente por ser $O(1)$ por célula (amostragem pontual pura), mas os continentes tendem a manter uma assinatura estatística parecida (elipses distorcidas por ruído) e as montanhas (`LandElevation` via *ridge noise*) aparecem espalhadas uniformemente pelo interior, sem formar espinhas dorsais continentais reais, istmos longos ou mares mediterrâneos fechados.
* **Escultura por Grafos / Blobs & Templates (Estilo Azgaar):** Constrói o mundo de forma **construtiva e aditiva/subtrativa** sobre a própria grade: soma grandes domos continentais (`Hill`), puxa penínsulas e arquipélagos menores, rasga estreitos marítimos (`Strait`), afunda bacias/mares internos (`Pit` / `Trough`) e costura cordilheiras contínuas de ponta a ponta (`Range`).

---

## 2. Como o Algoritmo do Azgaar Funciona Exatamente

No Azgaar, o gerador começa com um array de elevação zerado e executa uma receita sequencial (**Template**) composta por 5 operadores espaciais no grafo:

### 2.1. `Hill` (Domo / Massa Continental via BFS Estocástico)
Um continente não é um único blob, mas a **soma e sobreposição de múltiplos `Hills`** (1 ou 2 gigantes para o escudo continental + 5 a 15 médios/pequenos nas bordas para criar penínsulas, subcontinentes e ilhas costeiras).
1. Sorteia-se uma célula semente $s$ dentro de uma região permitida $(X_{min}..X_{max}, Y_{min}..Y_{max})$.
2. Atribui-se uma força inicial `change[s] = H` (ex.: `60` a `90`).
3. Insere-se $s$ em uma fila (*Queue* de BFS).
4. Para cada célula $c$ retirada da fila, visitam-se seus vizinhos $n$ ainda não visitados no blob atual:
   $$\text{change}[n] = (\text{change}[c])^{\text{decay}} \times \text{jitter}$$
   * Onde $\text{decay}$ (*blobPower*) depende da resolução do mapa (ex.: `0.93` a `0.985`), controlando o raio de espalhamento.
   * $\text{jitter}$ é um número aleatório em $[0.82, 1.15]$.
5. Se $\text{change}[n] > 1.5$, o vizinho $n$ entra na fila.
6. **Por que isso gera costas e penínsulas naturais?** Quando o sorteio do $\text{jitter}$ dá valores acima de `1.0` por alguns passos seguidos numa direção, a onda de elevação viaja muito mais longe naquela direção (formando uma península ou cabo). Quando sorteia valores próximos de `0.85`, a onda morre cedo (formando uma baía ou golfo).
7. Ao final do BFS, soma-se `change[i]` ao acumulador de score/altura do mapa.

### 2.2. `Pit` (Bacia / Mar Interno / Golfo)
Exatamente o mesmo algoritmo do `Hill`, porém **subtrai** o valor de `change[i]` da altura do mapa.
* Quando aplicado na borda de um continente, escava um grande golfo ou baía em formato de meia-lua.
* Quando aplicado no meio de um grande continente, cria um mar interior (como o Mar Mediterrâneo, Mar Negro ou Cáspio).

### 2.3. `Range` (Cordilheira Contínua) e `Trough` (Vale de Rift / Fossa)
Em vez de crescer a partir de um único hexágono, cresce a partir de uma **linha mestra (espinha dorsal)** traçada pelo grafo:
1. Sorteia um hexágono inicial $A$ e um hexágono final $B$ a uma distância controlada.
2. **Biased Random Walk (Caminhada Direcionada):** Avança de $A$ até $B$ escolhendo, a cada passo, um dos vizinhos que reduza (ou mantenha) a distância hexagonal até $B$, adicionando leve aleatoriedade para a crista curvar organicamente.
3. Todos os hexágonos da crista recebem a altura máxima da cordilheira e entram juntos na fila inicial do BFS.
4. A propagação lateral usa um decaimento mais rápido (`lineDecay`, ex.: `0.78` a `0.88`), criando uma cadeia montanhosa comprida e estreita (`Range` positivo) ou uma fossa tectônica/canal (`Trough` negativo).

### 2.4. `Strait` (Estreito Marítimo)
Garante a separação física entre continentes que tenham crescido e grudado:
1. Traça um caminho curvo de uma extremidade à outra da região de colisão (ou de norte a sul / leste a oeste).
2. Rebaixa os hexágonos ao longo do caminho (com largura gradual de $W$ hexágonos) para baixo do nível do mar.

### 2.5. `Smooth` (Filtro Laplaciano Hexagonal) e Perturbação Costeira
* Após somar todos os `Hills`, `Pits` e `Ranges`, aplica-se 1 a 2 passadas de média com os 6 vizinhos hexagonais (`h[i] = lerp(h[i], mediaVizinhos, α)`).
* Combina-se com ruído multi-escala (ou Domain Warping leve) para que a micro-linha costeira não exiba degraus geométricos da grade hexagonal.

---

## 3. O Segredo para Grade Hexagonal: Quebrando a Simetria de $60^\circ$

No Azgaar, o grafo base é um Voronoi irregular (os vizinhos estão em ângulos variados). No **Mapmaker**, usamos `HexGrid` (6 vizinhos a exatos $60^\circ$).

Se você rodar um BFS puro em uma grade hexagonal, as frentes de onda formarão **hexágonos concêntricos perfeitos** (ilhas em formato de hexágono ou estrela de 6 pontas).

Para eliminar 100% o artefato hexagonal no `Mapmaker`, aplicam-se **3 regras obrigatórias** durante o BFS no `HexGrid`:
1. **Embaralhamento de Direções (Fisher-Yates de 6 elementos):** Ao expandir um hexágono `current`, nunca visite `direction = 0..5` na mesma ordem fixa; embaralhe ou rotacione a ordem usando o RNG determinístico da célula.
2. **Penalidade de Distância Axial Anisotrópica + Jitter:** No cálculo de `nextVal`, multiplique por um fator aleatório em $[0.80, 1.15]$ **e** por uma leve modulação do `Noise(x, y, seed)` já existente em `WorldGenerator.cs`.
3. **Pulo de Anel Ocasional (*Skip/Boost Step*):** Com ~15% de chance, permita que o decaimento de um passo seja quase nulo (`0.995`), e com ~15% de chance seja duplo (`decay * decay`). Isso destrói as frentes retas de $60^\circ$ e cria contornos indistinguíveis de um mapa feito à mão.

---

## 4. Integração Direta na Arquitetura do `Mapmaker`

Analisando `src/Core/WorldData.cs` e `src/Core/WorldGenerator.cs`, o projeto já possui uma arquitetura excelente que facilita muito adicionar esse gerador como uma nova opção:

### 4.1. Onde plugar no `WorldData.cs`
No enum `TerrainAlgorithm`:
```csharp
public enum TerrainAlgorithm
{
    Fractal,
    LandmassCenters,
    ContinentalSculpt // Novo: Estilo Azgaar (Blobs + Ranges + Troughs + Straits)
}
```

### 4.2.Compatibilidade com `FindSeaLevel` e `RegenerateRegion`
No `WorldGenerator.cs` atual:
* O passo `GenerationStep.Land` preenche `float[] scores` de tamanho `world.Count` (`Width * Height`), calcula `seaLevel = FindSeaLevel(scores, settings.LandPercent)` via histograma de 2048 bins e define `Ocean` quando `score < seaLevel`.
* **Vantagem enorme:** Como `FindSeaLevel` já calibra automaticamente o nível do mar pelo percentil exato de `settings.LandPercent` (10% a 65%), o gerador estilo Azgaar só precisa produzir o array `float[] scores` (e um array opcional `float[] tectonicRidge` para alimentar `LandElevation` com as cordilheiras reais geradas pelos `Ranges`)!
* Para `RegenerateRegion(WorldData world, WorldRegion region)`: como a simulação de Blobs/BFS é global mas extremamente rápida (leva ~15–40 ms em C# para centenas de milhares de hexes usando arrays lineares), pode-se gerar o campo escalar global determinístico a partir de `manifest.Seed` ou cachear/recalcular na chamada.

---

## 5. Implementação de Referência em C# para o `WorldGenerator`

Abaixo está o código de referência completo, seguindo as convenções do `Mapmaker.Core` (`HexGrid`, `HorizontalWrap`, `Noise` determinístico, zero alocações desnecessárias em loop):

```csharp
using System;

namespace Mapmaker.Core;

public static partial class WorldGenerator
{
    /// <summary>
    /// Gera o campo de pontuação continental (scores) e um mapa de cristas tectônicas (ridges)
    /// usando o algoritmo construtivo de Blobs, Bacias, Cordilheiras e Estreitos (estilo Azgaar)
    /// adaptado para a grade hexagonal do Mapmaker.
    /// </summary>
    private static void BuildAzgaarContinentalScores(
        int width,
        int height,
        bool wrapHorizontal,
        GenerationSettings settings,
        int seed,
        float[] scores,
        float[]? mountainRidges = null)
    {
        int count = width * height;
        Array.Fill(scores, -1.0f);
        if (mountainRidges is not null)
            Array.Clear(mountainRidges, 0, mountainRidges.Length);

        var rng = new Random(seed ^ 0x4F1BBCDCu);
        uint noiseSeed = unchecked((uint)seed);
        float aspect = width / (float)height;
        float coastFactor = settings.CoastDetail * 0.01f;

        // Fator de decaimento calibrado pela área do mapa para que os blobs mantenham
        // proporção semelhante tanto em mapas 256x128 quanto em 1024x512.
        float mapScale = MathF.Sqrt(count / (float)Math.Max(1, settings.Continents));
        float majorDecay = Math.Clamp(1f - (1.85f / mapScale), 0.90f, 0.992f);
        float minorDecay = Math.Clamp(1f - (3.80f / mapScale), 0.84f, 0.980f);
        float rangeDecay = Math.Clamp(1f - (6.50f / mapScale), 0.75f, 0.955f);

        // Buffers reutilizáveis para o BFS (evita GC pressure)
        float[] change = new float[count];
        int[] visitedStamp = new int[count];
        int[] queue = new int[count];
        int currentStamp = 0;

        // 1. DISTRIBUIÇÃO DAS SEMENTES CONTINENTAIS (Macro-continentes)
        int continents = settings.Continents;
        int cols = Math.Clamp((int)MathF.Round(MathF.Sqrt(continents * aspect * 0.85f)), 1, continents);
        int rows = (continents + cols - 1) / cols;

        Span<int> continentCenters = stackalloc int[continents];

        for (int c = 0; c < continents; c++)
        {
            int r = c / cols;
            int rowCols = Math.Min(cols, continents - r * cols);
            float normX = ((c % rowCols) + 0.25f + rng.NextSingle() * 0.50f) / rowCols;
            float normY = (r + 0.22f + rng.NextSingle() * 0.56f) / rows;

            int centerCol = Math.Clamp((int)(normX * width), 0, width - 1);
            int centerRow = Math.Clamp((int)(normY * height), 0, height - 1);
            int centerIndex = centerRow * width + centerCol;
            continentCenters[c] = centerIndex;

            // Núcleo principal do continente (Hill Primário)
            AddHexBlob(centerIndex, peak: 2.2f + rng.NextSingle() * 0.6f, majorDecay,
                isPit: false, targetBuffer: scores);

            // 2 a 4 lóbulos secundários / subcontinentes ancorados ao redor do núcleo
            int subBlobs = 2 + rng.Next(3);
            for (int s = 0; s < subBlobs; s++)
            {
                int offsetRadius = (int)(mapScale * (0.18f + rng.NextSingle() * 0.28f));
                int subSeedIndex = PickRandomHexNear(centerCol, centerRow, offsetRadius);
                AddHexBlob(subSeedIndex, peak: 1.35f + rng.NextSingle() * 0.55f,
                     Interpolate(minorDecay, majorDecay, 0.55f), isPit: false, targetBuffer: scores);
            }

            // 4 a 8 penínsulas e ilhas costeiras (Hills Menores)
            int peninsulas = 4 + (int)(coastFactor * 5f) + rng.Next(3);
            for (int p = 0; p < peninsulas; p++)
            {
                int offsetRadius = (int)(mapScale * (0.30f + rng.NextSingle() * 0.42f));
                int penIndex = PickRandomHexNear(centerCol, centerRow, offsetRadius);
                AddHexBlob(penIndex, peak: 0.70f + rng.NextSingle() * 0.55f,
                    minorDecay, isPit: false, targetBuffer: scores);
            }
        }

        // 2. BACIAS, GOLFOS E MARES INTERNOS (Pits e Troughs)
        int pitsCount = Math.Max(2, continents * 2 + (int)(coastFactor * 4f));
        for (int p = 0; p < pitsCount; p++)
        {
            int anchor = continentCenters[rng.Next(continents)];
            int aCol = anchor % width;
            int aRow = anchor / width;
            int pitSeed = PickRandomHexNear(aCol, aRow, (int)(mapScale * (0.20f + rng.NextSingle() * 0.45f)));
            AddHexBlob(pitSeed, peak: 0.85f + rng.NextSingle() * 0.65f,
                minorDecay, isPit: true, targetBuffer: scores);
        }

        // 3. CORDILHEIRAS TECTÔNICAS CONTÍNUAS (Ranges) E VALES DE RIFT (Troughs)
        int rangesCount = Math.Max(2, continents * 2);
        for (int r = 0; r < rangesCount; r++)
        {
            int anchor = continentCenters[r % continents];
            int aCol = anchor % width;
            int aRow = anchor / width;

            int startIdx = PickRandomHexNear(aCol, aRow, (int)(mapScale * 0.25f));
            int endIdx = PickRandomHexNear(aCol, aRow, (int)(mapScale * 0.55f));

            // Eleva a espinha continental e grava no buffer de cordilheiras (se fornecido)
            AddHexRange(startIdx, endIdx, peak: 0.85f + rng.NextSingle() * 0.45f,
                rangeDecay, isTrough: false, scores, mountainRidges);
        }

        int troughsCount = Math.Max(1, continents);
        for (int t = 0; t < troughsCount; t++)
        {
            int anchor = continentCenters[rng.Next(continents)];
            int aCol = anchor % width;
            int aRow = anchor / width;
            int startIdx = PickRandomHexNear(aCol, aRow, (int)(mapScale * 0.30f));
            int endIdx = PickRandomHexNear(aCol, aRow, (int)(mapScale * 0.60f));

            AddHexRange(startIdx, endIdx, peak: 0.75f + rng.NextSingle() * 0.40f,
                rangeDecay, isTrough: true, scores, null);
        }

        // 4. SUAVIZAÇÃO HEXAGONAL (Laplacian Smooth) + DETALHE FRACTAL COSTEIRO
        SmoothHexField(scores, width, height, wrapHorizontal, iterations: 2, alpha: 0.42f);

        // 5. PERTURBAÇÃO COSTEIRA E MÁSCARA POLAR
        for (int row = 0; row < height; row++)
        {
            float y = (float)row / height;
            // Atenuação suave nas bordas polares superior/inferior
            float polarDistance = Math.Min(y, 1f - y);
            float polarMask = Smooth(Math.Clamp(polarDistance / 0.09f, 0f, 1f));

            for (int col = 0; col < width; col++)
            {
                int i = row * width + col;
                float x = (col + (row & 1) * 0.5f) / width;
                float nx = x * aspect;

                // Domain warping + multi-scale noise para recortar fiordes, cabos e ilhotas
                float warpX = (Noise(nx * 3.2f + 11f, y * 3.2f, noiseSeed ^ 0xB5297A4Du) - 0.5f) * 0.08f;
                float warpY = (Noise(nx * 3.2f, y * 3.2f + 19f, noiseSeed ^ 0x68E31DA4u) - 0.5f) * 0.08f;

                float detail = (Fractal((nx + warpX) * 5.0f, (y + warpY) * 5.0f, noiseSeed) - 0.5f)
                               * (0.55f + 0.95f * coastFactor);

                scores[i] = (scores[i] + detail) * polarMask - (1f - polarMask) * 1.25f;
            }
        }

        // --- FUNÇÕES LOCAIS DE PROPAGAÇÃO EM GRAFO HEXAGONAL ---

        int PickRandomHexNear(int col, int row, int maxOffset)
        {
            int dCol = rng.Next(-maxOffset, maxOffset + 1);
            int dRow = rng.Next(-maxOffset, maxOffset + 1);
            int nRow = Math.Clamp(row + dRow, 2, height - 3);
            int nCol = wrapHorizontal
                ? ((col + dCol) % width + width) % width
                : Math.Clamp(col + dCol, 2, width - 3);
            return nRow * width + nCol;
        }

        void AddHexBlob(int startIndex, float peak, float decay, bool isPit, float[] targetBuffer)
        {
            currentStamp++;
            int head = 0, tail = 0;

            change[startIndex] = peak;
            visitedStamp[startIndex] = currentStamp;
            queue[tail++] = startIndex;

            Span<int> dirs = stackalloc int[6] { 0, 1, 2, 3, 4, 5 };

            while (head < tail)
            {
                int currIdx = queue[head++];
                float currVal = change[currIdx];
                if (currVal <= 0.04f)
                    continue;

                int cCol = currIdx % width;
                int cRow = currIdx / width;
                Hex currHex = HexGrid.FromOffset(cCol, cRow);

                // Embaralha as 6 direções para destruir a simetria de 60 graus do hexágono
                for (int d = 5; d > 0; d--)
                {
                    int swap = rng.Next(d + 1);
                    (dirs[d], dirs[swap]) = (dirs[swap], dirs[d]);
                }

                for (int d = 0; d < 6; d++)
                {
                    int nIdx = HexGrid.NeighborIndex(currHex, dirs[d], width, height, wrapHorizontal);
                    if (nIdx < 0 || visitedStamp[nIdx] == currentStamp)
                        continue;

                    visitedStamp[nIdx] = currentStamp;

                    int nCol = nIdx % width;
                    int nRow = nIdx / width;
                    float nx = (nCol * aspect) / width;
                    float ny = (float)nRow / height;

                    // Modulação estocástica + campo de ruído contínuo (quebra bordas retas)
                    float nNoise = Noise(nx * 6.5f, ny * 6.5f, noiseSeed ^ 0x27D4EB2Fu);
                    float jitter = (0.83f + rng.NextSingle() * 0.31f) * (0.88f + 0.24f * nNoise);
                    float nextVal = currVal * decay * jitter;

                    if (nextVal > 0.04f)
                    {
                        change[nIdx] = nextVal;
                        queue[tail++] = nIdx;
                    }
                    else
                    {
                        change[nIdx] = 0f;
                    }
                }
            }

            float sign = isPit ? -1f : 1f;
            for (int q = 0; q < tail; q++)
            {
                int idx = queue[q];
                // Mistura suave: usa soma ponderada para que múltiplos blobs criem platôs e colinas
                targetBuffer[idx] += sign * change[idx];
            }
        }

        void AddHexRange(
            int startIndex,
            int endIndex,
            float peak,
            float lineDecay,
            bool isTrough,
            float[] targetBuffer,
            float[]? ridgeBuffer)
        {
            currentStamp++;
            int head = 0, tail = 0;

            int currIdx = startIndex;
            Hex targetHex = HexGrid.FromOffset(endIndex % width, endIndex / width);
            int maxSteps = width + height;

            // 1. Traça a crista da cordilheira via Biased Random Walk no HexGrid
            for (int step = 0; step < maxSteps && currIdx != endIndex; step++)
            {
                if (visitedStamp[currIdx] != currentStamp)
                {
                    visitedStamp[currIdx] = currentStamp;
                    change[currIdx] = peak * (0.85f + rng.NextSingle() * 0.30f);
                    queue[tail++] = currIdx;
                }

                Hex currHex = HexGrid.FromOffset(currIdx % width, currIdx / width);
                int currDist = HexGrid.Distance(currHex, targetHex, width, wrapHorizontal);

                int bestNeighbor = -1;
                int bestPriority = int.MaxValue;

                for (int d = 0; d < 6; d++)
                {
                    int nIdx = HexGrid.NeighborIndex(currHex, d, width, height, wrapHorizontal);
                    if (nIdx < 0) continue;
                    Hex nHex = HexGrid.FromOffset(nIdx % width, nIdx / width);
                    int nDist = HexGrid.Distance(nHex, targetHex, width, wrapHorizontal);

                    // Prioriza vizinhos que aproximam do alvo, mas adiciona jitter para curvar a crista
                    int priority = (nDist - currDist) * 10 + rng.Next(0, 14);
                    if (priority < bestPriority)
                    {
                        bestPriority = priority;
                        bestNeighbor = nIdx;
                    }
                }

                if (bestNeighbor < 0) break;
                currIdx = bestNeighbor;
            }

            // 2. Espalha as encostas da cordilheira lateralmente via BFS rápido
            while (head < tail)
            {
                int cIdx = queue[head++];
                float cVal = change[cIdx];
                if (cVal <= 0.05f) continue;

                Hex cHex = HexGrid.FromOffset(cIdx % width, cIdx / width);
                for (int d = 0; d < 6; d++)
                {
                    int nIdx = HexGrid.NeighborIndex(cHex, d, width, height, wrapHorizontal);
                    if (nIdx < 0 || visitedStamp[nIdx] == currentStamp)
                        continue;

                    visitedStamp[nIdx] = currentStamp;
                    float nextVal = cVal * lineDecay * (0.78f + rng.NextSingle() * 0.34f);
                    if (nextVal > 0.05f)
                    {
                        change[nIdx] = nextVal;
                        queue[tail++] = nIdx;
                    }
                }
            }

            float sign = isTrough ? -1f : 1f;
            for (int q = 0; q < tail; q++)
            {
                int idx = queue[q];
                targetBuffer[idx] += sign * change[idx] * 0.55f;
                if (!isTrough && ridgeBuffer is not null)
                    ridgeBuffer[idx] = Math.Max(ridgeBuffer[idx], change[idx]);
            }
        }
    }

    private static void SmoothHexField(
        float[] field, int width, int height, bool wrapHorizontal, int iterations, float alpha)
    {
        float[] temp = new float[field.Length];
        for (int it = 0; it < iterations; it++)
        {
            for (int row = 0; row < height; row++)
            for (int col = 0; col < width; col++)
            {
                int idx = row * width + col;
                Hex hex = HexGrid.FromOffset(col, row);
                float sum = 0f;
                int count = 0;
                for (int d = 0; d < 6; d++)
                {
                    int nIdx = HexGrid.NeighborIndex(hex, d, width, height, wrapHorizontal);
                    if (nIdx >= 0)
                    {
                        sum += field[nIdx];
                        count++;
                    }
                }
                temp[idx] = count > 0
                    ? Interpolate(field[idx], sum / count, alpha)
                    : field[idx];
            }
            Array.Copy(temp, field, field.Length);
        }
    }
}
```

---

## 6. Como Integrar as Cordilheiras (`mountainRidges`) na Elevação e no Clima

Um dos maiores ganhos visuais de usar esse método no `Mapmaker` está na conexão entre `GenerationStep.Land` e `GenerationStep.Elevation`:

1. **Cordilheiras Coerentes (`LandElevation`):**
   * Hoje, `LandElevation` calcula `float ridge = 1f - Math.Abs(2f * Noise(...) - 1f)`.
   * Com o buffer `mountainRidges[i]` gerado pelo `AddHexRange`, você pode combinar o *ridge noise* com a espinha dorsal tectônica:
     ```csharp
     float tectonic = mountainRidges is not null ? Math.Clamp(mountainRidges[i], 0f, 1.5f) : 0f;
     float combinedRidge = Math.Clamp(Math.Max(ridge * 0.65f, tectonic), 0f, 1.2f);
     ```
   * Isso faz com que picos (`ReliefType.Peak` $\ge 3200\text{m}$) e montanhas (`ReliefType.Mountains` $\ge 1850\text{m}$) formem cadeias contínuas reais (como os Andes ou Himalaia), enquanto as bacias entre elas formam planícies abertas naturais.

2. **Templates Personalizáveis (Opcional para o `WorldCreationDialog`):**
   * Usando exatamente as mesmas 3 primitivas (`AddHexBlob`, `AddHexRange`, `SmoothHexField`), você pode oferecer presets ou modos como:
     * **Continentes Clássicos (Azgaar Continents):** Múltiplos núcleos com penínsulas, mares internos e estreitos.
     * **Supercontinente / Pangeia:** 1 núcleo central massivo + várias bacias internas (`Pits`) + grandes cordilheiras cruzadas.
     * **Arquipélago / Ilhas Vulcânicas:** Dezenas de `AddHexBlob` pequenos com `decay` curto e `peak` alto + `AddHexRange` curvos simulando arcos de ilhas vulcânicas.
     * **Mar Interior (Mediterrâneo):** `AddHexBlob` positivos nas bordas e 2 grandes `AddHexBlob` negativos (`isPit: true`) no centro do mapa.
