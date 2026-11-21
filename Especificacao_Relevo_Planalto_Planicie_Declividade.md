# Especificação Simplificada: Planaltos, Planícies e Montanhas por Declividade nos Hexes

## 1. Por que hoje uma área alta inteira vira Morros ou Montanhas?

Atualmente, em `src/Core/WorldGenerator.cs` (linhas 201–205), o tipo de relevo (`ReliefType`) é escolhido olhando **apenas para a altitude absoluta (`Elevation[i]`)** do hex:

```csharp
area.Relief[i] = area.Elevation[i] switch
{
    >= 3200 => ReliefType.Peak,
    >= 1850 => ReliefType.Mountains,
    >= 650  => ReliefType.Hills,
    >= 220  => ReliefType.Rolling,
    _       => ReliefType.Flat
};
```

O problema dessa regra pura de altitude é exatamente o que você descreveu:
- Se você tiver uma área de **100 hexes a 1200m** (ou 1900m), **todos os 100 hexes** recebem ícone de morro/montanha.
- Na geografia real, **montanha ou escarpa não é apenas estar alto — é ter inclinação (desnível)**. Uma área extensa a 1200m onde todos os vizinhos também estão a ~1200m é plana no topo: isso é um **Planalto (ou Chapada)**.

---

## 2. A Solução Simplificada: Altitude ($H$) + Desnível com os 6 Vizinhos ($\Delta H$)

Em uma grade hexagonal (`HexGrid`), cada hex tem **6 vizinhos diretos**.  
Para saber que feature geográfica um hex forma, basta olhar **dois números**:

1. **Altitude do hex ($H$):** `Elevation[i]` em metros.
2. **Desnível local ($\Delta H$):** a maior diferença de altitude entre o hex atual e seus 6 vizinhos terrestres.

$$\Delta H = \max_{v \in \text{vizinhos}} |H_{\text{atual}} - H_v|$$

*(Opcionalmente, você também pode separar $\Delta H_{\text{queda}} = H_{\text{atual}} - \min(H_v)$ para saber se o hex está na **crista/borda de cima** do desnível ou no **pé/base**).*

---

## 3. O seu exemplo na prática (100 hexes a 1200m ao lado de hexes a 100m)

Imagine uma região com 100 hexes a **1200m**, cercada por uma área baixa a **100m**:

```text
   [ 100m ]  [ 100m ]  [1200m]  [1200m]  [1200m]
   Planície  Planície  BORDA!   Planalto Planalto
   (ΔH = 0)  (ΔH = 0) (ΔH=1100) (ΔH = 0) (ΔH = 0)
```

1. **No interior dos 100 hexes (ex.: hex a 1200m cercado por vizinhos a 1180m–1200m):**
   - **Altitude ($H$):** 1200m (Alta)
   - **Desnível ($\Delta H$):** ~20m (Quase zero, terreno plano)
   - **O que ele é:** **Planalto**!
   - **Como desenhar no mapa:** Relevo visual `Flat` ou `Rolling` (sem desenhar 100 sprites de montanhas empilhados), mantendo a altitude de 1200m.

2. **Na borda dos 100 hexes (hex a 1200m encostado num vizinho a 100m):**
   - **Altitude ($H$):** 1200m
   - **Desnível de queda ($\Delta H_{\text{queda}}$):** $1200 - 100 = 1100\text{ m}$ (Muito alto!)
   - **O que ele é:** **Montanha de Borda / Serra / Escarpa**!
   - **Como desenhar no mapa:** Recebe `ReliefType.Mountains` (ou `Hills` se o degrau for menor), criando um anel ou paredão montanhoso exatamente na fronteira do planalto.

3. **Na área baixa (hex a 100m cercado por vizinhos a 100m):**
   - **Altitude ($H$):** 100m (Baixa)
   - **Desnível ($\Delta H$):** ~0m (Plano)
   - **O que ele é:** **Planície** (`ReliefType.Flat`).

---

## 4. Tabela Simplificada de Regras (Para o Código)

