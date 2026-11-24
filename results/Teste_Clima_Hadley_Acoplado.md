# Teste do clima Hadley acoplado

Comparação das seeds 1234 e −77 no Atlas máximo (6144×2784; grade atmosférica 512×256). O clima padrão foi gerado novamente e comparado com a simulação acoplada. Os números do vento congelado vêm do ensaio anterior, cujos mundos de controle foram conferidos por hash.

| Seed | Modelo | Chuva terrestre média | Mediana | Terra com chuva <260 | Deserto | Floresta |
| ---: | --- | ---: | ---: | ---: | ---: | ---: |
| 1234 | Padrão | 637.2 | 491 | 28.08% | 8.99% | 25.52% |
| 1234 | Hadley congelado | 311.1 | 30 | 74.52% | 44.05% | 10.36% |
| 1234 | Hadley acoplado | 189.4 | 125 | 75.82% | 40.37% | 1.50% |
| -77 | Padrão | 520.5 | 404 | 35.63% | 17.18% | 18.58% |
| -77 | Hadley congelado | 298.3 | 19 | 76.55% | 44.43% | 9.69% |
| -77 | Hadley acoplado | 141.6 | 80 | 84.01% | 47.94% | 1.37% |

## Chuva por latitude

| Seed | Latitude absoluta | Padrão | Congelado | Acoplado |
| ---: | --- | ---: | ---: | ---: |
| 1234 | 0 a 15 graus | 1115.6 | 650.0 | 240.0 |
| 1234 | 15 a 35 graus | 410.4 | 273.4 | 201.8 |
| 1234 | 35 a 60 graus | 695.0 | 121.7 | 178.3 |
| 1234 | 60 a 90 graus | 133.2 | 65.4 | 97.3 |
| -77 | 0 a 15 graus | 1009.5 | 709.0 | 179.1 |
| -77 | 15 a 35 graus | 312.0 | 305.5 | 177.4 |
| -77 | 35 a 60 graus | 542.5 | 67.8 | 105.1 |
| -77 | 60 a 90 graus | 134.0 | 69.8 | 87.3 |

## Vapor, nuvens e solo

Vapor e nuvens são estoques médios durante a amostragem, somados nas duas camadas. Chuva é um fluxo acumulado e convertido para a saída anual experimental. Os valores abaixo são brutos, anteriores ao fator global de chuva, e usam células com maioria terrestre. As distâncias contam passos na grade reduzida, com bordas coerentes com a simulação; não são quilômetros.

| Seed | Distância da água | Células | Vapor médio | Nuvem média | Chuva bruta |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1234 | 0–2 coarse cells | 8560 | 14.937 | 0.238 | 29.092 |
| 1234 | 3–8 coarse cells | 14837 | 13.408 | 0.116 | 14.212 |
| 1234 | >8 coarse cells | 9475 | 12.762 | 0.078 | 9.497 |
| -77 | 0–2 coarse cells | 6279 | 12.570 | 0.220 | 26.940 |
| -77 | 3–8 coarse cells | 11695 | 10.946 | 0.115 | 14.103 |
| -77 | >8 coarse cells | 14862 | 13.395 | 0.054 | 6.607 |

## Conservação e execução

Balanço: água inicial + evaporação das superfícies aquáticas − escoamento = vapor + nuvem + solo finais. Precipitação terrestre e evapotranspiração transferem água entre reservatórios internos. Oceanos, lagos e áreas úmidas são fontes externas renováveis.

| Seed | Padrão (s) | Acoplado (s) | Fator global | Erro relativo de água | Reciclagem do solo | RMS térmico (°C) | Limitações de temperatura |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | 173.29 | 373.82 | 10.728 | 3.754e-13 | 116930.6 | 6.444 | 0 |
| -77 | 163.32 | 277.42 | 10.213 | 4.039e-13 | 119496.5 | 6.511 | 0 |

Pico de memória do processo: 5994.3 MiB.

Os tempos medem a geração completa em Debug. Houve checks concorrentes, sobretudo durante o primeiro caso; os valores registram esta execução e não constituem um benchmark de desempenho isolado.

