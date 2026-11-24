# Comparativo da reposição de umidade

Seeds 1234 e −77 no Atlas máximo: 6144×2784, 15.044.776 hexes ativos e atmosfera 512×256 com duas camadas. Os mapas novos foram gerados neste ensaio; a referência é o ensaio acoplado anterior arquivado. Os oito hashes de controle confirmam terreno e temperatura anual iguais. A comparação altera os alvos e a força de reposição, mantendo os demais processos do modelo.

Antes: fontes aquáticas até 80%, solo até 75%, taxa aquática 0,18. Agora: oceano 90%, oceano raso até 200 m 95%, lagos 85%, demais fontes e solo 80%, taxa aquática efetiva 0,36. Pesos por área permanecem 1,0 para oceano, 0,6 para lago e 0,4 para área úmida.

## Chuva e biomas

O fator de conversão de chuva do ensaio anterior foi congelado por seed **antes da execução nova**. A média global nova não é forçada a voltar à média antiga. O teto de 5000 permanece aplicado. Os valores anuais usam a escala do gerador; o modelo ainda não tem calibração meteorológica.

| Seed | Versão | Chuva global média | Chuva terrestre média | Mediana terrestre | Terra com chuva <260 | Deserto | Floresta |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | Anterior | 412.5 | 189.4 | 125 | 75.82% | 40.37% | 1.50% |
| 1234 | Reposição nova | 734.0 | 423.7 | 263 | 49.53% | 23.74% | 13.88% |
| -77 | Anterior | 399.7 | 141.6 | 80 | 84.01% | 47.94% | 1.37% |
| -77 | Reposição nova | 701.1 | 286.3 | 129 | 70.14% | 41.08% | 8.02% |

## Água que entra e chuva produzida

Totais brutos nas duas fases (estabilização e amostragem), com duração e grade iguais. A evaporação aquática é uma entrada externa; evapotranspiração e precipitação terrestre são transferências internas.

| Seed | Evaporação aquática anterior | Nova | Variação | Precipitação bruta anterior | Nova | Variação |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | 1715491.0 | 3008073.2 | +75.3% | 1316918.5 | 2444900.4 | +85.7% |
| -77 | 1734339.3 | 2996659.0 | +72.8% | 1350822.2 | 2461136.4 | +82.2% |

Os fluxos acumulados acima são diferentes do estoque que permanece no ar ao fim do teste:

| Seed | Vapor final anterior | Novo | Água de nuvem final anterior | Nova |
| ---: | ---: | ---: | ---: | ---: |
| 1234 | 1680181.1 | 1823874.1 | 39558.4 | 70303.1 |
| -77 | 1670506.6 | 1805895.7 | 40266.2 | 70109.6 |

## Distribuição por latitude

| Seed | Latitude absoluta | Chuva terrestre anterior | Nova |
| ---: | --- | ---: | ---: |
| 1234 | 0 a 15 graus | 240.0 | 676.0 |
| 1234 | 15 a 35 graus | 201.8 | 405.3 |
| 1234 | 35 a 60 graus | 178.3 | 318.4 |
| 1234 | 60 a 90 graus | 97.3 | 173.3 |
| -77 | 0 a 15 graus | 179.1 | 445.7 |
| -77 | 15 a 35 graus | 177.4 | 339.0 |
| -77 | 35 a 60 graus | 105.1 | 172.8 |
| -77 | 60 a 90 graus | 87.3 | 153.2 |

## Vapor e chuva no interior

Vapor é o estoque médio durante a amostragem, somando as duas camadas. As distâncias são células da grade atmosférica reduzida, sem conversão para quilômetros. São consideradas células com maioria terrestre, sem ponderação pela área física do planeta. Os estoques não identificam a origem da água: ar e solo já começam com água nos dois ensaios.

