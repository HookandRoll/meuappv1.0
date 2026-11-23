# Circulação Hadley em duas camadas

O protótipo calcula duas camadas horizontais acopladas: superfície e altitude. Cada camada possui vento, pressão, massa de ar e vorticidade próprios. A subida retira massa da superfície e adiciona a mesma quantidade em altitude; a descida faz o caminho inverso. A transferência carrega o movimento da camada doadora.

O campo continua separado da geração de chuva e biomas. A camada original Ventos e o formato `.world` mantêm seu comportamento.

## Como visualizar

1. Gere ou abra um mapa com conteúdo Relevo ou Terreno completo.
2. Escolha **Hadley: solo** para inspecionar o vento na superfície.
3. Escolha **Hadley: altitude** para inspecionar o retorno em altitude. Os dois itens consultam o mesmo resultado da simulação.
4. Laranja indica transferência ascendente; azul-violeta indica transferência descendente; ciano representa valores próximos de zero. O comprimento das setas varia com a velocidade.
5. Clique em um hex. A barra informa a camada selecionada, direção, velocidade, troca vertical, pressão e vorticidade. O tooltip informa a direção e velocidade da outra camada.

O cálculo ocorre em segundo plano, com mensagem de carregamento. Editar relevo, água ou clima invalida o campo. Cancelar um rascunho, substituir o mapa ou fechar a aplicação cancela resultados pendentes; um resultado antigo não pode substituir o mapa atual.

## Grade e resolução

| Largura real do mapa | Limite automático da grade |
| --- | --- |
| Menor que 512 | 128×64 |
| De 512 a 2047 | 256×128 |
| A partir de 2048 | 512×256 |

A grade também respeita as dimensões do mapa aberto. É possível fornecer `HadleyWindSettings` explicitamente para comparar resoluções. O construtor sem argumentos agora aplica os padrões; `default(HadleyWindSettings)`, com todos os campos zerados, é rejeitado.

No Atlas curvo, cada linha é amostrada por longitude relativa aos seus limites úteis. Os cantos vazios não se tornam oceanos artificiais. O Atlas conecta leste e oeste; regiões sem wrap têm bordas fechadas para transporte de massa. Essa representação conserva massa na grade retangular reduzida, sem métricas esféricas completas ou ponderação por área real de latitude.

## Evolução de cada passo

1. A pressão combina a massa de cada camada com o forçamento zonal. O forçamento possui sinais opostos nas duas camadas para conduzir circulação de retorno.
2. O gradiente de pressão acelera o vento. Coriolis aplica uma rotação que preserva a velocidade nesse subpasso.
3. Atrito e bloqueio montanhoso atuam principalmente junto ao solo; o vento em altitude tem seu próprio atrito e recebe influência do terreno pelo acoplamento com a superfície.
4. O confinamento de vorticidade mantém parte dos redemoinhos resolvidos. Esse termo é um recurso numérico e não representa uma simulação completa de turbulência atmosférica.
5. O transporte por volumes finitos usa um único fluxo compartilhado em cada face para atualizar massa e movimento. O passo se subdivide conforme a resolução e a velocidade máxima, mantendo o mesmo tempo simulado entre grades e um limite CFL conservador.
6. A diferença de massa entre camadas e o forçamento convectivo definem a transferência vertical. O fluxo é limitado pela disponibilidade de massa na camada doadora.
7. Uma mistura adicional troca movimento entre as camadas com impulsos iguais e opostos.

A pressão é atualizada a partir das novas massas. A amostragem para desenhar as setas usa interpolação bilinear e pode consultar qualquer uma das camadas.

## Parâmetros

Os valores são normalizados, sem calibração em m/s, hPa ou altitude física de cada camada. `VerticalFlow` é uma taxa de transferência de massa entre camadas por unidade de tempo da simulação, não uma velocidade vertical em m/s.

| Parâmetro | Padrão | Papel |
| --- | ---: | --- |
| MaxGridWidth / MaxGridHeight | 128 / 64 | Limites explícitos da grade; seleção automática conforme a tabela quando não há override |
| Iterations | 80 | Número de passos externos |
| TimeStep | 0,25 | Duração de cada passo externo; subdividida para estabilidade |
| PressureForce | 0,62 | Aceleração pelo gradiente de pressão |
| CoriolisStrength | 0,95 | Rotação dependente da latitude |
| ThermalContrast | 0,22 | Anomalia térmica dos continentes, quando o mapa já tem clima |
| OrographicDeflection | 1,35 | Remoção da componente que encontra a encosta |
| OrographicPressure | 0,35 | Resistência adicional ao fluxo contra a encosta |
| VorticityConfinement | 0,16 | Intensidade do reforço de vórtices resolvidos |
| PressureCoupling | 0,28 | Resposta da pressão à massa da camada |
| OceanDrag / LandDrag / MountainDrag | 0,045 / 0,085 / 0,22 | Atrito superficial |
| Diffusion | 0,04 | Difusão numérica do vento |
| VerticalExchange | 0,8 | Resposta vertical ao desequilíbrio de massa |
| ConvectiveLift | 0,12 | Transferência vertical associada ao forçamento convectivo |
| UpperDrag | 0,02 | Atrito do vento em altitude |
| LayerMixing | 0,04 | Mistura de movimento entre as duas camadas |
| UseZonalPressureBands | true | Mantém os cinturões prescritos no protótipo normal; false usa apenas o gradiente equador-polos no experimento climático |

