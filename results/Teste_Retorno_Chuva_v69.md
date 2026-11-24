# Retorno da chuva terrestre às nuvens: v69

Data: 24/11/2026. A implementação v68 de vegetação e raízes foi removida. A Alpha 2.0.6 usa a receita v69, com **limiar de 500 mm/ano e retorno de 25%** da nova chuva terrestre às nuvens inferiores.

## Funcionamento e balanço

A regra usa a média anualizada da chuva recebida desde o início da simulação, incluindo o aquecimento. O limiar é estrito: a média precisa superar 500 mm/ano. A conversão para as unidades de chuva do mapa inclui o fator 10,5, o teto de 5000 e o offset de precipitação.

Em cada passo elegível, o retorno é `chuva do passo × fração terrestre × 0,25`. Essa água é retirada da infiltração e adicionada às nuvens da camada inferior. As nuvens podem ser transportadas pelos ventos de solo, trocar água com vapor e com a camada superior e precipitar novamente. O retorno não retira água de reservas fictícias nem depende dos biomas finais.

A chuva exibida continua sendo a precipitação recebida antes do retorno. A mesma água pode chover mais de uma vez, mas não fica simultaneamente no solo e na nuvem. O balanço verificado é água inicial + evaporação aquática = atmosfera + solo + escoamento. O retorno é uma transferência interna, não uma entrada adicional.

Este é um retorno direto parametrizado de água líquida às nuvens. Não simula o processo completo de evaporação nem transporte subterrâneo. A propagação horizontal ocorre pelo transporte atmosférico já existente. A evaporação comum do solo da v67 foi preservada; cobertura vegetal e armazenamento de raízes foram excluídos.

## Comparativo de limiar e fração

Seeds 1155827384, 1234 e -77, Atlas máximo: referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos, atmosfera 512×256, três continentes, 25% de terra, costa 80, sem offsets. Hadley/Ferrel/Polar permanecem em 1,0/0,8/0,6. Aquecimento e amostragem de 240 passos cada, passo externo 0,25.

Cada combinação foi simulada sobre a mesma superfície da referência v67. As gerações completas v67/v69 também exigiram hashes idênticos de elevação, água e temperatura de referência. O CSV completo da v69 escolhida é idêntico, byte a byte, ao controle 500/25 nas três seeds.

Interior: células atmosféricas com maioria de terra, altitude terrestre média até 300 m e distância de pelo menos três células de água. Amostras: 9.873 células para 1155827384, 9.137 para 1234 e 6.481 para -77. Secura significa chuva abaixo de 260; não equivale à classificação final de deserto.

As médias são aritméticas na grade atmosférica, sem ponderação por área esférica. A média global inclui água e terra. A coluna acima de 5000 mede células da grade com chuva bruta acima do teto usado na geração dos hexes, antes da interpolação; não é um percentual de hexes finais.

| Seed | Limiar / retorno | Chuva no interior | Interior seco | Média global bruta | Grade acima de 5000 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v67, sem retorno | 969,51 | 20,68% | 1073,25 | 1,47% |
| 1155827384 | **500 / 25%** | **1032,92** | **19,56%** | **1130,37** | **1,88%** |
| 1155827384 | 500 / 50% | 1127,99 | 18,57% | 1219,37 | 2,45% |
| 1155827384 | 750 / 25% | 1021,16 | 19,95% | 1126,47 | 1,87% |
| 1155827384 | 750 / 50% | 1096,57 | 18,94% | 1209,22 | 2,43% |
| 1234 | v67, sem retorno | 1213,41 | 28,39% | 1017,62 | 1,90% |
| 1234 | **500 / 25%** | **1324,97** | **27,33%** | **1082,55** | **2,43%** |
| 1234 | 500 / 50% | 1480,58 | 26,11% | 1181,10 | 3,18% |
| 1234 | 750 / 25% | 1319,53 | 27,51% | 1079,73 | 2,43% |
| 1234 | 750 / 50% | 1466,34 | 26,30% | 1174,01 | 3,16% |
| -77 | v67, sem retorno | 649,84 | 32,06% | 999,23 | 1,53% |
| -77 | **500 / 25%** | **703,24** | **31,51%** | **1048,00** | **1,89%** |
| -77 | 500 / 50% | 783,21 | 30,64% | 1123,50 | 2,37% |
| -77 | 750 / 25% | 697,12 | 31,60% | 1045,80 | 1,89% |
| -77 | 750 / 50% | 768,99 | 30,91% | 1118,28 | 2,36% |

500/25 melhorou a média e reduziu a fração seca do interior nas três geografias. Foi escolhido como ponto de partida moderado: a média global bruta cresce aproximadamente 4,9–6,4%, contra 12,4–16,1% em 500/50. O retorno de 50% tem maior alcance no interior, mas também amplia mais a chuva já elevada. Esta escolha não estabelece uma calibração meteorológica definitiva.

## Biomas finais da receita escolhida

Contagens feitas depois do acabamento de biomas, da geração dos rios e dos biomas fluviais. A referência e a receita escolhida foram geradas integralmente; as outras três combinações intermediárias comparam somente a atmosfera.

