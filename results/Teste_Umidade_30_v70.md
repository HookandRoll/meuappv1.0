# Vapor +30% em todos os tamanhos: v70

Data: 24/11/2026. A Alpha 2.0.7 aplica o mesmo reforço de 30% à capacidade de vapor nos seis tamanhos, tanto em Mundo quanto em Região. O aumento não é progressivo entre tamanhos. A receita v70 preserva o retorno terrestre de 500 mm/25%, a mistura atmosférica e as três células de circulação.

## Capacidade de armazenamento

O multiplicador passa de 1,50 para 1,95: aumento relativo de 30%. A 25 °C e por unidade de massa de ar, o limiar de saturação passa de **45 para 58,5 unidades normalizadas de água**. A capacidade continua variando exponencialmente com a temperatura. As duas camadas usam o mesmo multiplicador, cada uma com sua própria temperatura e massa de ar.

Esse valor é um limiar de condensação. O solver pode conter vapor acima dele temporariamente, pois o excesso condensa gradualmente. A água de nuvem é um reservatório separado, sem novo teto de armazenamento nesta etapa.

## Aporte de vapor

A reposição sobre água depende do déficit entre o alvo de umidade relativa e o vapor presente. Para a mesma temperatura, massa de ar, umidade relativa, superfície e passo de tempo, aumentar a capacidade em 30% já aumenta o aporte em 30%.

O coeficiente de reposição foi mantido. Aumentá-lo também em 30% produziria 1,30 × 1,30 = 1,69 vezes o aporte potencial, contando o reforço duas vezes. A evaporação do solo continua limitada pela água disponível e não promete o mesmo aumento quando a reserva está esgotada.

| Parâmetro | v69 | v70 |
| --- | ---: | ---: |
| Multiplicador da capacidade de vapor | 1,50 | 1,95 |
| Capacidade a 25 °C por unidade de massa de ar | 45 | 58,5 |
| Multiplicador de velocidade da reposição aquática | 2,0 | 2,0 |
| Relaxação aquática efetiva por unidade de tempo | 0,36 | 0,36 |
| Alvos de umidade: oceano profundo / raso | 90% / 95% | 90% / 95% |
| Alvos: lago / outras águas | 85% / 80% | 85% / 80% |
| Umidade relativa inicial | 55% | 55% |
| Retorno terrestre | Acima de 500 mm, 25% | Acima de 500 mm, 25% |
| Mistura de vapor/nuvens | 0,08 | 0,08 |
| Conversão de chuva | 10,5 | 10,5 |

A água atmosférica inicial também cresce 30%, pois a umidade relativa inicial continua em 55%; a reserva inicial do solo não mudou. O novo estoque inicial é incluído no balanço. Durante a simulação, a água adicional entra pelas fontes aquáticas; solo e retorno terrestre transferem água já disponível. O reforço não acrescenta uma fonte independente de água na atmosfera.

## Tamanhos do mapa

A seleção de configuração foi verificada para os seis tamanhos em Mundo e Região. O teste usa manifestos de tamanho real e valida suas dimensões, sem gerar ou alocar todos esses mundos. A mesma configuração 1,95 é selecionada em todos os casos, e os demais parâmetros são iguais à v69.

| Tamanho escolhido | Aumento da capacidade | Aumento do aporte aquático sob condições equivalentes |
| --- | ---: | ---: |
| 128×64 | 30% | 30% |
| 256×128 | 30% | 30% |
| 512×256 | 30% | 30% |
| 1024×512 | 30% | 30% |
| 2048×1024 | 30% | 30% |
| 4096×2048 | 30% | 30% |

As simulações controladas confirmaram armazenamento inicial +30% nas temperaturas -30, 0, 25 e 45 °C, além do aporte +30% em oceano profundo, oceano raso, lago e outras águas, com umidade relativa equivalente. Isso não implica chuva final exatamente 30% maior em cada hex ou em cada tamanho: transporte, calor, condensação e retorno terrestre continuam ativos.

## Chuva no Atlas máximo

Comparação v69/v70 nas seeds 1155827384, 1234 e -77. Referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos, atmosfera 512×256, três continentes, 25% de terra, costa 80, sem offsets. Aquecimento e amostragem de 240 passos cada, passo externo 0,25.

As variantes exigiram hashes idênticos de elevação, água e temperatura de referência. Os CSVs completos da referência v69 também coincidem, byte a byte, com os CSVs v69 do experimento anterior de retorno de chuva nas três seeds.

As médias globais abaixo são aritméticas na grade atmosférica, incluindo água e terra, sem ponderação por área esférica e antes do teto de chuva da geração dos hexes.

| Seed | Média global bruta v69 | v70 | Variação |
| --- | ---: | ---: | ---: |
| 1155827384 | 1130,37 | 1464,03 | +29,52% |
| 1234 | 1082,55 | 1401,53 | +29,47% |
| -77 | 1048,00 | 1354,49 | +29,25% |

Interior: células com maioria de terra, altitude terrestre média até 300 m e distância de pelo menos três células atmosféricas de água. São 9.873, 9.137 e 6.481 células, respectivamente. O limiar 260 é um indicador atmosférico de secura, não uma contagem de desertos finais.

