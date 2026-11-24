# Chuva nos hexes: centro 75%, seis vizinhos 25% cada

Data: 24/11/2026. A Alpha 2.0.8 usa a receita v71. Ela mantém a atmosfera da v70 e acrescenta uma passagem sobre a chuva interpolada para os hexes, antes de classificar biomas e gerar rios.

## Regra implementada

Cada hex mantém 75% da sua chuva-base e recebe 25% da chuva-base de cada vizinho imediato. Todos leem o mesmo campo original, sem reutilizar contribuições recém-recebidas. O alcance é um anel de um hex, igualmente em todos os tamanhos.

Um impulso isolado de 1000 mm produz 750 mm no centro e 250 mm em cada um dos seis vizinhos. Os sete valores somam 2250 mm. Numa região uniforme com 1000 mm de base, cada hex interior termina com 2250 mm antes do teto, pois também recebe chuva dos seus vizinhos.

Os pesos somam **225%**, não 100%. Portanto, este é um bônus de geração que amplia o campo final de chuva. Não representa uma redistribuição conservativa da água da atmosfera. O solver conserva sua própria água, mas a chuva adicional aplicada aos hexes não é retirada de suas nuvens.

O teto anterior foi preservado: cada valor-base é limitado a 5000; a soma ponderada também é limitada a 5000; então é aplicado o offset de precipitação. O arredondamento para o campo inteiro de chuva ocorre ao final. Contribuições sobrepostas podem atingir o teto em vez de receber todo o ganho de 125%.

Oceanos, lagos e terra participam desse campo. Vizinhos fora da área ativa do Atlas ou de bordas fechadas são ignorados, sem redistribuir seus pesos. O wrap segue a opção horizontal do manifesto. Não há bloqueio de altitude nesta passagem.

O retorno 500 mm/25%, o vapor +30%, as duas camadas e as três células continuam no solver. O retorno usa a chuva simulada anterior ao bônus; a chuva adicional do campo final não é reinjetada nas nuvens. O halo de rios usado na classificação de biomas também foi preservado.

## Verificação dos pesos, limites e regeneração

- PASS: impulso de 1000 gera exatamente 750 no centro e seis contribuições de 250; somente os sete hexes recebem valores positivos.
- PASS: região uniforme recebe 225% no interior antes do teto; a fonte permanece imutável, sem cascata.
- PASS: bordas fechadas e wrap horizontal respeitados. Num canto fechado, com apenas dois vizinhos válidos, o impulso totaliza 1250 e mantém 750 no centro.
- PASS: valores artificiais fora do contorno ativo do Atlas não vazam para os hexes válidos.
- PASS: geração normal aplica o espalhamento e o teto; terreno, água, temperatura de referência e vento permanecem iguais à v70 no cenário controlado.
- PASS: save/load e regeneração completa reproduzem chuva e biomas; regeneração regional respeita seleção e bloqueios, sem expandir novamente a chuva já processada.

## Comparação no tamanho máximo

Seeds 1155827384, 1234 e -77. Referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos, atmosfera 512×256, três continentes, terra 25%, costa 80 e offsets zero. Aquecimento e amostragem de 240 passos cada, passo externo 0,25.

As variantes exigiram hashes idênticos de elevação, água e temperatura de referência. Os CSVs atmosféricos completos v70/v71 são idênticos, byte a byte, nas três seeds. As referências v70 também coincidem com os CSVs do experimento anterior de umidade +30%.

As médias abaixo usam a chuva final dos hexes, depois do espalhamento, do teto e do offset. São médias aritméticas, sem ponderação por área esférica. A média geral inclui água e terra. A média terrestre e o indicador abaixo de 260 incluem todos os hexes terrestres ativos, não somente as terras baixas do interior das análises atmosféricas anteriores.

| Seed | Receita | Média geral | Média terrestre | Terra abaixo de 260 | Mapa no teto de 5000 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v70 | 1435,24 | 1762,71 | 11,86% | 4,31% |
| 1155827384 | v71 | 2536,90 | 2755,04 | 4,80% | 24,29% |
| 1234 | v70 | 1338,70 | 1838,49 | 17,25% | 4,67% |
| 1234 | v71 | 2395,63 | 2705,04 | 9,98% | 20,61% |
| -77 | v70 | 1298,69 | 1239,52 | 33,13% | 4,12% |
| -77 | v71 | 2304,04 | 1900,30 | 22,60% | 22,35% |

