# Clima antigo comparado ao Hadley sem faixa subtropical

Teste em 23/11/2026. Seeds 1234 e −77, Atlas de 6144×2784 com 15.044.776 hexes úteis, referência 4096×2048, gerador v63, terra 25%, detalhe costeiro 80%, temperatura e precipitação padrão. Uma geração completa por modelo e seed.

## O que foi desligado

- O caminho experimental não chama ModernRainBaseline e não usa o termo −260 × subtropical nem os reforços latitudinais de chuva do modelo antigo.
- O campo Hadley experimental usa UseZonalPressureBands=false. Não há os máximos de pressão prescritos em 30° nem os mínimos prescritos em 60°.
- Permanece um forçamento térmico simples e monotônico entre equador e polos, proporcional a −cos(π × latitude), além da anomalia térmica continental. Isso ainda é uma aproximação prescrita, não insolação e termodinâmica completas.
- A chuva passa por transporte conservativo de vapor nas duas camadas, evaporação de água aberta, transferência vertical, elevação orográfica e condensação. Biomas e rios são calculados depois dessa chuva.

## Controle do terreno e da temperatura

As comparações SHA-256 confirmaram igualdade de elevação, água, relevo, formas geográficas, temperatura, tectônica, IDs e catálogo de regiões entre os modelos. Os 16 hashes do mapa antigo também coincidiram com os resultados do teste anterior de repetibilidade no Atlas máximo.

## Chuva e biomas

| Seed | Modelo | Chuva média em terra | Mediana em terra | Terra com chuva abaixo de 260 mm/ano | Deserto na terra | Florestas na terra |
| --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1234 | Antigo | 637,16 | 491 | 28,08% | 8,99% | 25,52% |
| 1234 | Hadley sem faixas | 311,15 | 30 | 74,52% | 44,05% | 10,36% |
| -77 | Antigo | 520,46 | 404 | 35,63% | 17,18% | 18,58% |
| -77 | Hadley sem faixas | 298,30 | 19 | 76,55% | 44,43% | 9,69% |

Os percentuais usam os hexes terrestres úteis como denominador, incluindo terra polar. Deserto é o bioma Desert final; baixa precipitação também pode resultar em estepe ou tundra conforme temperatura e regras de classificação.

## Conversão da intensidade global

O simulador usa unidades normalizadas. Um teste inicial pequeno produziu chuva bruta insuficiente. Por isso, antes dos biomas e rios, um único multiplicador global usa a média de chuva do modelo antigo como referência de intensidade. O fator é igual em todas as latitudes e longitudes. Não foi ajustado por faixa nem por continente. O teto comum de 5000 mm/ano pode reduzir a média final em relação ao alvo.

| Seed | Média global antiga | Média bruta antes da calibração | Fator único | Média global Hadley após teto | Pontos úteis no teto de 5000 |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1234 | 412,46 | 31,70 | 13,01 | 402,07 | 0,95% |
| -77 | 399,69 | 32,18 | 12,42 | 385,99 | 0,98% |

A coluna bruta é a saída anualizada do protótipo antes dessa conversão de intensidade, não uma previsão meteorológica calibrada. Os valores de chuva mostrados nos mapas já incluem o fator e o teto. A normalização global é uma variável do experimento e precisa ser considerada ao interpretar os resultados.

## Distribuição por latitude

| Seed | Latitude absoluta | Chuva em terra: antigo | Chuva em terra: Hadley | Deserto: antigo | Deserto: Hadley |
| --- | --- | ---: | ---: | ---: | ---: |
| 1234 | 0 a 15 graus | 1115,59 | 649,99 | 0,27% | 37,69% |
| 1234 | 15 a 35 graus | 410,40 | 273,39 | 29,25% | 65,60% |
| 1234 | 35 a 60 graus | 694,96 | 121,71 | 1,00% | 56,56% |
| 1234 | 60 a 90 graus | 133,16 | 65,45 | 0,00% | 0,00% |
| -77 | 0 a 15 graus | 1009,54 | 709,03 | 0,14% | 34,02% |
| -77 | 15 a 35 graus | 312,02 | 305,47 | 55,72% | 68,51% |
| -77 | 35 a 60 graus | 542,47 | 67,75 | 3,19% | 56,61% |
| -77 | 60 a 90 graus | 134,03 | 69,84 | 0,00% | 0,00% |

Norte e sul estão agregados em cada faixa. Os valores de chuva são os valores anuais armazenados pelo mapa, em mm/ano.

## Diferenças entre os mapas finais

| Seed | Terra com chuva diferente | Terra com bioma diferente | Diferença absoluta média de chuva em terra |
| --- | ---: | ---: | ---: |
| 1234 | 99,94% | 65,26% | 563,42 mm/ano |
| -77 | 99,93% | 54,36% | 473,78 mm/ano |

