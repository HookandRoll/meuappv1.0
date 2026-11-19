# Especificação Técnica: Escultura Construtiva de Ilhas, Baías e Penínsulas (Estilo Azgaar + Tectônica)

> **Destinatário:** Agente de Desenvolvimento / Arquiteto de Software  
> **Contexto:** Projeto `Mapmaker` (`Mapmaker.Core`, `HexGrid`, `WorldGenerator.ContinentalSculpt.cs`, `WorldGenerator.Islands.cs`)  
> **Integração no Pipeline:** **Camada 1 (Morfologia Costeira e Massas de Terra)** — consome `TectonicField` de `Especificacao_Sistema_Placas_Tectonicas.md` (Camada 0) e entrega relevo/costas prontos para `Especificacao_Sistema_Rios_Chuva_Degelo_Shader.md` (Camada 2).  
> **Referência Técnica:** Operadores e naturalização costeira do *Azgaar's Fantasy Map Generator* (`heightmap-generator.ts`, `heightmap-templates.ts`, `coastline-generator.ts`).

---

## 1. Visão Geral e Filosofia

O objetivo desta camada é esculpir **continentes, ilhas, arquipélagos, penínsulas e baías/golfos** usando a filosofia de **escultura construtiva em grafo** do Azgaar, agora **guiada pela Camada 0 (Placas Tectônicas)** quando disponível (ou operando em modo independente caso a tectônica esteja desligada).

### O Princípio Fundamental
A geografia do mapa é moldada sobre um acumulador de scores (`float[] _scores`) e um acumulador de cristas (`float[]? _ridges`) na grade hexagonal através de **5 operadores espaciais no grafo**:
1. **`AddHill`:** Domos aditivos via BFS estocástico (núcleos continentais, penínsulas orgânicas e ilhas).
2. **`AddRange`:** Cristas lineares via *Biased Random Walk* (cordilheiras e penínsulas montanhosas alongadas).
3. **`AddPit`:** Bacias subtrativas via BFS (baías arredondadas, golfos em ferradura e mares internos).
4. **`AddTrough`:** Valas/trincheiras subtrativas lineares (fiordes, rifts costeiros e canais estreitos).
5. **`AddStrait`:** Cortes marítimos de ponta a ponta (estreitos navegáveis que separam continentes e isolam ilhas costeiras).

A linha de costa é calibrada automaticamente por `FindSeaLevel` a partir do percentil exato de `settings.LandPercent`.

---

## 2. Integração Direta com `TectonicField` (Camada 0)

Quando `TectonicField` estiver ativo, ele substitui posicionamentos puramente aleatórios por **âncoras geológicas reais**:

| Dado do `TectonicField` | Como guia a Escultura (Camada 1) |
| :--- | :--- |
| `BaseCrustScore` | Inicializa `_scores` (crosta continental alta, oceânica baixa) antes dos `AddHill` de refinamento. |
| `TectonicRidge` | Soma diretamente em `_ridges` e guia `AddRange` para formar espinhas dorsais e penínsulas montanhosas. |
| `VolcanicArcMask` | Define os cinturões onde `AddHill` pequenos (com espaçamento marítimo) criam **arcos de ilhas vulcânicas** (estilo Japão/Caribe). |
| `RiftMask` | Guia `AddTrough` e `AddStrait` para abrir **baías compridas, mares estreitos e vales de fenda**. |
| `TransformMask` | Orienta `AddTrough` rasos e penínsulas retilíneas ao longo de falhas costeiras. |

---

## 3. Operadores Espaciais no Grafo Hexagonal (Otimizados para `Mapmaker.Core`)

> **Nota de Performance:** Para suportar mapas grandes (`WorldData.MaxWidth = 4096`, `MaxHeight = 2048`), evite `Dictionary<int, float>` e `HashSet<int>` dentro dos loops de BFS. Utilize buffers reutilizáveis com **stamp de visitação** (`int[] visitedStamp`, `float[] changeBuffer`, `int currentStamp++`), zerando a alocação de GC por operador.

### 3.1. Operador `AddHill` (Domo Continental / Ilha / Península)
Propaga elevação por BFS estocástico com embaralhamento das 6 direções hexagonais para eliminar simetria de $60^\circ$.

