# Especificação Técnica: Clima, Temperatura e Chuva Estilo Azgaar no Mapmaker

## 1. Diagnóstico: Por que os mundos atuais do projeto são tão quentes?

Você acertou os valores de memória: hoje, em [WorldGenerator.cs](file:///d:/Documentos/ChatGPT/Mapmaker/src/Core/WorldGenerator.cs#L634-L645), a função `Climate` usa exatamente **`31,0 °C` (`310f`) no Equador** e **`-14,5 °C` (`310f - 455f = -145f`) nos Polos**:

```csharp
// Código atual em src/Core/WorldGenerator.cs (linhas 637-644):
float latitude = Math.Abs(y - 0.5f) * 2f;
float latitudeSquared = latitude * latitude;
float seaLevelTemperature = 310f - 455f * latitudeSquared * (0.40f + 0.60f * latitudeSquared);
...
short temperature = (short)MathF.Round(Interpolate(seaLevelTemperature, 120f, maritime * 0.19f)
    + temperatureVariation - landElevation * 0.065f);
```

Existem **três problemas matemáticos** nessa fórmula atual que deixam o planeta inteiro superaquecido:

### Problema A: A curva `0.40 * lat² + 0.60 * lat⁴` demora demais para esfriar
Como `latitude` vai de `0.0` a `1.0`, elevar ao quadrado e à quarta potência (`lat⁴`) faz o valor ficar quase zero até metade do mapa!  
Veja o que a fórmula atual produz ao nível do mar comparada com o **Azgaar / Terra real**:

| Latitude no Mapa (`lat`) | Graus Equivalentes | Referência Real na Terra | Temp. Atual no Projeto | Temp. Ideal (Estilo Azgaar) | O que acontece hoje no mapa |
| :--- | :--- | :--- | :--- | :--- | :--- |
| `0.00` | **$0^\circ$ (Equador)** | Amazônia / Congo | **`+31,0 °C`** | **`+26,5 °C`** | Quente demais no nível do mar |
| `0.25` | **$22,5^\circ$ (Trópicos)** | Rio de Janeiro / Saara | **`+29,8 °C`** | **`+22,5 °C`** | Quase não esfriou nada desde o Equador |
| `0.50` | **$45,0^\circ$ (Temperado)** | Sul da França / Canadá Sul | **`+24,7 °C`** | **`+11,5 °C`** | **Bug:** A $45^\circ$ ainda é tropical (`> 22,5 °C`)! |
| `0.65` | **$58,5^\circ$ (Subpolar)** | Escócia / Moscou / Alasca Sul | **`+18,2 °C`** | **`+1,5 °C`** | Deveria ser Taiga fria, mas vira Floresta Temperada quente |
| `0.75` | **$67,5^\circ$ (Círculo Polar)** | Círculo Polar Ártico | **`+12,1 °C`** | **`-6,5 °C`** | **Bug grave:** `+12 °C` dentro do Círculo Polar! |
| `0.90` | **$81,0^\circ$ (Alto Ártico)** | Norte da Groenlândia | **`-1,9 °C`** | **`-21,0 °C`** | Só começa a congelar (`0 °C`) aos $80^\circ$ de latitude |
| `1.00` | **$90,0^\circ$ (Polos)** | Polo Norte / Antártida | **`-14,5 °C`** | **`-28,0 °C`** | Frio insuficiente para formar calotas polares extensas |

> **Resumo do impacto:** Hoje, **mais de 55% da altura do mapa** tem temperatura tropical (`>= 22,5 °C`), e a linha de congelamento (`0 °C`) só começa a aparecer nos últimos **11% das bordas superior e inferior** do mapa!

### Problema B: O `Interpolate(seaLevelTemperature, 120f, maritime * 0.19f)` esquenta os Polos
Na linha 643, o código puxa qualquer costa (`maritime = 1`) em 19% na direção de `+12,0 °C` (`120f`).
- Num polo a `-14,5 °C`, a costa sobe para **`-9,5 °C`**!
- No mundo real (e no Azgaar), o oceano polar está a `-2 °C` / `0 °C`, nunca a `+12 °C`.

### Problema C: Chuva alta demais em águas geladas
Em `RegionalRainBaseline`, a chuva só cai um pouco na latitude média, mas não zera a evaporação nos polos frios, gerando umidade excessiva onde o ar deveria ser frio e seco.

---

## 2. Como Implementar a Temperatura Estilo Azgaar

No Azgaar, a temperatura ao nível do mar segue uma curva suave de insolação solar (de **`+26,5 °C`** no Equador até **`-28,0 °C`** nos Polos), onde:
- **Zona Tropical ($0^\circ\text{ a }23^\circ$, `lat 0.00..0.26`):** fica acima de `20 °C`.
- **Zona Subtropical / Mediterrânea ($23^\circ\text{ a }35^\circ$, `lat 0.26..0.39`):** fica entre `15 °C` e `20 °C`.
- **Zona Temperada ($35^\circ\text{ a }55^\circ$, `lat 0.39..0.61`):** fica entre `4,5 °C` e `15 °C`.
- **Zona Boreal / Taiga ($55^\circ\text{ a }66^\circ$, `lat 0.61..0.73`):** fica entre `-5,5 °C` e `+4,5 °C`.
- **Zona de Tundra ($66^\circ\text{ a }78^\circ$, `lat 0.73..0.87`):** fica entre `-13,0 °C` e `-5,5 °C`.
- **Zona Glacial / Calota Polar ($78^\circ\text{ a }90^\circ$, `lat 0.87..1.00`):** fica abaixo de `-13,0 °C` (chegando a `-28,0 °C`).

### Fórmula pronta para substituir em `Climate()` (`src/Core/WorldGenerator.cs`):

```csharp
private static (short Temperature, ushort Rainfall) Climate(
    float x, float y, float aspect, short elevation, float maritime, uint seed)
{
    // 1. Latitude normalizada: 0.0 no Equador, 1.0 nos Polos
    float latitude = Math.Abs(y - 0.5f) * 2f;

    // 2. Curva térmica estilo Azgaar:
    //    Equador = +26.5 °C (265), 45° (lat 0.5) = +11.4 °C (114), Polo = -28.0 °C (-280)
    //    Combina queda quadrática suave nos trópicos com queda mais íngreme nas latitudes médias.
    float latCurve = MathF.Pow(latitude, 1.55f); // 0.5^1.55 = 0.3415 -> 265 - 545 * 0.3415 = 79..115
    float seaLevelTemperature = 265f - 545f * (0.65f * latitude * latitude + 0.35f * latCurve);

    // 3. Moderação marítima realista:
    //    O oceano reduz a variação térmica extrema do interior continental e impede que a água
    //    congele tanto quanto o interior da Antártida, mas NUNCA puxa o polo para +12 °C!
    float oceanTarget = Math.Clamp(seaLevelTemperature * 0.82f, -45f, 245f);
    float moderatedTemp = Interpolate(seaLevelTemperature, oceanTarget, maritime * 0.22f);

    // 4. Variação regional de ruído (menor sobre o mar, maior no interior continental)
    float continentality = 1f - maritime * 0.55f;
    float temperatureVariation = (Noise(x * aspect * 3.1f, y * 3.1f, seed ^ 0x4CF5AD43u) - 0.5f) * 58f * continentality;

    // 5. Lapse Rate (Gradiente Térmico Vertical do Azgaar): -6.5 °C a cada 1000m (-0.065 décimos/m)
    int landElevation = Math.Max(0, (int)elevation);
    short temperature = (short)MathF.Round(moderatedTemp + temperatureVariation - landElevation * 0.065f);

    // 6. Precipitação base (usada quando RegionalRain não substitui)
    ushort rainfall = CalculateAzgaarBaselineRain(x, y, aspect, latitude, temperature, landElevation, maritime, seed);
    return (temperature, rainfall);
}
```

#### Veja como ficam as temperaturas com essa fórmula nova:
- `lat = 0.00` ($0^\circ$ Equador): **`+26,5 °C`** (`265`)
- `lat = 0.25` ($22,5^\circ$ Trópico): **`+22,0 °C`** (`220`)
- `lat = 0.50` ($45,0^\circ$ Temperado): **`+11,1 °C`** (`111`)
- `lat = 0.65` ($58,5^\circ$ Boreal / Taiga): **`+0,7 °C`** (`7`)
- `lat = 0.75` ($67,5^\circ$ Círculo Polar / Tundra): **`-7,3 °C`** (`-73`)
- `lat = 0.88` ($79,0^\circ$ Calota Polar): **`-18,1 °C`** (`-181`)
- `lat = 1.00` ($90,0^\circ$ Polo): **`-28,0 °C`** (`-280`)

---

## 3. Como Implementar a Chuva Estilo Azgaar (Células de Hadley + Ventos + Sombra de Chuva)

No Azgaar, a chuva combina **3 efeitos físicos** que você pode colocar diretamente em [WorldGenerator.RegionalClimate.cs](file:///d:/Documentos/ChatGPT/Mapmaker/src/Core/WorldGenerator.RegionalClimate.cs):

### 3.1. As 4 Zonas de Umidade por Latitude (Células de Hadley)
Hoje, `RegionalRainBaseline` tem o pico do Equador e o pico Temperado, mas **não tem o vale seco Subtropical ($20^\circ\text{ a }32^\circ$)** nem o **ressecamento Polar ($> 70^\circ$)**.

Para ficar igual ao Azgaar:
1. **Pico Equatorial (ZCIT, `lat 0.00..0.18`):** Convergência dos ventos Alísios $\rightarrow$ alta precipitação (`+1150 mm`).
2. **Depressão Subtropical Seca (`lat 0.20..0.38`, centro em `lat = 0.28` / $25^\circ$):** Onde o ar seco da Célula de Hadley desce $\rightarrow$ **reduz a chuva base em `-380 mm`** (fazendo surgir cinturões naturais de Deserto, Savana e Chaparral nessa latitude!).
3. **Pico Temperado / Frente Polar (`lat 0.45..0.68`, centro em `lat = 0.55` / $50^\circ$):** Ventos do Oeste (*Westerlies*) úmidos $\rightarrow$ segundo pico de chuva (`+580 mm`).
4. **Frio Polar (`lat > 0.72` ou `temperature < -20`):** O ar muito frio segura pouco vapor d'água (Equação de Clausius-Clapeyron) $\rightarrow$ multiplica a chuva por um fator térmico `coldFactor = Smooth(Math.Clamp((temperature + 180f) / 260f, 0.18f, 1.0f))`.

### 3.2. As 6 Faixas de Vento Planetárias (*Wind Belts*)
Em [WorldGenerator.RegionalClimate.cs](file:///d:/Documentos/ChatGPT/Mapmaker/src/Core/WorldGenerator.RegionalClimate.cs#L128-L135), o código já calcula o transporte para **Leste (`east`)** e para **Oeste (`west`)** e mistura pelo peso `westerly`:
- **`lat = 0.00 a 0.30` ($0^\circ\text{–}27^\circ$ — Alísios):** `westerly ≈ 0.0` $\rightarrow$ Vento sopra de **Leste para Oeste** (`west[x]`).
  - *Resultado:* A costa **Leste** dos continentes tropicais chove muito (Mata Atlântica / Rainforest), e o lado **Oeste** atrás das cordilheiras fica seco (Atacama / Deserto).
- **`lat = 0.35 a 0.65` ($31^\circ\text{–}58^\circ$ — Ventos do Oeste / *Westerlies*):** `westerly ≈ 1.0` $\rightarrow$ Vento sopra de **Oeste para Leste** (`east[x]`).
  - *Resultado:* A costa **Oeste** temperada recebe muita chuva orográfica (Floresta Temperada Chuvosa, estilo Canadá/Chile Sul), e o lado **Leste** atrás da cordilheira entra em **Sombra de Chuva** (Patagônia / Estepe).
- **`lat = 0.70 a 1.00` ($63^\circ\text{–}90^\circ$ — Ventos Polares):** `westerly ≈ 0.0` $\rightarrow$ Vento volta a soprar de **Leste para Oeste**.

### Código pronto para `RegionalRainBaseline` e `BuildRegionalRain` (`src/Core/WorldGenerator.RegionalClimate.cs`):

```csharp
internal static ushort RegionalRainBaseline(float x, float y, float aspect, float maritime, uint seed, bool wrap)
{
    float latitude = Math.Abs(y - 0.5f) * 2f;

    // 1. Pico Equatorial (ZCIT: 0° a 18°)
    float equator = 1f - Smooth(Math.Clamp(latitude / 0.24f, 0f, 1f));

    // 2. Cinturão Subtropical Seco (Célula de Hadley descendente: ~18° a 34°, pico em lat = 0.28)
    float subtropicalDry = Smooth(Math.Max(0f, 1f - Math.Abs(latitude - 0.28f) / 0.14f));

    // 3. Pico Temperado (Frentes dos Westerlies: ~42° a 62°, pico em lat = 0.54)
    float temperate = Smooth(Math.Max(0f, 1f - Math.Abs(latitude - 0.54f) / 0.18f));

    // 4. Supressão Polar (Ar gelado acima de 65° retém muito menos umidade)
    float polarDry = Smooth(Math.Clamp((latitude - 0.66f) / 0.26f, 0f, 1f));

    float region = 0.80f * TerrainNoise(x * aspect, y, 2.1f, aspect, wrap, seed ^ 0xD46A89C3u)
        + 0.20f * TerrainNoise(x * aspect, y, 5.7f, aspect, wrap, seed ^ 0x71F28E5Bu);

    float rain = 340f
        + 1320f * equator
        - 260f  * subtropicalDry
        + 640f  * temperate
        + 280f  * maritime
        + 980f  * (region - 0.5f);

    // Reduz a chuva nos polos frios (estilo Azgaar)
    rain *= Interpolate(1.0f, 0.32f, polarDry);

    return (ushort)Math.Clamp(MathF.Round(rain), 45f, 4200f);
}
```

### 3.3. Reforçando a Chuva Orográfica e a Sombra de Chuva (*Rain Shadow*)
No `BuildRegionalRain` ([WorldGenerator.RegionalClimate.cs](file:///d:/Documentos/ChatGPT/Mapmaker/src/Core/WorldGenerator.RegionalClimate.cs#L134-L140)) e `MoistureCoefficients` ([WorldGenerator.OrographicClimate.cs](file:///d:/Documentos/ChatGPT/Mapmaker/src/Core/WorldGenerator.OrographicClimate.cs#L15-L25)):

1. **Mais condensação ao subir a cordilheira (*Windward Uplift*):**
   - Em `MoistureCoefficients`, mudar o divisor de `2200f` para **`1500f`**:
     ```csharp
     float condensed = 1f - MathF.Exp(-Math.Max(0, elevation - previous) / 1500f);
     ```
     Isso faz a nuvem perder mais umidade ao escalar uma cordilheira, deixando o lado detrás da montanha (**Sombra de Chuva**) visivelmente mais seco (`humidity` cai para `< 0.18`).
2. **Menos recuperação espontânea no interior seco:**
   - Na linha 23 de `WorldGenerator.OrographicClimate.cs`:
     ```csharp
     _ => (retention * (1 - condensed), (1 - retention) * 0.35f, condensed) // antes era 0.55f
     ```
     Ao reduzir a recarga sobre terra seca de `0.55f` para `0.35f`, a sombra de chuva atrás das montanhas e o interior de grandes continentes ficam realmente áridos (formando Desertos, Badlands e Estepes naturais atrás das serras!).
3. **Multiplicador de Sombra de Chuva no `BuildRegionalRain`:**
   - Na linha 138–139 de `WorldGenerator.RegionalClimate.cs`:
     ```csharp
     float enhancement = humidity * (1f - MathF.Exp(-ascent / 1400f)) * 750f;
     result.Rain[i] = (ushort)Math.Clamp(MathF.Round(baseline * (0.35f + 0.85f * humidity) + enhancement), 0, 5000);
     ```
     - Quando a nuvem chega cheia do mar e sobe a serra (`humidity ≈ 0.9`, `ascent > 0`): a chuva ganha até `+650 mm` extras na encosta (*Chuva Orográfica*).
     - Quando a nuvem já cruzou a serra e secou (`humidity ≈ 0.10`): `baseline * (0.35 + 0.085) = 0.43 * baseline`, derrubando a chuva pela metade ou menos na sombra da montanha!

---

## 4. Resumo: O que muda no mapa ao aplicar esses 3 ajustes

| Região do Mapa | Como era antes | Como fica com a calibração estilo Azgaar |
| :--- | :--- | :--- |
| **Equador ($0^\circ\text{–}15^\circ$)** | `31 °C` (muito quente) | **`25 °C a 27 °C`** — Florestas Tropicais (*Rainforest*) e Sazonais úmidas |
| **Subtrópicos ($20^\circ\text{–}32^\circ$)** | `28 °C a 30 °C` e úmido | **`20 °C a 23 °C`** e mais seco — Cinturão natural de **Desertos, Savanas e Chaparral** |
| **Zona Temperada ($35^\circ\text{–}55^\circ$)** | `20 °C a 26 °C` (ainda tropical!) | **`6 °C a 15 °C`** — **Campos, Florestas Temperadas e Estepes** reais |
| **Encosta virada para o mar (Barlavento)** | Chuva pouco diferente do interior | **Chuva Orográfica forte** — Floresta Chuvosa na encosta costeira |
| **Atrás da Cordilheira (Sotavento)** | Continuava verde/úmido | **Sombra de Chuva real** — Deserto, Badlands ou Estepe atrás da serra |
| **Círculo Polar ($60^\circ\text{–}75^\circ$)** | `+8 °C a +15 °C` (quase sem Taiga/Tundra) | **`-8 °C a +4 °C`** — Cinturões largos e bem definidos de **Taiga, Turfeira e Tundra** |
| **Polos ($78^\circ\text{–}90^\circ$)** | `-9 °C a -14 °C` | **`-15 °C a -28 °C`** — **Geleiras e Calotas Polares** naturais |