Percentuais sobre os hexes terrestres ativos: 3.734.788 para 1155827384, 3.843.223 para 1234 e 3.821.766 para -77. Florestas seguem a classificação existente `Biomes.IsForest`, incluindo pântanos (`Swamp`), mangue, taiga e as demais florestas. JSONs por variante preservam as contagens de todos os biomas.

| Seed | Receita | Deserto | Badlands | Estepe | Florestas |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v67 | 10,02% | 3,63% | 8,45% | 42,52% |
| 1155827384 | v69, 500/25 | 8,91% | 3,15% | 7,91% | 45,32% |
| 1234 | v67 | 14,63% | 4,32% | 8,39% | 43,90% |
| 1234 | v69, 500/25 | 13,53% | 3,89% | 8,39% | 45,66% |
| -77 | v67 | 28,89% | 4,70% | 12,67% | 27,38% |
| -77 | v69, 500/25 | 28,14% | 4,54% | 12,54% | 28,89% |

Desertos em hexes: 374.271 → 332.642 na seed 1155827384, 562.222 → 520.133 na seed 1234 e 1.104.139 → 1.075.473 na seed -77. Os ganhos dependem da geografia; o retorno não elimina as áreas secas.

## Conservação e propagação

- PASS: exatamente no limiar e abaixo dele, retorno zero; proporções de 25%/50%, exclusão de água oceânica e ponderação de costas mistas verificadas.
- PASS: limiar em unidades anuais, independente da quantidade de chuva de um único passo; offset de precipitação respeitado; histórico úmido não cria retorno quando não chega nova chuva.
- PASS: pulso controlado em vento fixo avança de alcance médio 1,019 para 1,371 células a jusante ao retornar 50% com limiar zero. O retorno acumulado é exatamente metade da precipitação recebida nesse caso totalmente terrestre, sem quebrar o balanço.
- PASS: sistema sem água e sem fonte aquática continua vazio; conservação, positividade, determinismo, parâmetros inválidos, criação, save/load e regeneração completa da v69.
- PASS: todas as execuções aceitas da matriz têm erro relativo de água menor que 1,1e-13, abaixo do limite 1e-5. Nenhuma aplicação do limitador térmico de emergência.

O pulso usa limiar zero para isolar o mecanismo de propagação; a receita do aplicativo usa 500 mm/ano e 25%.

## Compatibilidade

- PASS: v67 permanece idêntica ao controle com retorno desativado. Testes anteriores de transporte de nuvens, transporte v66, três células, Hadley acoplado e Hadley v64 passaram.
- PASS: 48 mundos de referência antigos continuam idênticos bit a bit, incluindo todas as camadas, rios, regiões e ícones.
- PASS: v68 removida é recusada explicitamente, evitando reinterpretar seus saves como v69. A build Alpha 2.0.5 foi mantida em `dist` para abrir esses experimentos antigos.

## Interface e EXE

- PASS: compilação do projeto `MapmakerRainRecycling.csproj` sem avisos ou erros com `-p:NuGetAudit=false` e exportação portátil concluída com código 0.
- PASS: EXE criou a receita v69 pelo formulário normal, mostrou chuva/biomas e as duas camadas de vento, salvou e reabriu o mapa e reconstruiu os mesmos hashes de vento. O autoteste encerrou com código 0 e stderr vazio.

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.6-Experimental-Retorno-Chuva.exe`, 206.120.760 bytes. SHA-256: `C259F70778193FE20FCCD052C912036A1C41AC61EDE75397ACCC2583F6337D30`. A nova receita é o padrão; não exige argumento de inicialização.

Logs principais: `rain-return-checks.log`, `rain-return-selected-checks.log`, `rain-return-1155827384.log`, `rain-return-other-seeds.log`, `rain-return-selected.log`, `rain-return-selected--77.log`, `rain-return-compatibility.log`, `rain-return-export.log` e `rain-return-ui.log`.

## Diagnósticos e limites

Dados em `results/rain-return/<seed>/`: `summary.json`, `atmosphere.json`, CSVs por célula, contagens de biomas, PNGs e escalas. Os PNGs de cada campo compartilham a escala entre variantes da mesma seed. Vapor, nuvens, retorno e evaporações representam a janela de amostragem; solo representa o estado final.

Exemplo da seed 1155827384:

| Chuva v67 | Chuva v69 |
| --- | --- |
| ![Chuva v67](rain-return/1155827384/v67-rain.png) | ![Chuva v69](rain-return/1155827384/v69-rain.png) |

| Retorno às nuvens v69 | Água do solo v69 |
| --- | --- |
| ![Retorno às nuvens](rain-return/1155827384/v69-rainReturn.png) | ![Água do solo](rain-return/1155827384/v69-soil.png) |

Retorno e solo são campos diferentes e têm escalas próprias. As imagens mostram a grade atmosférica, com norte na primeira linha, sem a projeção curva do Atlas.

O limiar atua na grade atmosférica que alimenta os hexes, não em cada hex final isoladamente. A média acumulada usada para ativar o retorno pode diferir da chuva da janela final, pois esta exclui o aquecimento. Nenhum bioma é lido para decidir o retorno, e não há difusão de água subterrânea entre hexes.

Esta etapa compara a duração padrão de 240 + 240 passos; não demonstra estabilização de longo prazo da nova receita. Nenhum push ou release no GitHub foi realizado.