| Seed | Chuva no interior v69 → v70 | Interior abaixo de 260 v69 → v70 |
| --- | ---: | ---: |
| 1155827384 | 1032,92 → 1350,46 | 19,56% → 15,41% |
| 1234 | 1324,97 → 1709,97 | 27,33% → 19,36% |
| -77 | 703,24 → 903,41 | 31,51% → 23,16% |

O reforço também aumenta a chuva em regiões que já eram úmidas. A parcela da grade atmosférica acima de 5000 passa de 1,88% para 3,85%, de 2,43% para 4,15% e de 1,89% para 3,63%, respectivamente. Esse é um indicador bruto antes da interpolação; não representa uma contagem de hexes. O teto de geração de 5000 foi preservado, portanto a média final dos hexes pode ter um ganho menor que a média bruta da grade.

## Biomas finais

Contagens depois do acabamento, dos rios e dos biomas fluviais, sobre todos os hexes terrestres ativos. Denominadores: 3.734.788, 3.843.223 e 3.821.766, respectivamente. Florestas seguem `Biomes.IsForest`, incluindo pântano (`Swamp`), mangue, taiga e as demais florestas. JSONs por variante guardam as contagens de todos os biomas.

| Seed | Receita | Deserto | Badlands | Estepe | Florestas |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v69 | 8,91% | 3,15% | 7,91% | 45,32% |
| 1155827384 | v70 | 7,55% | 2,70% | 4,69% | 52,76% |
| 1234 | v69 | 13,53% | 3,89% | 8,39% | 45,66% |
| 1234 | v70 | 11,79% | 3,15% | 6,00% | 50,72% |
| -77 | v69 | 28,14% | 4,54% | 12,54% | 28,89% |
| -77 | v70 | 25,97% | 3,71% | 11,02% | 33,75% |

Desertos em hexes: 332.642 → 282.122; 520.133 → 453.305; 1.075.473 → 992.431. A geografia e as regras de classificação continuam influenciando o resultado; o aumento não elimina desertos.

## Conservação e compatibilidade

- PASS: configuração +30% em todos os tamanhos e modos; aporte controlado +30% sem aumento adicional do coeficiente de reposição.
- PASS: estoque inicial nas duas camadas, quatro temperaturas, fontes aquáticas, determinismo, conservação e sistema vazio sem criação de água quando não há fonte.
- PASS: criação v70, save/load e regeneração completa reproduzem chuva e biomas.
- PASS: v69 é idêntica ao controle explícito com capacidade 1,50; os CSVs v69 das três seeds máximas coincidem integralmente com o experimento anterior.
- PASS: regressões de retorno de chuva v69, transporte v66/v67, três células, Hadley acoplado e Hadley v64.
- PASS: 48 mundos antigos de referência idênticos bit a bit, incluindo camadas, rios, regiões e ícones.
- PASS: todas as seis execuções máximas conservam água com erro relativo menor que 1,1e-13, abaixo do limite 1e-5. Nenhuma aplicação do limitador térmico de emergência.

## EXE e interface

- PASS: projeto `MapmakerHumidityBoost.csproj` compilado sem avisos ou erros com `-p:NuGetAudit=false`; exportação portátil concluiu com código 0.
- PASS: autoteste do EXE criou v70 pelo formulário, mostrou chuva, biomas e as camadas de vento, salvou e reabriu o mapa e reconstruiu os mesmos hashes de vento. Encerramento com código 0 e stderr vazio.

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.7-Experimental-Umidade-30.exe`, 206.120.760 bytes, versão 2.0.7.0. SHA-256: `36805BAD68D83F54B9F8A30BA951B5AB75DFC54C06E1019A0209FF043F5A235D`.

Novos mapas usam v70 por padrão. Saves v69 mantêm capacidade 1,50 e retorno 500/25; v64–v67 conservam suas receitas. A v68 removida exige a build Alpha 2.0.5.

## Dados e reprodução

Dados por seed em `results/humidity-boost/<seed>/`: JSONs, CSVs, contagens finais de biomas e PNGs. Cada campo usa uma escala compartilhada entre v69 e v70 da mesma seed. Os PNGs mostram a grade atmosférica, sem a projeção curva do Atlas.

| Chuva v69, seed 1155827384 | Chuva v70 |
| --- | --- |
| ![Chuva v69](humidity-boost/1155827384/v69-rain.png) | ![Chuva v70](humidity-boost/1155827384/v70-rain.png) |

```powershell
dotnet run -c Release --project tests/GeneratorChecks -- --humidity-boost-only
dotnet run -c Release --project tests/GeneratorChecks -- --humidity-boost-review
python tests/GeneratorChecks/render_climate_diagnostics.py results/humidity-boost/1155827384 results/humidity-boost/1234 results/humidity-boost/-77
dotnet run --project tests/CompatibilityChecks
```

Logs: `humidity-boost-checks.log`, `humidity-boost-1155827384.log`, `humidity-boost-other-seeds.log`, `humidity-boost-compatibility.log`, `humidity-boost-build.log`, `humidity-boost-export.log` e `humidity-boost-ui.log`.

O comparativo usa a duração padrão, não uma nova demonstração de estabilização de longo prazo. A mistura atmosférica continua ativa; o espalhamento legado por hexes continua desativado. O halo de rios usado na classificação de biomas não foi alterado. Nenhum push ou release no GitHub foi realizado.
