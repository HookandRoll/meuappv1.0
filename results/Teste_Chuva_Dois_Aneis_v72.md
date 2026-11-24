# Vapor 40 e chuva em dois anéis: v72

Data: 24/11/2026. A Alpha 2.0.9 aplica a capacidade nominal 40, com aporte proporcional, e os pesos pedidos: 50% no centro, 15% em cada um dos seis hexes do primeiro anel e 5% em cada um dos 12 do segundo. Os valores são iguais em todos os tamanhos e nos modos Mundo e Região.

## Capacidade e aporte

A capacidade a 25 °C por unidade de massa de ar passa de 58,5 para 40 unidades normalizadas de água. O multiplicador da curva de capacidade passa de 1,95 para 40/30. A capacidade continua dependendo da temperatura e da massa de ar nas duas camadas; 40 não é um teto universal por hex.

O fator 40/58,5 = 0,68376068 reduz o armazenamento nominal e o aporte aquático em aproximadamente 31,62%, sob a mesma temperatura, massa, umidade relativa, superfície e passo de tempo. O coeficiente de aproximação aos alvos permanece igual; reduzi-lo novamente contaria o fator duas vezes no aporte. Os testes controlados verificaram a redução em oceano profundo, oceano raso, lago e outras águas, além do estoque inicial em quatro temperaturas.

A umidade relativa inicial permanece em 55%; por isso a água atmosférica inicial também diminui na proporção 40/58,5 e continua contabilizada no balanço inicial. A reserva inicial de solo não mudou. A evaporação do solo continua limitada pela água disponível.

| Parâmetro | v71 | v72 |
| --- | ---: | ---: |
| Multiplicador da capacidade de vapor | 1,95 | 40/30 |
| Capacidade a 25 °C por unidade de massa de ar | 58,5 | 40 |
| Multiplicador da velocidade de reposição aquática | 2,0 | 2,0 |
| Alvos de umidade: oceano profundo / raso | 90% / 95% | 90% / 95% |
| Alvos: lago / outras águas | 85% / 80% | 85% / 80% |
| Retorno terrestre | Acima de 500 mm, 25% | Acima de 500 mm, 25% |
| Mistura de vapor e nuvens | 0,08 | 0,08 |
| Conversão de chuva | 10,5 | 10,5 |

O total efetivamente evaporado e a chuva final não precisam cair exatamente 31,62%, pois as temperaturas, a umidade relativa, os ventos e a ativação do retorno terrestre podem evoluir de maneira diferente.

## Espalhamento da chuva

| Destino | Hexes | Peso em cada um | Impulso com 1000 mm de base |
| --- | ---: | ---: | ---: |
| Centro | 1 | 50% | 500 mm |
| Primeiro anel, distância 1 | 6 | 15% | 150 mm em cada |
| Segundo anel, distância 2 | 12 | 5% | 50 mm em cada |
| Total antes do teto e das bordas | 19 | 200% somados | 2000 mm somados |

O segundo anel contém 12 posições únicas: as seis em linha reta e as seis entre elas. Cada hex recebe as contribuições do campo-base original; valores recém-recebidos não voltam a se espalhar. Uma região uniforme de 1000 mm resulta em 2000 mm no interior antes do teto, contra 2250 na v71.

O alcance agora é dois passos na malha hexagonal em todos os tamanhos. Vizinhos fora do mapa ou do contorno ativo do Atlas são ignorados sem redistribuição de seus pesos. O wrap horizontal segue o manifesto. Oceanos, lagos e terra participam; não há bloqueio de altitude nessa passagem.

O campo-base é limitado a 5000 antes do espalhamento. A soma também é limitada a 5000 e depois recebe o offset de precipitação. O arredondamento ocorre ao final. O teto continua reduzindo o ganho onde as contribuições se sobrepõem.

Os pesos somam 200%, portanto o espalhamento continua sendo um bônus de geração. A chuva extra dos hexes não é retirada das nuvens nem reinjetada no retorno 500/25. O solver atmosférico conserva sua própria água; o campo final ampliado não é uma redistribuição conservativa dessa água. Biomas e rios usam o campo final.

## Testes controlados

- PASS: seleção de capacidade 40 e demais parâmetros nos seis tamanhos e nos modos Mundo/Região, por validação dos manifestos e da configuração, sem alocar todos os mundos grandes.
- PASS: aporte aquático na proporção 40/58,5 em quatro tipos de superfície e estoque inicial nas duas camadas, em -30, 0, 25 e 45 °C.
- PASS: impulso de 1000 produz 500 no centro, seis valores de 150 e 12 de 50. Exatamente 19 hexes positivos e soma de 2000, com conferência independente pela distância hexagonal.
- PASS: fonte imutável, região uniforme, bordas fechadas, wrap e contorno ativo do Atlas. Não há duplicação do segundo anel nem vazamento de células inativas.
- PASS: geração normal aplica os dois anéis e o teto. Sistema vazio continua sem água quando não há fonte.
- PASS: save/load, regeneração completa e regeneração regional com seleção e bloqueios preservam a receita e não reutilizam chuva já expandida.

## Comparativo no Atlas máximo

Seeds 1155827384, 1234 e -77. Referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos, atmosfera 512×256, três continentes, terra 25%, costa 80 e offsets zero. Aquecimento e amostragem de 240 passos cada, passo externo 0,25.

As gerações exigiram hashes idênticos de elevação, água e temperatura de referência. Nas três seeds, os CSVs da atmosfera e da chuva final agregada da referência v71 são idênticos, byte a byte, aos do experimento anterior. A comparação altera capacidade e pesos juntos; não isola o efeito de cada mudança.