```csharp
public void AddHill(int startCell, float height, float blobPower, float jitterMin = 0.88f, float jitterMax = 1.12f)
{
    int stamp = ++_visitStamp;
    _visited[startCell] = stamp;
    _change[startCell] = height;
    _queue.Clear();
    _touched.Clear();

    _queue.Enqueue(startCell);
    _touched.Add(startCell);

    Span<int> dirs = stackalloc int[6] { 0, 1, 2, 3, 4, 5 };

    while (_queue.TryDequeue(out int current))
    {
        float currentVal = _change[current];
        Hex currentHex = HexAt(current);

        // Obrigatório: embaralha 0..5 (Fisher-Yates) para quebrar frentes retas de 60°
        Shuffle(dirs);

        for (int i = 0; i < 6; i++)
        {
            int neighbor = Neighbor(currentHex, dirs[i]);
            if (neighbor < 0 || _visited[neighbor] == stamp)
                continue;

            // Decaimento exponencial estilo Azgaar + leve modulação de ruído espacial
            float jitter = jitterMin + _random.NextSingle() * (jitterMax - jitterMin);
            float nextVal = MathF.Pow(currentVal, blobPower) * jitter;

            // Pulo/boost ocasional (~12%) para quebrar anéis concêntricos perfeitos
            float roll = _random.NextSingle();
            if (roll < 0.12f) nextVal = MathF.Pow(currentVal, MathF.Min(0.995f, blobPower + 0.02f)) * jitter;
            else if (roll > 0.88f) nextVal = MathF.Pow(currentVal, blobPower * blobPower) * jitter;

            if (nextVal > 0.05f)
            {
                _visited[neighbor] = stamp;
                _change[neighbor] = nextVal;
                _queue.Enqueue(neighbor);
                _touched.Add(neighbor);
            }
        }
    }

    foreach (int cell in _touched)
        _scores[cell] += _change[cell];
}
```

* **Calibração de `blobPower` (escalada por resolução do mapa):**
  * Continentes grandes: `0.985` a `0.995`.
  * Penínsulas / ilhas médias: `0.94` a `0.97`.
  * Ilhotas / arcos vulcânicos: `0.88` a `0.93`.

---

### 3.2. Operador `AddRange` (Cristas e Penínsulas Montanhosas)
Traça uma espinha dorsal de $A$ até $B$ usando *Biased Random Walk* com **distância compatível com `HorizontalWrap`** e propaga lateralmente com decaimento `linePower`:

```csharp
public void AddRange(int startCell, int endCell, float height, float linePower, float randomness = 0.20f, bool writeToRidges = true)
{
    int stamp = ++_visitStamp;
    _spine.Clear();
    _spine.Add(startCell);
    _visited[startCell] = stamp;

    int current = startCell;
    int maxSteps = (_width + _height) / 2;

    // 1. Biased Random Walk respeitando HorizontalWrap
    for (int step = 0; step < maxSteps && current != endCell; step++)
    {
        Hex curHex = HexAt(current);
        int bestNeighbor = -1;
        float bestDist = float.MaxValue;

        for (int dir = 0; dir < 6; dir++)
        {
            int n = Neighbor(curHex, dir);
            if (n < 0 || _visited[n] == stamp) continue;

            // Usar distância que considera wrap horizontal quando _wrap == true
            float dist = WrappedDistanceSquared(n, endCell);
            if (_random.NextSingle() < randomness)
                dist *= 0.5f;

            if (dist < bestDist)
            {
                bestDist = dist;
                bestNeighbor = n;
            }
        }

        if (bestNeighbor < 0) break;
        current = bestNeighbor;
        _spine.Add(current);
        _visited[current] = stamp;
    }

    // 2. Expansão lateral por anéis (linePower)
    _queue.Clear();
    foreach (int c in _spine) _queue.Enqueue(c);

    float h = height;
    while (_queue.Count > 0 && h > 0.08f)
    {
        int ringCount = _queue.Count;
        for (int i = 0; i < ringCount; i++)
        {
            int cell = _queue.Dequeue();
            float delta = h * (0.85f + _random.NextSingle() * 0.30f);
            _scores[cell] += delta;
            if (writeToRidges && _ridges != null)
                _ridges[cell] = MathF.Max(_ridges[cell], delta);

            Hex hx = HexAt(cell);
            for (int dir = 0; dir < 6; dir++)
            {
                int n = Neighbor(hx, dir);
                if (n >= 0 && _visited[n] != stamp)
                {
                    _visited[n] = stamp;
                    _queue.Enqueue(n);
                }
            }
        }
        h = MathF.Pow(h, linePower) - 0.06f;
    }
}
```

---

### 3.3. Operadores Subtrativos: `AddPit`, `AddTrough` e `AddStrait`

