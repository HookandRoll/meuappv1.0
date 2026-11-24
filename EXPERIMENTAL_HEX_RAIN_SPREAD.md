# Alpha 2.0.8: chuva expandida nos hexes

A receita v71 acrescenta uma passagem sobre a chuva interpolada para os hexes. Cada contribuição original fica em 75% no hex de origem e envia 25% a cada um dos seis vizinhos imediatos. O alcance é um anel de um hex em todos os tamanhos, tanto em Mundo quanto em Região.

## Pesos e sobreposição

Para um hex isolado com chuva-base de 1000 mm e vizinhos sem chuva:

| Destino | Contribuição |
| --- | ---: |
| Hex central | 750 mm |
| Cada um dos seis vizinhos | 250 mm |
| Soma dos sete hexes | 2250 mm |

Os pesos somam 225%: 75% + 6 × 25%. Portanto, essa regra amplifica a chuva do mapa; não redistribui uma quantidade conservada de chuva. Cada hex também recebe as contribuições originais dos seus vizinhos. Numa região uniforme com 1000 mm em todos os hexes, o resultado no interior é 2250 mm antes do teto.

Todas as contribuições leem um campo-base imutável, depois da interpolação atmosférica e da conversão para as unidades do mapa. A chuva recebida nesta passagem não é espalhada novamente. Regenerar um mapa também recalcula a base original, sem multiplicar uma chuva que já havia sido expandida.

A chuva-base mantém o teto anterior de 5000 mm antes do offset. Após somar os pesos, o resultado também é limitado a 5000 e então recebe o offset de precipitação. Por isso, regiões com contribuições sobrepostas podem atingir o teto em vez de receber um aumento integral de 125%. A regra é aplicada antes da classificação dos biomas e da geração dos rios, que passam a usar a chuva resultante.

## Limites do mapa

São usados os seis vizinhos da malha hexagonal. Vizinhos fora do domínio ativo do Atlas ou fora das bordas fechadas são ignorados, sem redistribuir seus pesos. O wrap horizontal é respeitado quando essa opção está gravada no manifesto do mapa. Oceanos, lagos e terra participam do campo de chuva; não há bloqueio de altitude nesta passagem de um hex.

## Atmosfera e umidade

A chuva já alimentava vários hexes por interpolação da grade atmosférica. Vapor e nuvens continuam sendo transportados e misturados no solver, com as duas camadas e as três células. A v71 usa a mesma atmosfera da v70, incluindo vapor +30% e retorno terrestre 500 mm/25%.

O bônus 75/25 é aplicado depois dessa simulação. A água extra dos valores finais do mapa não é retirada das nuvens do solver. O balanço atmosférico permanece conservativo, mas o campo de chuva ampliado não representa uma distribuição conservativa daquela água. O retorno 500/25 continua usando a chuva da simulação, antes desse bônus; a chuva extra do pós-processamento não volta a alimentar as nuvens.

## Verificar

```powershell
dotnet build MapmakerHexRainSpread.csproj -p:NuGetAudit=false
dotnet run -c Release --project tests/GeneratorChecks -- --hex-rain-spread-only
dotnet run -c Release --project tests/GeneratorChecks -- --hex-rain-spread-review
python tests/GeneratorChecks/render_climate_diagnostics.py --mapped results/hex-rain-spread/1155827384 results/hex-rain-spread/1234 results/hex-rain-spread/-77
dotnet run --project tests/CompatibilityChecks
```

O teste controlado verifica os pesos, os seis sentidos, uma passagem, sobreposição, bordas, wrap, contorno do Atlas, teto de chuva, criação, save/load e regeneração regional com bloqueios. O comparativo gera as três seeds máximas nas receitas v70 e v71, compara a atmosfera e mede chuva final e biomas depois dos rios.

Opções do comparativo: `--small`, `--seed 1155827384`, `--seeds 1234,-77`, `--versions 71` e `--output-root results/outro-comparativo`.

Os CSVs `v*-cells.csv` mostram a atmosfera, anterior ao bônus. Os CSVs `v*-hex-rain.csv` mostram a média da chuva dos hexes finais em cada bloco da grade de diagnóstico. Use `--mapped` no renderizador para visualizar essa segunda série, com escala compartilhada entre as variantes da mesma seed.

## EXE e versões

`dist/Jota-Fantasy-Map-Maker-Alpha-2.0.8-Experimental-Chuva-Expandida.exe` cria mapas v71 por padrão, sem argumentos adicionais. O assembly `MapmakerHexRainSpread` tem identidade própria para separar o cache do runtime das builds anteriores.

Saves v70 e anteriores suportados preservam suas receitas e não recebem o bônus ao abrir. A receita removida v68 continua exigindo a build Alpha 2.0.5. Para experimentar a nova regra, crie um mapa novo.

Resultados: [comparativo de chuva por hex v71](results/Teste_Chuva_Hex_v71.md).