O efeito é forte: a chuva aumenta também onde já chovia, e mais hexes atingem o teto. Esses números medem o bônus no mapa; a quantidade de vapor, nuvens e chuva calculada dentro da atmosfera não aumentou em relação à v70.

## Biomas finais

Contagens depois do acabamento de biomas, dos rios e dos biomas fluviais. Percentuais sobre os hexes terrestres ativos: 3.734.788 para 1155827384, 3.843.223 para 1234 e 3.821.766 para -77. Florestas seguem `Biomes.IsForest`, incluindo pântano (`Swamp`), mangue, taiga e as demais florestas.

| Seed | Receita | Deserto | Badlands | Estepe | Florestas |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v70 | 7,55% | 2,70% | 4,69% | 52,76% |
| 1155827384 | v71 | 3,58% | 1,08% | 2,17% | 66,24% |
| 1234 | v70 | 11,79% | 3,15% | 6,00% | 50,72% |
| 1234 | v71 | 7,46% | 2,19% | 2,64% | 62,42% |
| -77 | v70 | 25,97% | 3,71% | 11,02% | 33,75% |
| -77 | v71 | 20,15% | 2,08% | 4,16% | 46,77% |

Desertos em hexes: 282.122 → 133.813 na seed 1155827384, 453.305 → 286.791 na seed 1234 e 992.431 → 770.234 na seed -77. As regras de temperatura, classificação e gelo polar continuam ativas; esta etapa não as alterou.

## Atmosfera e compatibilidade

- PASS: atmosfera v70/v71 idêntica nas três seeds máximas; erro relativo de água menor que 1,2e-14 e nenhuma aplicação do limitador térmico de emergência. Isso certifica o solver, não conservação do bônus de chuva nos hexes.
- PASS: regressões de umidade +30%, retorno 500/25, transporte de nuvens v67, três células, Hadley acoplado e Hadley v64.
- PASS: 48 mundos antigos de referência idênticos bit a bit, incluindo camadas, rios, regiões e ícones.
- PASS: receitas anteriores suportadas não recebem o novo bônus. A v68 removida continua exigindo a build Alpha 2.0.5.

## Interface e EXE

- PASS: projeto `MapmakerHexRainSpread.csproj` compilado sem avisos ou erros com `-p:NuGetAudit=false`; exportação portátil concluída com código 0.
- PASS: EXE criou v71 pelo formulário, exibiu chuva/biomas e as camadas de vento, salvou e reabriu e reconstruiu os mesmos hashes de vento. Autoteste encerrou com código 0 e stderr vazio.

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.8-Experimental-Chuva-Expandida.exe`. SHA-256: `670AD1083DFAAA939F8A6045F0FA3B5864664540CCC937F0259681D144F56A6A`.

Crie um mapa novo para usar a v71. A identidade de assembly própria permite manter as builds anteriores para comparar os mesmos parâmetros e a mesma seed.

## Imagens e reprodução

Os CSVs atmosféricos continuam em `v*-cells.csv`; eles descrevem a etapa anterior ao bônus. Os CSVs `v*-hex-rain.csv` agregam a chuva dos hexes finais à grade de diagnóstico, para visualizar a diferença. Os PNGs dessa segunda série usam uma escala compartilhada, registrada em `mapped-scales.json`.

| Chuva dos hexes v70, seed 1155827384 | Chuva dos hexes v71 |
| --- | --- |
| ![Chuva dos hexes v70](hex-rain-spread/1155827384/v70-mapped-rain.png) | ![Chuva dos hexes v71](hex-rain-spread/1155827384/v71-mapped-rain.png) |

As imagens são uma agregação retangular do campo final e não usam a projeção curva do Atlas. As estatísticas e contagens de biomas foram calculadas diretamente nos hexes ativos.

```powershell
dotnet run -c Release --project tests/GeneratorChecks -- --hex-rain-spread-only
dotnet run -c Release --project tests/GeneratorChecks -- --hex-rain-spread-review
python tests/GeneratorChecks/render_climate_diagnostics.py --mapped results/hex-rain-spread/1155827384 results/hex-rain-spread/1234 results/hex-rain-spread/-77
dotnet run --project tests/CompatibilityChecks
```

Logs: `hex-rain-spread-checks.log`, `hex-rain-spread-1155827384.log`, `hex-rain-spread-other-seeds.log`, `hex-rain-spread-compatibility.log`, `hex-rain-spread-build.log`, `hex-rain-spread-export.log` e `hex-rain-spread-ui.log`. Dados completos por seed em `results/hex-rain-spread/<seed>/`.

Nenhum commit, push ou release no GitHub foi realizado.