A agregação do terreno combina altitude média e pico local para reduzir a perda de cordilheiras estreitas. Os kernels de maior resolução processam linhas independentes em paralelo; cada estágio termina antes de o próximo ler seus resultados.

## Limites físicos

As posições gerais das células ainda são orientadas por `-cos(3π × latitude)` e pela anomalia térmica do mapa. O protótipo não calcula as células inteiramente a partir de insolação, termodinâmica e rotação. A célula de Ferrel também continua orientada por esse forçamento, sem um modelo físico completo de transporte por tempestades.

As camadas normais de visualização não transportam temperatura ou vapor e não substituem o clima atual. O experimento climático descrito abaixo acrescenta esses processos em um caminho separado. Nenhum dos dois modelos implementa correntes marítimas ou uma atmosfera 3D.

## Validação

```text
dotnet build Mapmaker.csproj
dotnet run --project tests/GeneratorChecks -- --hadley-only
dotnet run --project tests/GeneratorChecks -- --hadley-large
dotnet run --project tests/GeneratorChecks
dotnet run --project tests/CompatibilityChecks
godot --path . -- --hadley-review
godot --path . -- --hadley-seed-comparison
godot --path . -- --hadley-climate-comparison
godot --path . -- --hadley-replenishment-comparison
godot --path . -- --hadley-orographic-comparison
dotnet run --project tests/GeneratorChecks -- --hadley-climate-only
```

Os checks específicos verificam parâmetros padrão, resultados determinísticos, conservação da massa total, circulação superficial com retorno em altitude, subida equatorial, descida subtropical, Coriolis, influência do relevo, execução prolongada de 400 iterações, fronteiras fechadas/periódicas, cancelamento e grade 512×256. A tolerância relativa de massa é `1e-5`; todos os valores devem permanecer finitos e as massas positivas.

O teste Godot verifica os dois itens do seletor e a inspeção, o compartilhamento do campo, a invalidação por edição, o cancelamento do rascunho e a substituição do mapa enquanto existe cálculo pendente. As capturas ficam em `.godot/hadley-two-layers-surface.png` e `.godot/hadley-two-layers-upper.png`.

`--hadley-seed-comparison` gera o Atlas máximo duas vezes para cada seed 1234 e −77. Compara hashes de todos os arrays do mundo, dos onze campos Hadley e dos buffers de textura; também verifica se executar Hadley preserva os dados do mapa. Salva capturas de terreno, vento antigo, solo e altitude e um `summary.json` em `results/hadley-seed-comparison`. As estatísticas angulares usam pontos uniformes da grade atmosférica e descartam vetores com módulo menor que 0,02; não representam porcentagens da área física do planeta. As capturas ilustram as diferenças, sem uma asserção de igualdade dos pixels da interface.

## Experimento de chuva sem faixa subtropical

`--hadley-climate-comparison` é um caminho diagnóstico separado. Para as seeds 1234 e −77, gera um Atlas máximo com o clima antigo e outro com transporte de vapor pelas duas camadas Hadley. A chuva nova entra antes da classificação dos biomas e da geração dos rios. Elevação, água, relevo, formas geográficas, temperatura, tectônica e regiões são comparados por hash para manter as variáveis de controle.

Nesse experimento, a chuva não usa `ModernRainBaseline`, sua penalidade subtropical ou os reforços equatoriais/temperados antigos. O vento usa `UseZonalPressureBands=false`: troca o forçamento de três células por um gradiente simples `−cos(π × latitude)`, sem alta pressão imposta em 30° ou baixa pressão imposta em 60°. Esse gradiente ainda é um forçamento térmico aproximado; não é uma solução completa de insolação e termodinâmica.

O teste anterior com vento congelado permanece em `HadleyMoistureSimulation`, com resultados em `results/hadley-climate-comparison`. A comparação atual usa `HadleyCoupledClimateSimulation`, integrada aos mesmos subpassos do solver de vento:

1. Transporta calor, vapor e água de nuvem por faces compartilhadas, nas duas camadas. A troca vertical usa a mesma fração de massa de ar do solver.
2. Relaxa a temperatura próxima ao solo em direção à temperatura anual original, com resposta mais rápida sobre continentes e uma variação sazonal de sinais opostos entre hemisférios. A temperatura em altitude começa 25 °C abaixo da superficial e evolui independentemente. Anomalias das duas temperaturas alteram o forçamento de pressão a cada subpasso.
3. Condensa o excesso de vapor em água de nuvem. Nuvens são transportadas, podem reevaporar e precipitam gradualmente, com tempo de residência padrão de 3 unidades. O resfriamento na subida e o aquecimento na descida agora alteram a temperatura transportada, conforme a seção abaixo.
4. Armazena a parte terrestre da precipitação em um reservatório de solo, com estoque inicial de 12 e capacidade de 80 unidades por fração terrestre. A evapotranspiração retira água desse estoque. O excesso e a chuva sobre água saem como escoamento.
5. Aplica aquecimento por condensação, resfriamento por evaporação e uma aproximação de expansão/compressão na troca vertical.

A reposição aquática agora usa os seguintes alvos de umidade relativa na camada superficial:

| Fonte | Alvo para interromper a reposição | Peso por área |
| --- | ---: | ---: |
| Oceano com profundidade maior que 200 m | 90% | 1,0 |
| Oceano raso, profundidade de até 200 m | 95% | 1,0 |
| Lago | 85% | 0,6 |
| Área úmida | 80% | 0,4 |
| Solo | 80% | Limitado pelo estoque de água |

O limite de 200 m é a definição operacional do oceano raso neste experimento, usando a elevação de cada hex antes da agregação. Não é a faixa decorativa calculada pelo renderer. Cada fonte mantém seu próprio alvo em células atmosféricas mistas; uma parte de oceano profundo já em 90% não impede uma parte rasa de continuar evaporando até 95%.

`AquaticReplenishmentMultiplier=2` dobra a taxa aquática base `EvaporationRelaxation=0,18`, resultando em **0,36** antes dos pesos de área e disponibilidade. A evapotranspiração mantém sua taxa de 0,035 e passa a usar o alvo de 80%. Os alvos interrompem a fonte local, sem limitar vapor que chega pelo vento. A chuva sobre o oceano permanece contabilizada como saída da atmosfera para o reservatório aquático externo; a evaporação posterior é a entrada correspondente. Não se adiciona a mesma chuva novamente ao vapor de forma instantânea.

Há 240 passos de estabilização e 240 de amostragem, com passo externo 0,25, subdividido para estabilidade. Uma oscilação sazonal dura 60 unidades: a amostragem cobre um ciclo. A soma de **água inicial + evaporação de superfícies aquáticas − escoamento − água final** deve fechar com erro relativo abaixo de `1e-5`. A água final inclui vapor, nuvens e solo. Precipitação sobre terra e evapotranspiração são transferências internas, não criação ou perda de água. Oceanos, lagos e áreas úmidas são fontes externas renováveis neste protótipo; seus estoques não são simulados.

### Histórico térmico ao atravessar o relevo

Com `OrographicThermodynamics=true`, a camada superficial transporta a energia estática seca dividida pelo calor específico: `massa × (T_kelvin + Γ_seco × altitude_km)`. A temperatura local é reconstruída subtraindo o termo da altitude e convertendo para °C. A taxa seca de referência é `DryLapseRate=9,8 °C/km`. O transporte usa as mesmas faces dos fluxos de vapor, nuvem e massa de ar.

Esse estado transportado conserva o histórico térmico da parcela durante a passagem pela montanha. A subida consome a parcela térmica em favor da altitude e esfria o ar; na descida ocorre o inverso. A condensação remove vapor para nuvens e a precipitação pode retirar essa água antes da descida. O aquecimento posterior, portanto, não devolve automaticamente a água que choveu. Não há máscara de chuva aplicada ao lado protegido da montanha.

A troca vertical converte explicitamente o estado térmico superficial para a representação da camada superior, e vice-versa. A camada superior continua transportando sua própria temperatura e recebe o ar e a água transferidos da superfície. Mantém-se a parametrização anterior de ±6 °C na troca entre camadas; o termo `Γ×altitude` não é confundido com temperatura real nem aplicado duas vezes. A temperatura anual do mapa, usada nos biomas, permanece controlada; apenas o estado da atmosfera experimental evolui.