| Seed | Distância da água | Vapor anterior | Novo | Chuva bruta anterior | Nova |
| ---: | --- | ---: | ---: | ---: | ---: |
| 1234 | 0–2 células da grade | 14.937 | 16.208 | 29.092 | 68.335 |
| 1234 | 3–8 células da grade | 13.408 | 14.405 | 14.212 | 33.648 |
| 1234 | >8 células da grade | 12.762 | 13.194 | 9.497 | 14.194 |
| -77 | 0–2 células da grade | 12.570 | 13.599 | 26.940 | 64.591 |
| -77 | 3–8 células da grade | 10.946 | 11.630 | 14.103 | 28.088 |
| -77 | >8 células da grade | 13.395 | 13.701 | 6.607 | 8.883 |

## Conservação, estabilidade e tempo

| Seed | Tempo novo (s) | Fator de chuva fixo | Erro relativo de água | Erro relativo de ar | Limitações térmicas |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1234 | 292.36 | 10.728206 | 2.754e-13 | 3.577e-09 | 0 |
| -77 | 266.60 | 10.213314 | 2.983e-13 | 1.661e-08 | 0 |

Pico de memória do processo: 5785.8 MiB.

Balanço: água inicial + evaporação aquática − escoamento = água final em vapor, nuvens e solo. Os estoques iniciais são idênticos aos do ensaio anterior. Os tempos medem geração completa em Debug com a interface do Godot; os ensaios não constituem um benchmark isolado de hardware.

## Leitura dos resultados

- Seed 1234: chuva terrestre média +123.7%; chuva bruta no interior (>8 células) +49.5%; razão equatorial (0–15°) / latitude média (35–60°) 1.35 → 2.12.
- Seed -77: chuva terrestre média +102.1%; chuva bruta no interior (>8 células) +34.4%; razão equatorial (0–15°) / latitude média (35–60°) 1.70 → 2.58.

A reposição foi alterada em conjunto com os alvos; o ensaio mede o efeito combinado, sem atribuir cada resultado a um único parâmetro. Estoque de vapor, água de nuvem e volume precipitado precisam ser lidos separadamente. O aumento de chuva não comprova que o clima inteiro está calibrado. O gradiente térmico equador–polos segue prescrito, e o modelo ainda não resolve fisicamente todas as frentes e tempestades de latitude média.

## Capturas

### Seed 1234

[Chuva anterior](hadley-coupled-climate-comparison/seed-1234-hadley-no-bands-rain.png) · [Chuva nova](hadley-replenishment-comparison/seed-1234-rain.png) · [Biomas anteriores](hadley-coupled-climate-comparison/seed-1234-hadley-no-bands-biomes.png) · [Biomas novos](hadley-replenishment-comparison/seed-1234-biomes.png) · [Vento no solo](hadley-replenishment-comparison/seed-1234-surface-wind.png) · [Vento em altitude](hadley-replenishment-comparison/seed-1234-upper-wind.png)

### Seed -77

[Chuva anterior](hadley-coupled-climate-comparison/seed--77-hadley-no-bands-rain.png) · [Chuva nova](hadley-replenishment-comparison/seed--77-rain.png) · [Biomas anteriores](hadley-coupled-climate-comparison/seed--77-hadley-no-bands-biomes.png) · [Biomas novos](hadley-replenishment-comparison/seed--77-biomes.png) · [Vento no solo](hadley-replenishment-comparison/seed--77-surface-wind.png) · [Vento em altitude](hadley-replenishment-comparison/seed--77-upper-wind.png)

## Repetir

```powershell
dotnet build Mapmaker.csproj --no-restore
dotnet run --project tests/GeneratorChecks -- --hadley-climate-only
.\Godot_v4.7.2-stable_mono_win64_console.exe --path . -- --hadley-replenishment-comparison
```

O comando exige a referência arquivada e preserva seus dados. O gerador padrão e seus saves não são alterados. O comando --hadley-climate-comparison continua sendo o teste separado que normaliza a média global.
