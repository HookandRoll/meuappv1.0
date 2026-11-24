# Alpha 2.0.6: retorno da chuva terrestre às nuvens

A implementação experimental v68 de cobertura vegetal e raízes foi removida. A receita v69 usa a chuva recebida durante a própria simulação para devolver uma fração da água terrestre às nuvens da camada inferior. Não consulta os biomas, que continuam sendo classificados depois do clima.

## Regra

A configuração da receita v69 é limiar de **500 mm/ano** e retorno de **25%**. O comparativo registra também 750 mm e retorno de 50%.

Em cada passo, a simulação acumula a chuva recebida e calcula sua média anualizada desde o início da execução. Essa estimativa usa a mesma conversão 10,5, o teto de 5000 e o offset de precipitação usados na geração do mapa. O limiar é estritamente maior que o valor configurado: exatamente 500 mm não ativa uma configuração de 500 mm.

Quando a estimativa supera o limiar, uma fração da nova chuva que chegou à terra retorna às nuvens. Por exemplo, num passo elegível com retorno de 25%, de cada 1 unidade de chuva terrestre, 0,25 volta às nuvens e 0,75 permanece disponível para infiltração/escoamento. O retorno nunca utiliza uma reserva inicial fictícia nem devolve chuva que não caiu naquele passo.

O ciclo é chuva → terra → nuvens inferiores → transporte pelo vento → nova chuva. A nuvem devolvida só participa do transporte e dos processos de nuvem dos passos seguintes. Cada nova queda pode voltar a ser reciclada enquanto o local estiver acima do limiar.

O controle de chuva continua registrando a precipitação recebida, antes do retorno. A água reciclada é subtraída da infiltração e adicionada ao reservatório de nuvens, portanto não fica simultaneamente no solo e na atmosfera. A precipitação acumulada pode superar a água inicial porque a mesma água pode chover mais de uma vez. O balanço de massa não usa a chuva como perda externa: água inicial + evaporação aquática = atmosfera + solo + escoamento.

O retorno aplica-se apenas à fração terrestre. Oceanos, lagos e outras superfícies aquáticas mantêm seus mecanismos anteriores. Nas células mistas, a fração de terra pondera o retorno. A evaporação do solo já existente na v67 continua ativa; o sistema de raízes e cobertura vegetal da v68 não existe na nova implementação.

## Resolução e aproximação

O clima é simulado numa grade atmosférica de até 512×256. A chuva dessa grade é interpolada para os hexes do mapa. O limiar e o retorno são avaliados nessa grade durante a simulação, e não separadamente em cada um dos milhões de hexes finais. A estimativa acumulada também pode diferir da média da janela final de amostragem, que exclui o aquecimento.

Trata-se de um retorno direto parametrizado de água líquida às nuvens. Não resolve evaporação, evapotranspiração vegetal ou transporte subterrâneo entre hexes. A propagação horizontal da água devolvida ocorre pelo transporte atmosférico existente. As duas camadas e as três células de circulação continuam no mesmo solver.

## Testar e comparar

```powershell
dotnet build MapmakerRainRecycling.csproj -p:NuGetAudit=false
dotnet run -c Release --project tests/GeneratorChecks -- --rain-return-only
dotnet run -c Release --project tests/GeneratorChecks -- --climate-matrix
python tests/GeneratorChecks/render_climate_diagnostics.py results/rain-return/1155827384 results/rain-return/1234 results/rain-return/-77
dotnet run --project tests/CompatibilityChecks
```

A matriz compara a v67 com as quatro combinações de 500/750 mm e 25%/50%, nas seeds 1155827384, 1234 e -77, no tamanho máximo. Conta os biomas finais da referência e da receita v69 depois do acabamento e dos rios. As quatro combinações intermediárias comparam a atmosfera sobre a mesma superfície, sem gerar uma nova classificação final de biomas para cada uma.

Opções: `--small`, `--seed 1234`, `--seeds 1234,-77`, `--skip-comparison`, `--versions 69` e `--output-root results/outro-experimento`. Uma atualização com `--versions` verifica o hash da superfície contra o resumo existente antes de substituir resultados.

Os diagnósticos exportam chuva, vapor, nuvens, água do solo, retorno de chuva, evaporação aquática e evaporação do solo. O retorno tem acumulado global e uma taxa média por célula durante a janela de amostragem. PNGs do mesmo campo compartilham a escala entre variantes da mesma seed; os limites ficam em `scales.json`.

## Versões

Novos mapas na Alpha 2.0.6 usam v69. Saves v64–v67 preservam suas receitas e não recebem o retorno retroativamente. Saves da v68 removida exigem a build Alpha 2.0.5; a nova build recusa essa receita com uma mensagem explícita, em vez de recalcular silenciosamente outro clima.

EXE local: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.6-Experimental-Retorno-Chuva.exe`. A nova receita é o padrão e não exige argumento de inicialização. O assembly `MapmakerRainRecycling` separa o cache .NET das builds anteriores.

Parâmetros escolhidos e resultados: [comparativo do retorno de chuva](results/Teste_Retorno_Chuva_v69.md).