As médias abaixo usam a chuva final dos hexes, depois do espalhamento, do teto e do offset. A média geral inclui água e terra. As demais colunas terrestres incluem todos os hexes terrestres ativos, não apenas terras baixas do interior. As médias não têm ponderação por área esférica.

| Seed | Receita | Média geral | Média terrestre | Terra abaixo de 260 | Mapa no teto de 5000 |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v71 | 2536,90 | 2755,04 | 4,80% | 24,29% |
| 1155827384 | v72 | 1849,72 | 2134,40 | 8,44% | 9,48% |
| 1234 | v71 | 2395,63 | 2705,04 | 9,98% | 20,61% |
| 1234 | v72 | 1720,32 | 2183,59 | 12,96% | 8,51% |
| -77 | v71 | 2304,04 | 1900,30 | 22,60% | 22,35% |
| -77 | v72 | 1676,95 | 1497,87 | 27,06% | 8,38% |

A redução de chuva é consistente com o ajuste pedido. O espalhamento alcança um anel adicional, enquanto o aporte e a amplificação total diminuem. Há menos hexes no teto, mas a fração terrestre abaixo de 260 aumenta. Esse limiar é um indicador de secura, não uma contagem de desertos.

A média bruta na grade atmosférica, antes do bônus dos hexes, passa de 1464,03 para 1006,53 na seed 1155827384, de 1401,53 para 963,38 na 1234 e de 1354,49 para 934,17 na -77. São médias aritméticas da grade, incluindo água e terra e anteriores ao teto dos hexes; diferem da média geral final da tabela.

## Biomas finais

Contagens depois do acabamento, dos rios e dos biomas fluviais. Denominadores terrestres: 3.734.788 para 1155827384, 3.843.223 para 1234 e 3.821.766 para -77. Florestas seguem `Biomes.IsForest`, incluindo pântano (`Swamp`), mangue, taiga e as demais florestas.

| Seed | Receita | Deserto | Badlands | Estepe | Florestas |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v71 | 3,58% | 1,08% | 2,17% | 66,24% |
| 1155827384 | v72 | 5,87% | 1,94% | 3,39% | 59,27% |
| 1234 | v71 | 7,46% | 2,19% | 2,64% | 62,42% |
| 1234 | v72 | 9,44% | 2,60% | 3,97% | 55,57% |
| -77 | v71 | 20,15% | 2,08% | 4,16% | 46,77% |
| -77 | v72 | 23,06% | 2,79% | 7,05% | 40,96% |

Desertos em hexes: 133.813 → 219.194; 286.791 → 362.789; 770.234 → 881.461. A temperatura de referência, os classificadores de bioma e a imposição de gelo polar não foram alterados.

## Balanço e compatibilidade

- PASS: todas as seis execuções máximas conservam a água atmosférica com erro relativo menor que 1,3e-13, abaixo do limite 1e-5. Nenhuma aplicação do limitador térmico de emergência.
- PASS: regressões de espalhamento v71, umidade +30% v70, retorno v69, transporte de nuvens v67, três células, Hadley acoplado e Hadley v64.
- PASS: 48 mundos antigos de referência idênticos bit a bit, incluindo camadas, rios, regiões e ícones.
- PASS: referências v71 idênticas ao experimento anterior tanto na atmosfera quanto na chuva final agregada. Saves anteriores suportados mantêm suas receitas. A v68 removida continua exigindo a Alpha 2.0.5.

## EXE e interface

- PASS: `MapmakerBalancedRain.csproj` compilou sem avisos ou erros com `-p:NuGetAudit=false`; a exportação portátil concluiu com código 0.
- PASS: EXE criou v72 pelo formulário, mostrou chuva/biomas e as duas camadas de vento, salvou e reabriu e reconstruiu os mesmos hashes de vento. O autoteste terminou com código 0 e stderr vazio.

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.9-Experimental-Chuva-Dois-Aneis.exe`, 206.122.968 bytes, versão 2.0.9.0. SHA-256: `56CE45EDF36FBA7A509A75590C70D2CE14319CCC16F54FA74F6F03E2DE62DA76`.

Crie um mapa novo para usar a v72. O assembly próprio separa o cache do runtime das versões anteriores.

## Dados e reprodução

Dados em `results/balanced-rain/<seed>/`: JSONs, CSVs atmosféricos, contagens finais de biomas e chuva final dos hexes agregada à grade de diagnóstico. Os PNGs `mapped` mostram o campo final, com escala compartilhada entre v71/v72 de cada seed.

| Chuva dos hexes v71, seed 1155827384 | Chuva dos hexes v72 |
| --- | --- |
| ![Chuva v71](balanced-rain/1155827384/v71-mapped-rain.png) | ![Chuva v72](balanced-rain/1155827384/v72-mapped-rain.png) |

As imagens usam uma agregação retangular e não a projeção curva do Atlas. Estatísticas e contagens foram calculadas diretamente nos hexes ativos.

```powershell
dotnet run -c Release --project tests/GeneratorChecks -- --balanced-rain-only
dotnet run -c Release --project tests/GeneratorChecks -- --balanced-rain-review
python tests/GeneratorChecks/render_climate_diagnostics.py --mapped results/balanced-rain/1155827384 results/balanced-rain/1234 results/balanced-rain/-77
dotnet run --project tests/CompatibilityChecks
```

Logs: `balanced-rain-checks.log`, `balanced-rain-1155827384.log`, `balanced-rain-other-seeds.log`, `balanced-rain-compatibility.log`, `balanced-rain-build.log`, `balanced-rain-export.log` e `balanced-rain-ui.log`.

O comparativo usa a duração padrão e não demonstra estabilização de longo prazo. Nenhum commit, push ou release no GitHub foi realizado.