Com apenas **Altitude ($H$)** e **Desnível ($\Delta H$)**, você classifica tanto o sprite de relevo (`ReliefType`) quanto o nome geográfico da feature:

| Altitude do Hex ($H$) | Desnível com Vizinhos ($\Delta H$) | Relevo Visual (`ReliefType`) | Nome da Feature Geográfica |
| :--- | :--- | :--- | :--- |
| **Qualquer** ($\ge 3200\text{ m}$ ou $\Delta H \ge 1400\text{ m}$) | Extremo | `Peak` (Picos) | **Pico / Cordilheira Alta** |
| **Média ou Alta** ($\ge 500\text{ m}$) | **Alto** ($\Delta H \ge 600\text{ m}$) | `Mountains` (Montanhas) | **Montanha / Serra / Escarpa** |
| **Qualquer** | **Moderado** ($250\text{ m} \le \Delta H < 600\text{ m}$) | `Hills` (Morros) | **Morros / Encosta** |
| **Alta** ($\ge 700\text{ m}$) | **Baixo** ($\Delta H < 120\text{ m}$) | `Flat` (Plano) | **Planalto / Chapada** |
| **Alta** ($\ge 700\text{ m}$) | **Suave** ($120\text{ m} \le \Delta H < 250\text{ m}$) | `Rolling` (Ondulado) | **Planalto Ondulado** |
| **Baixa** ($< 400\text{ m}$) | **Baixo** ($\Delta H < 100\text{ m}$) | `Flat` (Plano) | **Planície** |
| **Baixa** ($< 700\text{ m}$) | **Suave** ($100\text{ m} \le \Delta H < 250\text{ m}$) | `Rolling` (Ondulado) | **Colinas / Campina Ondulada** |
| **Mais baixo que todos os 6 vizinhos** | Vizinhos $\ge 300\text{ m}$ mais altos | `Flat` ou `Rolling` | **Vale / Depressão** |

> **Dica de ajuste:** Na borda entre `1200m` e `100m`, tanto o hex de `1200m` quanto o de `100m` têm $|\Delta H| = 1100\text{ m}$.  
> Para que a montanha nasça **no hex alto (1200m)** e não no hex baixo (100m), usamos a **queda para o vizinho mais baixo** (`drop = H - minVizinho`) para criar a montanha na crista, e no hex de baixo (`100m`) podemos colocar no máximo `Rolling` ou `Hills` (sopé da montanha) ou mantê-lo como `Flat` (planície na base da escarpa).

---

## 5. Como o Tamanho do Mapa (`Width × Height`) e a Escala (`HexDistance`) afetam os valores?

**Sim, os valores mudam conforme o tamanho do mapa!** E há dois motivos para isso no Mapmaker:

### Motivo A: O gerador usa coordenadas normalizadas (`x / Width`, `y / Height`)
No `WorldGenerator.cs`, os continentes e o relevo são gerados em coordenadas de `0.0` a `1.0`:
- Num mapa pequeno (**128 × 64**), uma encosta que sobe de `100m` para `1200m` acontece em apenas **1 ou 2 hexes** ($\Delta H$ por hex vizinho $\approx 550\text{ m a } 1100\text{ m}$).
- Num mapa gigante (**4096 × 2048**, que é **32× maior** em largura e altura), essa **mesma encosta** fica espalhada por **32 a 64 hexes**! Ou seja, de um hex para o vizinho imediato (`passo = 1`), a altura sobe apenas **20m a 35m por hex**, embora no conjunto forme a mesma escarpa de 1100m.

### Motivo B: Distância real por hex (`Manifest.HexDistance` em km)
Se o usuário definir `1 hex = 50 km`, um desnível de `300m` para o hex vizinho é uma inclinação suave ($300\text{ m} / 50\text{ km}$). Se `1 hex = 1 km`, o mesmo desnível de `300m` é um paredão muito íngreme ($300\text{ m} / 1\text{ km}$).

### Como resolver isso de forma simples para qualquer tamanho de mapa?

Você pode combinar **dois alcances de leitura** (o projeto já usa essa técnica em `WorldGenerator.cs` com `inlandSteps = Math.Max(1, world.Height / 96)`):