## Tempo e balanço de água

| Seed | Geração antiga completa | Geração Hadley completa | Erro relativo do balanço de vapor |
| --- | ---: | ---: | ---: |
| 1234 | 170,62 s | 163,85 s | 7.398e-07 |
| -77 | 155,95 s | 163,53 s | 8.134e-07 |

Pico acumulado de working set do processo: 7966,80 MiB. Mede o teste com mapas e renderização, não só o simulador.

O transporte de vapor verificou água inicial + evaporação − condensação − água final, com erro relativo menor que 1e-5. O balanço usa as unidades da grade atmosférica antes da conversão global. Houve 240 passos de aquecimento e 240 passos de medição, com passo externo 0,25 e subdivisões para estabilidade. As medições de tempo são locais e pontuais; parte da execução coincidiu com a suíte de regressão.

## Capturas comparativas

### Seed 1234

| Camada | Antigo | Hadley sem faixas |
| --- | --- | --- |
| Terreno | [Abrir](hadley-climate-comparison/seed-1234-old-terrain.png) | [Abrir](hadley-climate-comparison/seed-1234-hadley-no-bands-terrain.png) |
| Chuva | [Abrir](hadley-climate-comparison/seed-1234-old-rain.png) | [Abrir](hadley-climate-comparison/seed-1234-hadley-no-bands-rain.png) |
| Biomas | [Abrir](hadley-climate-comparison/seed-1234-old-biomes.png) | [Abrir](hadley-climate-comparison/seed-1234-hadley-no-bands-biomes.png) |
| Ventos | [Abrir](hadley-climate-comparison/seed-1234-old-wind.png) | [Abrir](hadley-climate-comparison/seed-1234-hadley-no-bands-wind.png) |

### Seed -77

| Camada | Antigo | Hadley sem faixas |
| --- | --- | --- |
| Terreno | [Abrir](hadley-climate-comparison/seed--77-old-terrain.png) | [Abrir](hadley-climate-comparison/seed--77-hadley-no-bands-terrain.png) |
| Chuva | [Abrir](hadley-climate-comparison/seed--77-old-rain.png) | [Abrir](hadley-climate-comparison/seed--77-hadley-no-bands-rain.png) |
| Biomas | [Abrir](hadley-climate-comparison/seed--77-old-biomes.png) | [Abrir](hadley-climate-comparison/seed--77-hadley-no-bands-biomes.png) |
| Ventos | [Abrir](hadley-climate-comparison/seed--77-old-wind.png) | [Abrir](hadley-climate-comparison/seed--77-hadley-no-bands-wind.png) |

## Limites da comparação

Este experimento troca tanto o transporte e a formação da chuva quanto os forçamentos latitudinais; ele não isola o efeito de apenas remover um termo no modelo antigo. A temperatura foi mantida como variável de controle. O vento é um snapshot estacionário de duas camadas: não recebe realimentação do calor liberado pela chuva, não transporta temperatura e não inclui estações, correntes marítimas, evapotranspiração ou reevaporação das gotas. A camada superior usa uma aproximação de temperatura 35 °C abaixo da superfície.

O resultado permite avaliar a distribuição produzida por este protótipo, mas não comprova que o clima real surgiria sem outros componentes. Nenhum ajuste regional foi feito depois de observar as seeds grandes. O clima padrão não foi substituído e o teste não salva mapas .world experimentais.

## Avaliação

Validação de software: build aprovada sem avisos ou erros; teste específico de umidade aprovado; suíte completa GeneratorChecks aprovada; 48 mundos de referência idênticos bit a bit. Esses checks aprovam a execução e os controles do experimento, não a qualidade meteorológica do clima resultante.

Nas duas seeds, a variante Hadley sem faixas produziu muito mais terra seca e desertos, com menos florestas. A mediana da chuva continental caiu para 19 a 30 mm/ano, apesar de a média global ter ficado próxima da referência após o fator único. As capturas mostram chuva concentrada perto do equador e de algumas encostas, com grandes interiores secos.

Eu não substituiria o clima padrão por esta variante. O resultado indica que o protótipo atual de circulação e transporte de umidade precisa de desenvolvimento e calibração; não demonstra que uma faixa artificial de deserto seja necessária em qualquer simulador. Também não permite atribuir o problema a um único componente sem novos testes de controle.

O sistema antigo permanece como padrão. A variante fica restrita ao experimento diagnóstico.

## Reprodução

```text
godot --path . -- --hadley-climate-comparison
dotnet run --project tests/GeneratorChecks -- --hadley-climate-only
```

Os números, hashes e totais de água estão em results/hadley-climate-comparison/summary.json.