* **`AddPit` (Baías em Ferradura / Golfos / Mares Internos):**
  * Mesma estrutura de BFS do `AddHill`, porém **subtraindo** `_change[cell]` de `_scores[cell]`.
  * Aplicado sobre células costeiras preliminares (score próximo ao `SeaLevel`), escava baías e golfos arredondados.
  * Aplicado no interior continental, forma mares internos (Mediterrâneo/Cáspio) ou grandes bacias lacustres.
* **`AddTrough` (Fiordes / Baías Estreitas / Rifts):**
  * Mesma estrutura do `AddRange`, porém **subtraindo** `_scores[cell]` (sem gravar em `_ridges`).
  * Quando alinhado com `RiftMask` ou perpendicular à linha de costa, rasga fiordes profundos e canais estreitos.
* **`AddStrait` (Estreitos Marítimos):**
  * Traça um caminho curvo atravessando istmos ou zonas de colisão continental indesejada, rebaixando 1 a 3 anéis de largura para garantir passagem marítima navegável (compatível com `_separateContinents` já existente no `ContinentalSculpture`).

---

## 4. Pipeline Completo de Escultura

```
[0. Fundação Tectônica]     -> BaseCrustScore + TectonicRidge (se tectônica ativa)
[1. Núcleos Continentais]   -> AddHill grandes sobre crosta continental
[2. Projeção de Penínsulas] -> AddHill médios nas margens + AddRange apontando para o mar
[3. Escavação de Baías]     -> AddPit na linha costeira + AddTrough em zonas de RiftMask/costa
[4. Fatiamento por Estreitos]-> AddStrait garantindo separação de continentes e ilhas costeiras
[5. Arquipélagos e Ilhas]   -> AddHill pequenos ao longo de VolcanicArcMask e cinturões oceânicos (respeitando coastGap e islandGap)
[6. Suavização + Máscara]   -> 1–2 passadas Laplaciana + atenuação polar (e leste/oeste se !_wrap)
```

### Receita de Recorte Costeiro (Evitando Continentes "Lisos")
Após definir a silhueta preliminar dos continentes:
1. Identifique a faixa litorânea (células próximas ao `SeaLevel` preliminar).
2. Ao longo dessa margem, distribua eventos espaçados:
   * **40% Penínsulas:** `AddHill` médio deslocado ligeiramente para o mar ou `AddRange` curto saindo da costa.
   * **35% Baías / Golfos:** `AddPit` centrado na linha de costa para cavar enseadas arredondadas.
   * **25% Fiordes / Canais:** `AddTrough` curto perpendicular à costa (com maior densidade em latitudes altas / zonas frias ou `RiftMask`).

---

## 5. Naturalização de Costa (Estilo Azgaar `coastline-generator.ts`)

Para evitar que a costa tenha rugosidade uniforme em todo o planeta (o clássico defeito de Perlin puro):

1. **Campo de Rugosidade Heterogêneo (`CoastRoughness`):**
   * Amostre um ruído de 2 oitavas de baixa frequência com expoente de contraste (`MathF.Pow(spread, 1.5f)`).
   * **Zonas calmas (`roughness < 0.25`):** Aplique suavização extra no score (gerando praias abertas, grandes baías suaves e planícies costeiras).
   * **Zonas acidentadas (`roughness >= 0.25`):** Amplifique o micro-detalhe de `SculptedCoastDetail` (gerando falésias, arquipélagos de ilhotas costeiras e costões recortados).
2. **Suavização Visual (Shader / Contorno):**
   * No futuro, ao renderizar linhas costeiras vetoriais ou no `Terrain.gdshader`, o mesmo campo de rugosidade pode modular o *Midpoint Displacement* nas arestas terra/água para esconder completamente os ângulos de $60^\circ$ dos hexágonos.

---

## 6. Critérios de Aceite

- [ ] Sem alocação excessiva de GC nos operadores BFS (uso de buffers reutilizáveis com stamp).
- [ ] `WrappedDistanceSquared` e `Neighbor` respeitam `HorizontalWrap == true` sem cortes na borda do mapa.
- [ ] Continentes exibem alternância natural entre costas suaves (praias/baías) e costas recortadas (penínsulas/fiordes), sem simetria hexagonal de $60^\circ$.
- [ ] Ilhas oceânicas formam arcos coerentes (alinhados a `VolcanicArcMask` quando tectônica estiver ativa) mantendo canais de água entre si.
- [ ] Geração 100% determinística a partir de `WorldManifest.Seed` e calibrada ao `settings.LandPercent`.