`OrographicThermodynamics=false` reativa o atalho antigo de resfriamento temporário na capacidade de condensação, permitindo ablações. `ThermalRelaxationMultiplier=0` desliga as trocas térmicas parametrizadas com superfície e alvo radiativo para os testes de coluna. O padrão continua sendo 1. Os efeitos de calor latente e reevaporação existentes continuam ativos; não foi introduzida uma nova taxa saturada calibrada nem um modelo completo de termodinâmica atmosférica. A altitude é a média amostrada na grade atmosférica, portanto cordilheiras menores que essa grade continuam parcialmente resolvidas.

Os checks incluem uma cordilheira simétrica com vento zonal prescrito e fronteiras periódicas. Verificam perfil adiabático seco, ar parado, transferência térmica entre as duas camadas sobre o pico e sobre a planície, condensação e chuva, inversão da sombra de chuva ao inverter o vento e conservação de água. Os ventos são fixados apenas nesse teste para isolar a termodinâmica. Na geração normal do experimento, os ventos continuam evoluindo junto com a temperatura e a umidade. Os perfis do teste controlado ficam em `results/hadley-orographic-column/summary.json`.

A saída bruta não tem calibração meteorológica. O teste registra essa intensidade e aplica um único fator global para usar a média global antiga como referência, antes do teto comum de 5000 mm/ano. O fator não depende da latitude, longitude ou continente. O teto pode reduzir a média final. Não há ajuste regional para favorecer as seeds testadas.

Para comparar especificamente a reposição mais forte, use `--hadley-replenishment-comparison`. Esse comando lê o ensaio acoplado anterior completo em `results/hadley-coupled-climate-comparison/summary.json`, gera novamente as seeds 1234 e −77 no Atlas máximo e exige igualdade dos oito hashes de controle. Mantém o fator de conversão de chuva anterior de cada seed, determinado antes da nova execução, sem recalcular a normalização global. Assim, o aumento da produção bruta pode aparecer na chuva final. O teto de 5000 continua aplicado. Salva o JSON comparativo e capturas de chuva, biomas e dos dois níveis de vento em `results/hadley-replenishment-comparison`; a referência anterior permanece intacta. Os resultados anteriores são arquivados, não regenerados por esse comando.

Para isolar o novo histórico térmico do relevo, use `--hadley-orographic-comparison`. A referência é o comparativo de reposição mais forte já concluído em `results/hadley-replenishment-comparison/summary.json`. Mantém alvos de umidade, força da reposição, terreno, temperatura anual e fator de conversão da chuva, e salva os resultados em `results/hadley-orographic-comparison`. Esse comando preserva a referência e evita que uma nova normalização global esconda as diferenças. A partir desta alteração, os outros comparadores usam o modelo experimental atual; compará-lo com referências mais antigas inclui todas as mudanças posteriores àquelas referências, não apenas a evaporação.

As unidades, capacidades de água, taxas térmicas, tempo de residência e período sazonal são parâmetros de experimento. A temperatura anual usada para classificar os biomas continua igual à do mapa de controle; as temperaturas dinâmicas pertencem à atmosfera experimental. A conservação de água e massa não significa conservação de energia: aquecimento superficial, relaxação radiativa e resfriamento de parcelas são parametrizados. O gradiente equador–polos continua prescrito e não há um modelo físico completo de instabilidades baroclínicas ou frentes de latitude média. A existência de circulação variável não garante distribuição realista da chuva.

`HadleyCoupledSettings` permite desligar a evolução térmica, desligar a reciclagem do solo ou variar a residência das nuvens para testes isolados. Também permite zerar a umidade inicial (padrão 55% da capacidade) e a água inicial do solo (padrão 12 unidades). Os checks verificam que cada opção afeta seu processo, conservação em um mundo totalmente seco, transporte de água de origem oceânica para terra inicialmente seca, determinismo, cancelamento, estabilidade em 512×256 e isolamento do gerador padrão. O JSON registra vapor e nuvens médios durante a amostragem por latitude e por distância da costa. Esses estoques não identificam a origem da água. As distâncias são células da grade reduzida, não quilômetros; as estatísticas usam células com maioria terrestre e não são ponderadas pela área física do planeta.

As próximas capturas e o JSON ficam em `results/hadley-coupled-humid-climate-comparison`, preservando o ensaio acoplado anterior em `results/hadley-coupled-climate-comparison`. Esse ensaio anterior usava reposição aquática de 0,18, alvo aquático de 80% e alvo do solo de 75%; seus números e imagens não representam os parâmetros novos. O teste não salva `.world` experimentais: o manifesto v63 continua representando o gerador padrão, e os mapas experimentais não devem ser persistidos como se fossem reprodutíveis pela geração normal. O método `CreateHadleyClimateExperiment` é interno e não está conectado ao formulário de criação nem à regeneração normal.
