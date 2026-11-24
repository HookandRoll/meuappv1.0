# Teste Hadley com as mesmas seeds no Atlas máximo

Data: 23/11/2026. Gerador v63, seed 1234 e −77, duas gerações independentes por seed. Atlas real: 6144×2784, com 15.044.776 hexes úteis; referência 4096×2048, terra 25%, detalhe costeiro 80%, temperatura e precipitação padrão. Grade Hadley 512×256, 80 passos externos, subdivisão temporal automática.

## Repetição da mesma seed

| Seed | Dados do mapa | Campos Hadley | Buffers de textura |
| --- | --- | --- | --- |
| 1234 | 16 hashes idênticos | 11 arrays idênticos | 7 buffers idênticos |
| -77 | 16 hashes idênticos | 11 arrays idênticos | 7 buffers idênticos |

Os hashes SHA-256 verificam igualdade bit a bit. O mapa inclui elevação, água, relevo, biomas, formas geográficas, features, temperatura, chuva, estradas, rios, flags, locks, entidades esparsas, tectônica e regiões. Os campos Hadley incluem vento, pressão e massa de ambas as camadas, transferência vertical e vorticidade.

Em cada uma das quatro execuções, o teste também comparou o mapa antes e depois de executar Hadley. Todas passaram: o simulador não alterou terreno, clima, rios ou regiões.

## Vento antigo comparado ao Hadley no solo

| Seed | Pontos comparáveis | Diferença angular média | Mediana | Percentil 90 | Pontos com diferença maior que 45° |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1234 | 126145 | 33,64° | 15,09° | 96,51° | 23,91% |
| -77 | 126062 | 33,17° | 14,82° | 95,60° | 22,93% |

A referência antiga usa a direção exibida pela camada Ventos: os pesos zonais e meridionais de AnnualWind. O Hadley usa o campo superficial calculado sobre o mesmo mapa. A diferença inclui a dinâmica de duas camadas e os efeitos do mundo gerado; não isola somente a contribuição das montanhas.

## Solo comparado à altitude

| Seed | Pontos comparáveis | Ângulo médio entre camadas | Pontos com ângulo maior que 90° |
| --- | ---: | ---: | ---: |
| 1234 | 124827 | 145,28° | 86,89% |
| -77 | 124679 | 146,17° | 87,44% |

Ângulo maior que 90° indica movimento predominantemente contrário, sem exigir oposição exata de 180°. As estatísticas amostram uniformemente a grade atmosférica e excluem vetores de módulo menor que 0,02. Os percentuais se referem aos pontos comparáveis, não à área física do planeta.

## Resposta a seeds diferentes

Entre 1234 e −77, 11 dos 11 campos Hadley tiveram hashes diferentes. O simulador responde ao terreno, à distribuição de água e ao clima do mundo; ele não sorteia um novo vento ao abrir a camada.

## Tempo e estabilidade

| Seed | Repetição | Geração do mapa | Hadley | Preparar e verificar texturas | Erro relativo de massa | Menor massa de camada |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | 1 | 172,22 s | 10,57 s | 4,49 s | 8.156e-10 | 0,19 |
| 1234 | 2 | 159,46 s | 9,79 s | 4,43 s | 8.156e-10 | 0,19 |
| -77 | 1 | 155,48 s | 10,32 s | 4,34 s | 9.446e-10 | 0,20 |
| -77 | 2 | 157,77 s | 10,01 s | 4,34 s | 9.446e-10 | 0,20 |

Pico acumulado de working set do processo Godot durante o teste: 5757,52 MiB. Inclui geração, mapa aberto, buffers gráficos e simulação; não representa o custo isolado de Hadley.

Todas as massas permaneceram positivas e todos os valores atmosféricos finitos. O erro relativo de massa ficou abaixo do limite de 1e-5. Tempos locais de quatro execuções, sem repetição estatística adicional; preparar texturas inclui seus hashes e exclui a espera de frames e as capturas.

## Capturas

As capturas mantêm o mesmo terreno em cada comparação. Não há comparação byte a byte dos PNGs, pois a interface contém estados como FPS. A igualdade dos buffers é verificada separadamente.

### Seed 1234

[Terreno](hadley-seed-comparison/seed-1234-terrain.png) · [Vento antigo](hadley-seed-comparison/seed-1234-old-wind.png) · [Hadley no solo](hadley-seed-comparison/seed-1234-surface.png) · [Hadley em altitude](hadley-seed-comparison/seed-1234-upper.png)

### Seed -77

[Terreno](hadley-seed-comparison/seed--77-terrain.png) · [Vento antigo](hadley-seed-comparison/seed--77-old-wind.png) · [Hadley no solo](hadley-seed-comparison/seed--77-surface.png) · [Hadley em altitude](hadley-seed-comparison/seed--77-upper.png)

## Reprodução

```text
godot --path . -- --hadley-seed-comparison
```

O comando gera os quatro mapas e encerra com erro se detectar diferenças entre repetições, mutação dos dados de origem ou falhas numéricas. O JSON com todos os hashes está em `results/hadley-seed-comparison/summary.json`.

O protótipo continua separado da geração de chuva e mantém seu forçamento por latitude. Este teste confirma repetibilidade e diferenças entre campos de vento; não demonstra mudança nos desertos ou validação meteorológica completa.
