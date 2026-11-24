# Alpha 2.0.9: vapor 40 e chuva em dois anéis

A receita v72 combina a redução de vapor pedida com um espalhamento mais amplo e pesos menores. Os valores são iguais em todos os tamanhos e nos modos Mundo e Região. As receitas antigas suportadas permanecem preservadas.

## Capacidade e aporte de vapor

A 25 °C, por unidade de massa de ar, a capacidade nominal passa de **58,5 para 40 unidades normalizadas de água**. O multiplicador da curva térmica passa de 1,95 para 40/30. A capacidade continua variando com temperatura e massa de ar nas duas camadas; não é um teto fixo de 40 para qualquer temperatura ou hex.

O fator é 40/58,5 = 0,68376068, uma redução de aproximadamente 31,62%. Sob a mesma temperatura, massa de ar, umidade relativa, superfície e passo de tempo, o aporte aquático cai nessa mesma proporção, porque depende da capacidade e do déficit de vapor.

O coeficiente de aproximação aos alvos de umidade foi mantido: ele já estava igual ao da v69. Reduzi-lo novamente multiplicaria a queda do aporte por outro fator. A umidade relativa inicial permanece em 55%, de modo que a água atmosférica inicial também diminui na proporção 40/58,5. Esse estoque inicial continua contabilizado no balanço; a reserva inicial do solo não mudou.

Os alvos de umidade de oceano profundo/raso continuam em 90%/95%; lagos e outras águas em 85%/80%. O retorno terrestre 500 mm/25%, a mistura atmosférica 0,08 e as intensidades Hadley/Ferrel/Polar 1,0/0,8/0,6 não foram alterados.

## Chuva nos hexes

Cada hex de origem contribui com:

| Destino | Quantidade de hexes | Peso em cada hex | Exemplo com 1000 mm de base |
| --- | ---: | ---: | ---: |
| Centro | 1 | 50% | 500 mm |
| Primeiro anel, distância 1 | 6 | 15% | 150 mm em cada |
| Segundo anel, distância 2 | 12 | 5% | 50 mm em cada |
| Total antes do teto e das bordas | 19 | 200% somados | 2000 mm somados |

O segundo anel tem exatamente 12 posições distintas, incluindo as seis posições em linha reta e as seis entre elas. A contribuição usa a distância hexagonal, com alcance máximo de dois passos. Não há dupla contagem dos hexes do segundo anel.

A passagem lê um campo-base imutável. Cada hex recebe os pesos originais dos seus vizinhos; o que foi recebido não é espalhado novamente. Uma região uniforme de 1000 mm resulta em 2000 mm no interior antes do teto. Regenerar recalcula a base original, sem amplificar novamente o campo já processado.

O teto-base de 5000 é preservado antes e depois da soma, e o offset de precipitação é aplicado ao final. Contribuições fora do mapa ou do contorno ativo do Atlas são ignoradas sem redistribuição de pesos. O wrap horizontal segue o manifesto. Não há bloqueio de altitude nessa passagem; oceanos, lagos e terra participam do campo de chuva.

Os pesos somam 200%, portanto o espalhamento continua sendo um bônus de geração, separado do balanço da atmosfera. A chuva adicional dos hexes alimenta biomas e rios, mas não é retirada das nuvens nem reinjetada no retorno 500/25. O retorno continua sendo calculado sobre a chuva do solver, anterior ao bônus.

## Testar

```powershell
dotnet build MapmakerBalancedRain.csproj -p:NuGetAudit=false
dotnet run -c Release --project tests/GeneratorChecks -- --balanced-rain-only
dotnet run -c Release --project tests/GeneratorChecks -- --balanced-rain-review
python tests/GeneratorChecks/render_climate_diagnostics.py --mapped results/balanced-rain/1155827384 results/balanced-rain/1234 results/balanced-rain/-77
dotnet run --project tests/CompatibilityChecks
```

Os testes cobrem seleção nos seis tamanhos e nos dois modos, capacidade e aporte proporcional, quatro temperaturas, impulso de chuva nos 19 hexes, fonte imutável, bordas, wrap, contorno do Atlas, teto, balanço atmosférico, save/load e regeneração regional com bloqueios.

O comparativo gera v71 e v72 nas três seeds máximas, com os mesmos parâmetros de terreno. Opções: `--small`, `--seed 1155827384`, `--seeds 1234,-77`, `--versions 72` e `--output-root results/outro-comparativo`.

Os CSVs atmosféricos e os da chuva final nos hexes permanecem separados. A opção `--mapped` do renderizador mostra a chuva final agregada à grade de diagnóstico, com escala compartilhada entre as versões de cada seed.

## EXE e compatibilidade

`dist/Jota-Fantasy-Map-Maker-Alpha-2.0.9-Experimental-Chuva-Dois-Aneis.exe` cria mapas v72 por padrão. Projeto e assembly: `MapmakerBalancedRain`, com cache de runtime separado das builds anteriores.

Saves v71 preservam vapor 58,5 e pesos 75%/25%; v70 e anteriores suportados também preservam suas receitas. A v68 removida continua exigindo a Alpha 2.0.5. Crie um mapa novo para experimentar os valores da v72.

Resultados: [comparativo de vapor e dois anéis v72](results/Teste_Chuva_Dois_Aneis_v72.md).
