# Alpha 2.0.7: vapor +30% em todos os tamanhos

A receita v70 aumenta em 30% a capacidade de vapor nas duas camadas atmosféricas. O reforço é igual nos seis tamanhos, em Mundo e Região; não acumula 30% a cada tamanho maior. Os saves anteriores continuam usando suas receitas.

## Capacidade e aporte

O multiplicador da capacidade passa de 1,50 para 1,95. A fórmula continua dependendo da temperatura e da massa de ar. A 25 °C, por unidade de massa de ar, o limiar de saturação passa de 45 para 58,5 unidades normalizadas de água. O excesso vira nuvens gradualmente pela condensação existente; não é um teto rígido aplicado por descarte de vapor. O armazenamento de água de nuvem é outro reservatório e não recebeu um limite novo.

O aporte de umidade sobre água depende da diferença entre o alvo de umidade relativa e o vapor presente. Sob a mesma temperatura, massa de ar, umidade relativa, superfície e passo de tempo, a capacidade 30% maior já faz esse aporte crescer 30%.

O coeficiente de reposição permanece igual. Aumentá-lo também em 30% multiplicaria o aporte potencial por 1,30 × 1,30 = 1,69 sob essas condições, ultrapassando o reforço pedido. O ajuste foi contado uma vez só.

| Parâmetro | v69 | v70 |
| --- | ---: | ---: |
| Multiplicador da capacidade | 1,50 | 1,95 |
| Capacidade a 25 °C por unidade de massa de ar | 45 | 58,5 |
| Multiplicador da velocidade de reposição aquática | 2,0 | 2,0 |
| Alvo de umidade: oceano profundo / raso | 90% / 95% | 90% / 95% |
| Alvo de umidade: lago / outras superfícies aquáticas | 85% / 80% | 85% / 80% |
| Retorno de chuva terrestre | Acima de 500 mm, 25% | Acima de 500 mm, 25% |
| Mistura horizontal de vapor/nuvens | 0,08 | 0,08 |
| Tempo de precipitação de nuvens | 3 | 3 |

A umidade relativa inicial permanece em 55%. Por isso, a água atmosférica inicial também cresce 30%, enquanto a reserva inicial do solo permanece igual. Esse novo estoque é contabilizado no balanço inicial. Durante a simulação, oceanos, lagos e outras fontes aquáticas continuam fornecendo a água; o solo e o retorno de chuva apenas transferem água já disponível.

O ganho de chuva não é obrigado a ser exatamente 30%. Ventos, temperatura dinâmica, condensação, reevaporação, retenção de nuvens e ativação do retorno terrestre podem mudar a distribuição e a quantidade final.

## Testar

```powershell
dotnet build MapmakerHumidityBoost.csproj -p:NuGetAudit=false
dotnet run -c Release --project tests/GeneratorChecks -- --humidity-boost-only
dotnet run -c Release --project tests/GeneratorChecks -- --humidity-boost-review
python tests/GeneratorChecks/render_climate_diagnostics.py results/humidity-boost/1155827384 results/humidity-boost/1234 results/humidity-boost/-77
dotnet run --project tests/CompatibilityChecks
```

O comparativo gera v69 e v70 para as três seeds no Atlas máximo, com a mesma superfície, temperatura de referência e demais parâmetros. Conta os biomas depois do acabamento e dos rios. Pode ser limitado com `--small`, `--seed 1234`, `--seeds 1234,-77`, `--versions 70` ou `--output-root results/outro-comparativo`.

O teste de seleção cobre os seis tamanhos nos dois modos sem alocar ou gerar todos esses mundos. As simulações controladas verificam armazenamento nas duas camadas, quatro temperaturas, aporte em oceano profundo/raso, lago e outras águas, conservação, determinismo e save/load.

## Executável e compatibilidade

`dist/Jota-Fantasy-Map-Maker-Alpha-2.0.7-Experimental-Umidade-30.exe` cria mapas v70 por padrão. O projeto e assembly são `MapmakerHumidityBoost`, separados da build anterior para evitar conflito de cache do runtime.

Saves v69 continuam com capacidade 1,50 e retorno 500/25; v64–v67 também conservam suas configurações. A receita removida v68 permanece recusada na build atual e pode ser aberta na Alpha 2.0.5.

O espalhamento legado de chuva por hexes continua desativado. A mistura atmosférica 0,08 e o halo de umidade de rios usado na classificação dos biomas continuam ativos. Não foram alterados neste reforço.

Resultados: [comparativo de umidade v70](results/Teste_Umidade_30_v70.md).