1. **Vizinho imediato (`passo = 1` hex):**
   - Captura **degraus pintados à mão no Editor** (ex.: quando você pinta um bloco a `1200m` encostado em `100m` com o pincel, a borda sempre tem `1100m` em 1 hex de distância, não importa se o mapa é `128×64` ou `4096×2048`).
2. **Vizinho em escala regional (`passo = Math.Max(1, world.Height / 256)` hexes):**
   - Escala automaticamente com a resolução do mapa:
     - **128 × 64** e **256 × 128** $\rightarrow$ `passo = 1` hex
     - **512 × 256** $\rightarrow$ `passo = 1` hex
     - **1024 × 512** $\rightarrow$ `passo = 2` hexes
     - **2048 × 1024** $\rightarrow$ `passo = 4` hexes
     - **4096 × 2048** $\rightarrow$ `passo = 8` hexes
   - Ao medir a diferença entre o hex atual e o hex a `passo` de distância, uma encosta gerada proceduralmente terá **o mesmo $\Delta H$ em qualquer preset de tamanho de mapa**, sem você precisar criar uma tabela de valores diferente para cada resolução!

$$\Delta H_{\text{efetivo}} = \max\Big(\Delta H_{\text{passo } 1},\; \Delta H_{\text{passo regional}}\Big)$$

| Preset do Mapa | `Height` | `passo regional` (`Height / 256`) | Como funciona |
| :--- | :--- | :--- | :--- |
| **128 × 64** / **256 × 128** | 64 / 128 | `1` hex | Olha apenas os 6 vizinhos imediatos |
| **512 × 256** | 256 | `1` hex | Olha os 6 vizinhos imediatos |
| **1024 × 512** | 512 | `2` hexes | Olha o vizinho imediato (pincel) e a 2 hexes (gerador) |
| **2048 × 1024** | 1024 | `4` hexes | Olha o vizinho imediato (pincel) e a 4 hexes (gerador) |
| **4096 × 2048** | 2048 | `8` hexes | Olha o vizinho imediato (pincel) e a 8 hexes (gerador) |

---

## 6. Como implementar isso no projeto (C#)

Você pode adicionar uma função auxiliar simples que calcula o relevo e o nome geográfico de qualquer célula `i` usando `HexGrid.NeighborIndex`:

```csharp
public enum LandformType : byte
{
    None,
    Planicie,          // Baixo e plano
    Colinas,           // Baixo e levemente ondulado
    Planalto,          // Alto e plano no topo (ex: interior dos 100 hexes a 1200m)
    EscarpaOuSerra,    // Borda abrupta de um planalto (ex: hex 1200m vizinho de 100m)
    Morros,            // Desnível moderado
    Montanhas,         // Desnível alto ou crista tectônica
    Pico,              // Topo extremo
    ValeOuDepressao    // Cercado por hexes bem mais altos
}

public static (ReliefType Relief, LandformType Landform) ClassifyHexRelief(WorldData world, int cell)
{
    if (world.Water[cell] != WaterType.None)
        return (ReliefType.Flat, LandformType.None);

    short h = world.Elevation[cell];
    Hex hex = HexGrid.FromOffset(cell % world.Width, cell / world.Width);
    int regionalStep = Math.Max(1, world.Height / 256); // Escala com o tamanho do mapa (1 a 8 hexes)

    short minNeighbor = h;
    short maxNeighbor = h;
    int higherNeighbors = 0;
    int validNeighbors = 0;

    for (int dir = 0; dir < 6; dir++)
    {
        // 1. Vizinho imediato (steps = 1): detecta bordas pintadas com o pincel em qualquer tamanho de mapa
        int n1 = HexGrid.NeighborIndex(hex, dir, world.Width, world.Height, world.Manifest.HorizontalWrap, steps: 1);
        if (n1 >= 0)
        {
            short nh1 = world.Water[n1] == WaterType.Ocean ? (short)0 : world.Elevation[n1];
            if (nh1 < minNeighbor) minNeighbor = nh1;
            if (nh1 > maxNeighbor) maxNeighbor = nh1;
        }

        // 2. Vizinho na escala regional (steps = Height / 256): mantém a mesma proporção de encosta de 128x64 até 4096x2048
        int nReg = regionalStep == 1 ? n1
            : HexGrid.NeighborIndex(hex, dir, world.Width, world.Height, world.Manifest.HorizontalWrap, steps: regionalStep);
        if (nReg >= 0)
        {
            short nhReg = world.Water[nReg] == WaterType.Ocean ? (short)0 : world.Elevation[nReg];
            if (nhReg < minNeighbor) minNeighbor = nhReg;
            if (nhReg > maxNeighbor) maxNeighbor = nhReg;
            if (nhReg - h >= 250) higherNeighbors++;
            validNeighbors++;
        }
    }

    int drop = h - minNeighbor; // Queda para o vizinho mais baixo (detecta borda de planalto!)
    int rise = maxNeighbor - h; // Subida para o vizinho mais alto (detecta pé de serra / vale)

    // 1. Picos extremos
    if (h >= 3200 && drop >= 400)
        return (ReliefType.Peak, LandformType.Pico);

    // 2. Borda abrupta (ex: hex a 1200m ao lado de hex a 100m -> drop = 1100m)
    if (drop >= 600)
        return (ReliefType.Mountains, h >= 700 ? LandformType.EscarpaOuSerra : LandformType.Montanhas);

    // 3. Desnível moderado (encostas e morros)
    if (drop >= 250)
        return (ReliefType.Hills, LandformType.Morros);

    // 4. Vale ou depressão (hex baixo cercado por vários vizinhos bem mais altos)
    if (validNeighbors >= 4 && higherNeighbors >= 3)
        return (ReliefType.Rolling, LandformType.ValeOuDepressao);

    // 5. Terreno de topo plano ou suave (drop < 250m):
    // Aqui está o segredo do Planalto vs Planície!
    if (h >= 700)
    {
        // Está a 700m+ (ex: 1200m), mas os vizinhos têm quase a mesma altura!
        ReliefType visual = drop >= 110 ? ReliefType.Rolling : ReliefType.Flat;
        return (visual, LandformType.Planalto);
    }

    // Abaixo de 700m e sem grande desnível
    if (drop >= 100 || h >= 300)
        return (ReliefType.Rolling, LandformType.Colinas);

    return (ReliefType.Flat, LandformType.Planicie);
}
```

---

## 7. Onde plugar isso no Mapmaker?

Existem **3 lugares** onde essa lógica encaixa direto no seu código:

1. **Na Geração do Mapa (`src/Core/WorldGenerator.cs`):**
   - Em vez de definir `area.Relief[i]` no mesmo loop em que calcula `area.Elevation[i]`, você primeiro preenche todo o array `area.Elevation` e, em seguida, roda um segundo passo rápido olhando os 6 vizinhos de cada hex com `ClassifyHexRelief`.
2. **No Pincel de Elevação do Editor (`src/GodotApp/MapView.Editor.cs`):**
   - No método `FinishGesture()` (quando você solta o clique após usar a ferramenta **Definir 1200m**, **Subir** ou **Nivelar**), você pode recalcular automaticamente o `Relief` dos hexes pintados e de seus vizinhos (desde que não estejam com `HexLocks.Relief` travado).
   - Assim, ao pintar uma mancha de 100 hexes com `Definir 1200m` no meio de uma planície de `100m`, o **meio da mancha fica plano (Planalto)** e a **borda da mancha ganha Montanhas/Morros automaticamente**!
3. **Na Barra de Inspeção (`src/GodotApp/MapView.cs` e `MapEditorPanel.cs`):**
   - No rodapé onde hoje aparece `Plano · 1200 m`, você pode exibir o nome geomorfológico derivado:
     - `Planalto (Plano) · 1200 m` (no meio dos 100 hexes)
     - `Serra / Escarpa (Montanhas) · 1200 m` (na borda para os 100m)
     - `Planície (Plano) · 100 m` (na parte baixa)
     - `Vale / Depressão · 150 m` (entre duas serras)