## Interpretação e limites

A simulação transporta calor, vapor e nuvens e realimenta o vento a cada subpasso. A camada superior tem temperatura própria; nuvens precipitam gradualmente e podem reevaporar. O solo começa com um estoque finito e recicla precipitação. Os testes de ablação confirmaram que desligar evolução térmica, reciclagem ou reduzir residência das nuvens altera os processos correspondentes.

O resultado precisa distinguir espalhar umidade de produzir chuva suficiente. Comparação com o vento congelado:

- Seed 1234: razão de chuva terrestre 0–15° / 35–60° passou de 5.34 para 1.35; mediana de 30 para 125; média de 311.1 para 189.4.
- Seed -77: razão de chuva terrestre 0–15° / 35–60° passou de 10.46 para 1.70; mediana de 19 para 80; média de 298.3 para 141.6.

Há vapor no interior na simulação, mas isso não garante condensação e precipitação ali. O estoque inicial de vapor é 55% da capacidade e o solo começa com 12 unidades: estes mapas de estoque não isolam a origem oceânica. Um check separado começa com ar e solo secos e verifica que água evaporada da costa alcança células terrestres. A intensidade terrestre ainda precisa ser avaliada junto à circulação vertical e à partição entre chuva no oceano e no continente. O experimento está disponível para comparação; estes resultados não justificam substituir o clima padrão.

O ensaio mantém o gradiente prescrito equador–polos, sem a faixa seca subtropical antiga e sem os três cinturões prescritos. A circulação variável ainda não resolve um modelo físico completo de frentes e tempestades de latitude média. O balanço de água não comprova conservação de energia ou realismo meteorológico. Capacidades, fontes, taxas e duração sazonal usam unidades normalizadas. Aplica-se um único fator global, determinado pela média do clima padrão, antes do teto de 5000; nenhum ajuste depende da seed, região ou latitude. A temperatura anual do mapa e dos biomas permanece controlada; as temperaturas atmosféricas evoluem apenas no experimento.

A atmosfera superior nova começa 25 °C abaixo da superfície, enquanto o ensaio congelado usava −35 °C fixos. A comparação completa reúne essas mudanças; as ablações pequenas verificam mecanismos, sem atribuir a melhora ou piora dos Atlas a uma única causa.

Validação: build do aplicativo com zero avisos/erros; testes de conservação, determinismo, ablações, cancelamento e 512×256; checks do vento de duas camadas; 48 mundos de referência idênticos. A verificação NuGet de vulnerabilidades emitiu NU1900 por indisponibilidade da rede durante os checks de compatibilidade; os 48 checks passaram.

## Capturas

### Seed 1234

[Chuva padrão](hadley-coupled-climate-comparison/seed-1234-old-rain.png) · [Chuva acoplada](hadley-coupled-climate-comparison/seed-1234-hadley-no-bands-rain.png) · [Biomas acoplados](hadley-coupled-climate-comparison/seed-1234-hadley-no-bands-biomes.png) · [Vento acoplado](hadley-coupled-climate-comparison/seed-1234-hadley-no-bands-wind.png)

### Seed -77

[Chuva padrão](hadley-coupled-climate-comparison/seed--77-old-rain.png) · [Chuva acoplada](hadley-coupled-climate-comparison/seed--77-hadley-no-bands-rain.png) · [Biomas acoplados](hadley-coupled-climate-comparison/seed--77-hadley-no-bands-biomes.png) · [Vento acoplado](hadley-coupled-climate-comparison/seed--77-hadley-no-bands-wind.png)

## Repetir

```powershell
dotnet build Mapmaker.csproj --no-restore
dotnet run --project tests/GeneratorChecks -- --hadley-climate-only
.\Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --hadley-climate-comparison
```

Esse comando salva capturas e summary.json em results/hadley-coupled-climate-comparison e encerra o aplicativo ao concluir. A comparação é diagnóstica; o formulário normal e a regeneração continuam com o clima padrão. Não são salvos arquivos .world com o manifesto do gerador padrão para representar mapas experimentais.
