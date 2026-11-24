# Histórico térmico do ar sobre o relevo

O experimento agora transporta o estado térmico superficial corrigido pela altitude: `massa × (T_kelvin + Γ_seco × altitude_km)`. A temperatura local é reconstruída desse estado. Subir resfria o ar, descer o aquece, e a água que precipitou não reaparece automaticamente no vapor. A taxa seca de referência é 9,8 °C/km. Condensação, aquecimento latente, nuvens, reevaporação, solo e vento continuam acoplados.

## Cordilheira controlada

Grade 128×32, fronteira periódica, oceano dos dois lados, cordilheira simétrica, vento zonal prescrito de módulo 1,2 e trocas térmicas parametrizadas com o exterior desligadas. A imposição de vento serve apenas para isolar a termodinâmica; os mapas completos usam ventos dinâmicos.

| Modelo | Chuva média na encosta de entrada | Chuva média no lado protegido |
| --- | ---: | ---: |
| Atalho anterior | 0.000 | 0.000 |
| Histórico térmico novo | 341.758 | 38.354 |

Razão entrada/protegido no novo modelo: 8.91. Ao inverter o vento, o padrão de chuva se inverteu. Os checks também verificaram perfil adiabático seco, ar parado, conversão do estado térmico entre solo e altitude sobre planície e pico, ablação e conservação de água. O modelo anterior não sustentou precipitação nessa coluna sem convergência vertical: o resfriamento local temporário não acumulava o efeito da subida. Isso não significa que o modelo anterior não produzia chuva nos mapas completos.

[Dados e perfis da coluna](hadley-orographic-column/summary.json)

## Mesmas seeds no Atlas máximo

Foram gerados dois Atlas novos de 6144×2784 (15.044.776 hexes ativos), atmosfera 512×256 e duas camadas. A referência é o ensaio de reposição mais forte arquivado. Mantêm-se os alvos 90/95/85/80%, a reposição aquática efetiva 0,36, as condições iniciais de água e o fator de conversão da chuva por seed. O fator não foi recalculado para igualar as médias globais. O teto comum de 5000 permanece. A chuva anual está na escala do gerador, sem calibração meteorológica.

| Seed | Versão | Chuva global média | Chuva terrestre média | Mediana terrestre | Terra com chuva <260 | Deserto | Floresta |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | Anterior | 734.0 | 423.7 | 263 | 49.53% | 23.74% | 13.88% |
| 1234 | Histórico térmico | 741.6 | 467.6 | 270 | 48.76% | 24.61% | 16.38% |
| -77 | Anterior | 701.1 | 286.3 | 129 | 70.14% | 41.08% | 8.02% |
| -77 | Histórico térmico | 711.7 | 331.3 | 132 | 67.87% | 40.10% | 9.66% |

## Chuva por latitude

| Seed | Latitude absoluta | Anterior | Novo |
| ---: | --- | ---: | ---: |
| 1234 | 0 a 15 graus | 676.0 | 775.2 |
| 1234 | 15 a 35 graus | 405.3 | 449.3 |
| 1234 | 35 a 60 graus | 318.4 | 325.5 |
| 1234 | 60 a 90 graus | 173.3 | 174.6 |
| -77 | 0 a 15 graus | 445.7 | 584.1 |
| -77 | 15 a 35 graus | 339.0 | 367.6 |
| -77 | 35 a 60 graus | 172.8 | 178.3 |
| -77 | 60 a 90 graus | 153.2 | 155.5 |

## Vapor e chuva no interior

Estoques médios durante a amostragem nas duas camadas, com células de maioria terrestre. As distâncias são células da grade reduzida e não quilômetros. Esses estoques não identificam a origem oceânica da água, pois a atmosfera e o solo já começam com água. A coluna controlada e as estatísticas do Atlas são testes diferentes.

| Seed | Distância da água | Vapor anterior | Novo | Chuva bruta anterior | Nova |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1234 | 0–2 células da grade | 16.208 | 16.286 | 68.335 | 70.538 |
| 1234 | 3–8 células da grade | 14.405 | 14.062 | 33.648 | 40.740 |
| 1234 | >8 células da grade | 13.194 | 13.063 | 14.194 | 14.186 |
| -77 | 0–2 células da grade | 13.599 | 14.247 | 64.591 | 73.950 |
| -77 | 3–8 células da grade | 11.630 | 11.425 | 28.088 | 34.713 |
| -77 | >8 células da grade | 13.701 | 13.321 | 8.883 | 8.988 |

## Conservação e execução

| Seed | Geração nova (s) | Fator fixo | Erro de água | Erro de ar | Limitações térmicas | RMS térmico (°C) |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | 286.19 | 10.728206 | 2.608e-13 | 2.658e-09 | 0 | 6.616 |
| -77 | 269.09 | 10.213314 | 2.863e-13 | 1.132e-08 | 0 | 6.531 |

Pico de memória do processo: 5676.5 MiB.

Os oito hashes de controle (elevação, água, relevo, formas geográficas, temperatura anual, tectônica e regiões) são idênticos à referência. Os ventos foram calculados novamente. Tempos em Debug com interface do Godot, sem representar um benchmark isolado de hardware.

## Limites

O novo transporte representa o histórico térmico e conserva a água, mas não torna o protótipo um modelo atmosférico completo. A troca entre camadas mantém a parametrização anterior de ±6 °C; não há uma nova taxa saturada calibrada. Trocas radiativas, calor latente, evaporação e pressão continuam parametrizados. Não há uma asserção de conservação da energia total. A altitude média da grade reduzida não resolve todas as cordilheiras estreitas. A temperatura anual usada pelos biomas permanece a do controle. Não foram alterados frentes, temperatura própria dos oceanos, correntes marítimas ou a regra de residência das nuvens.

## Capturas

### Seed 1234

[Chuva anterior](hadley-replenishment-comparison/seed-1234-rain.png) · [Chuva nova](hadley-orographic-comparison/seed-1234-rain.png) · [Biomas anteriores](hadley-replenishment-comparison/seed-1234-biomes.png) · [Biomas novos](hadley-orographic-comparison/seed-1234-biomes.png) · [Vento no solo](hadley-orographic-comparison/seed-1234-surface-wind.png) · [Vento em altitude](hadley-orographic-comparison/seed-1234-upper-wind.png)

### Seed -77

[Chuva anterior](hadley-replenishment-comparison/seed--77-rain.png) · [Chuva nova](hadley-orographic-comparison/seed--77-rain.png) · [Biomas anteriores](hadley-replenishment-comparison/seed--77-biomes.png) · [Biomas novos](hadley-orographic-comparison/seed--77-biomes.png) · [Vento no solo](hadley-orographic-comparison/seed--77-surface-wind.png) · [Vento em altitude](hadley-orographic-comparison/seed--77-upper-wind.png)

## Repetir

```powershell
dotnet build Mapmaker.csproj --no-restore
dotnet run --project tests/GeneratorChecks -- --hadley-climate-only
.\Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --hadley-orographic-comparison
```

O comparador exige a referência de reposição já concluída e preserva seus dados. O gerador padrão e os saves continuam sem alteração. A simulação detalhada pertence ao caminho experimental.
