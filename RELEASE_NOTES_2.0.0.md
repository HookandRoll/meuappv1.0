# Alpha 2.0 - Wind and Climate Simulation

Jota Fantasy Map Maker para Windows x64, com runtime .NET e recursos incorporados no EXE portátil.

- **Circulação Hadley em duas camadas.** As camadas `Hadley: solo` e `Hadley: altitude` mostram circulação superficial e de retorno, troca de massa e movimento, Coriolis, atrito, deflexão pelo relevo e vorticidade. O cálculo usa grade reduzida de até 512×256 e ocorre em segundo plano. A inspeção mostra direção, velocidade, pressão, movimento vertical e vorticidade em unidades do protótipo.
- **Experimento de clima acoplado.** Os comandos diagnósticos transportam calor, vapor e água de nuvem, calculam chuva gradual, reevaporação e reciclagem pelo solo. A reposição aquática tem taxa efetiva 0,36 e alvos de 90% no oceano, 95% no oceano raso de até 200 m, 85% nos lagos e 80% nas demais fontes. O ar transporta seu histórico térmico para resfriar na subida e aquecer na descida do relevo, formando sombra de chuva sem uma máscara regional imposta.
- **Ajustes de criação.** Sliders de temperatura de −20 a +20 °C e de precipitação de −50 a +50%, ambos em passos de 1.
- **Correções e desempenho.** Cache de regiões e salvamento, snapshots comprimidos e orçamento de undo, correções no desvio dos rios e nos pincéis costeiros, paralelismo em clima e renderização, cache de drenagem e seleção de ilhas com menor custo.

## O que está disponível na interface

O formulário normal continua usando o gerador v63 e seu clima anual. As duas camadas Hadley são visualizações experimentais e não modificam automaticamente chuva ou biomas do mapa aberto. O clima acoplado completo está nos comandos de comparação descritos em [Prototipo_Hadley_Ventos_2D.md](https://github.com/HookandRoll/meuappv1.0/blob/v2.0.0-alpha/docs/Prototipo_Hadley_Ventos_2D.md). Mapas desse experimento não são salvos como arquivos `.world` do gerador padrão.

A versão do aplicativo passa a 2.0.0-alpha; a versão do gerador e o formato dos saves não mudam. Arquivos existentes continuam com sua receita de geração.

## Validação

Os ensaios incluem conservação de água e massa de ar, repetibilidade, cancelamento, estabilidade, transporte entre oceano e continente e uma cordilheira com vento controlado e sombra de chuva reversível. Os comparativos das seeds 1234 e −77 usam o Atlas máximo de 6144×2784, com controles de terreno e temperatura anual por hash e fator de conversão da chuva fixo. O arquivo de validação da release contém relatórios, JSONs e capturas de chuva, biomas e das duas camadas de vento.

O modelo atmosférico usa unidades e processos parametrizados, sem calibração meteorológica completa ou garantia de conservação da energia total.

## Downloads

- [EXE portátil Windows x64](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.0.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.0-Wind-and-Climate-Simulation.exe)
- [Relatórios, dados e imagens de validação](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.0.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.0-Validation.zip)
- [Checksums SHA-256](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.0.0-alpha/SHA256SUMS.txt)
