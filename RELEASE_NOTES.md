# Jota Fantasy Map Maker — Alpha 1.1.32

Executável portátil para **Windows x64**: `Jota-Fantasy-Map-Maker-Alpha-1.1.32.exe`, com recursos e runtime .NET incorporados. Não exige Godot ou SDK instalados para jogar/editar mapas.

Todas as mudanças desde a **Alpha 1.1.31**. Mapas novos usam o **gerador v45**, reunindo as revisões v41–v45 e as melhorias do formato e do renderer. [Release anterior](https://github.com/HookandRoll/meuappv1.0/releases/tag/v1.1.31-alpha).

## Formato de mapas e preparação para expansão

- Novo formato de save **v2**, com camadas independentes dentro do arquivo `.world`.
- Biomas, relevo, formas geográficas e ícones usam IDs de 16 bits; características geográficas têm 64 flags combináveis. A estrutura permite acrescentar classificações e artes sem reutilizar bits dos IDs.
- Forma geográfica separada do relevo visual: um planalto pode continuar plano, com bioma e características próprios.
- Camadas e campos novos preservados ao copiar, editar, desfazer/refazer, regenerar e salvar/abrir. Locks independentes para forma geográfica e características.
- Leitura e migração de saves v1 para v2 ao salvar, com valores padrão para camadas opcionais e tratamento de locks antigos. Abrir não regenera o mundo.
- Novos planos de dados, catálogo de artes e prévia de edição na GPU preservam IDs altos e flags simultâneas, incluindo o bit 63, sem colisões com metadados visuais.
- **Arquivos salvos em v2 precisam desta versão ou de outra com suporte a v2 para serem abertos.** A receita do gerador de cada mapa continua preservada.

## Relevo, planaltos e formas geográficas

- Classificação após calcular as altitudes, usando altitude, subida, queda e os seis vizinhos, com alcance regional que acompanha a resolução e ajuste pela distância física dos hexes.
- Planícies, terrenos ondulados, planaltos/chapadas, escarpas/serras, morros, montanhas, picos, vales/depressões, encostas e sopés identificados separadamente.
- Altitude alta sozinha não preenche um topo plano com montanhas. As quebras de declividade distinguem cristas e escarpas das áreas baixas ao redor.
- Pincéis de elevação e água e a edição numérica atualizam o relevo e a forma geográfica dos hexes afetados e vizinhos dependentes, com locks e uma operação de undo/redo.
- Regeneração do campo **Forma geográfica** e preservação do relevo/forma ao escolher manter água e elevação.
- Inspeção mostra a classificação geográfica derivada.

## Composição das montanhas e escarpas

- Símbolos acompanham quebras de declividade e bordas altas, com contornos mais contínuos e arte própria de escarpa em três variantes.
- Prolongamentos curtos para o lado alto dão volume ao contorno, sem desenhar somente uma linha.
- Regiões montanhosas e interiores altos/alpinos também recebem grupos de montanhas, com clareiras entre eles.
- Montanhas comuns se concentram em manchas regionais e usam menos peças que as regiões de picos. A escolha da arte não altera a classificação ou altitude salva.
- Artes junto aos contornos têm tamanho, posição e sobreposição controlados para evitar cobrir extensamente o interior dos planaltos baixos.

## Rios e vegetação

- Rios abrem um corredor visual que recorta vegetação e ícones próximos ao leito, inclusive artes vindas de hexes vizinhos.
- Máscara e rio visível compartilham a geometria, com suporte a larguras, curvas, junções, costa, pan, zoom e redimensionamento.
- O recorte preserva o chão, o relevo e os dados de hidrologia; é limpo corretamente quando não há rios.
- Ao aproximar o zoom, a vegetação passa gradualmente para uma composição compacta no canto superior esquerdo do hex, liberando o centro e a direita para os detalhes.

## Clima, chuva e desertos

- Aplicada a fórmula térmica da especificação de clima: base de **26,5 °C no equador**, aproximadamente **11,1 °C a 45°** e **−28 °C nos polos**, antes da altitude, influência marítima e variação regional.
- Oceano modera a temperatura em direção a um alvo dependente da latitude, em vez de aquecer os polos em direção a +12 °C.
- Variação regional menor e amortecida sobre o oceano; gradiente vertical de −6,5 °C por 1.000 m preservado.
- Chuva com pico equatorial, cinturão subtropical seco, segundo pico temperado e ressecamento polar/térmico.
- Condensação orográfica mais forte, menor recuperação artificial da umidade no interior e sombra de chuva mais seca atrás das serras.
- Desertos passam a surgir nas áreas quentes e áridas, preservando florestas tropicais onde há calor e chuva suficiente.

## Altitude e cumes

- Calibração da cauda superior das cordilheiras ativas para máximos de **6.000–8.000 m por seed**, com altitudes extremas progressivamente mais raras.
- O ajuste atua após a suavização e se concentra nos cumes; terras até 800 m, água e áreas sem levantamento tectônico suficiente permanecem intactas.
- Na validação de três seeds, os máximos chegaram a 6.164, 7.664 e 7.360 m, com apenas 0,02%–0,31% da terra acima de 6.000 m.
- Clima e classificação de relevo usam as alturas finais; a camada Elevação representa a faixa até 8.000 m.

## Costas, lagos, baías e ruído

- Remodelagem rasa de margens com altitude e ruído espacial em várias escalas. Trocas entre terra e água preservam a área de cada massa terrestre e as conexões de terra e água.
- Proteção de ilhas, istmos e passagens: o passe de margens não une lagos separados nem fecha saídas. Polos e margens oceânicas obrigatórias continuam protegidos.
- Canais de separação continental têm largura variável por distância ponderada por ruído, com núcleos terrestres protegidos.
- Depressões tectônicas divergentes variam em eixo, largura e intensidade, reduzindo o aspecto tubular das grandes enseadas e lagos alongados. Esses corpos podem continuar existindo por razões tectônicas.
- Ruído visual deformado em três escalas nas costas, lagos e transições de biomas; menos arredondamento e faixa de blend mais estreita.
- Ruído fino atenuado à distância para reduzir cintilação; fase determinada pela seed e wrap horizontal preservado.
- Terreno, rios e máscara de recorte compartilham a mesma função de deslocamento costeiro.

## Biomas, propagação e pântanos

- Passe de ruído também nos dados das bordas dos biomas, preservando clima, água, praias e locks.
- Nova propagação por maioria dos vizinhos em três escalas, regional até local, para absorver faixas estreitas que escapavam da consolidação por área pequena.
- A troca exige maioria de pelo menos quatro vizinhos e adequação climática. Empates preservam o bioma; uma célula absorvida não oscila de volta na passagem seguinte.
- A propagação não atravessa água e preserva praia, alpino, swamp, locks, áreas abertas e cinturões climáticos legítimos.
- **Pântano arborizado (`Swamp`)** integrado com chão e árvores próprios, três variantes de cada, detalhes, nome na inspeção e opção no editor.
- Pântanos ocupam baixadas quentes e úmidas junto a rios, lagos rasos e enseadas abrigadas, com distribuição irregular. Água profunda, margens frias, secas, altas ou íngremes não viram pântano apenas pelo formato.
- O swamp não altera água, altitude nem a cota de terra/mar. A margem de regeneração inclui os alcances extras para evitar emendas.

## Recursos e trabalho técnico

- Novas especificações, relatórios de implementação, capturas comparativas e artes adicionais para expansão dos biomas e características.
- Escarpa e swamp estão integrados; as demais artes preparadas para futuros biomas/features não significam que todos esses sistemas já estejam ativos.
- `GeneratorChecks` executa dentro de `RunChecks(args)` com `try/catch`: falhas imprimem a exceção completa e stack trace no **stderr** e retornam **código 1**, evitando a exceção .NET sem tratamento que disparava o pop-up do Windows e deixava o processo preso.
- Ampliação dos testes de formato, classificação, contornos, grupos de montanhas, clima, altitudes, ruído de margens, pântanos, maioria de biomas, locks, compatibilidade e regeneração.

## Compatibilidade e uso

- A nova geração de biomas, clima, relevo, margens e geografia usa **v45 em mapas novos**. Abrir um mapa antigo preserva seus dados e sua receita de regeneração.
- Melhorias de apresentação, como grupos de montanhas, ruído visual e recorte de rios, aparecem ao reabrir mapas existentes nesta build.
- Conservadas as cotas exatas de terra, exclusão do oceano polar do denominador, placas polares/laterais extras, domínio curvo do Mundo e modo Região retangular.

## Validação da release

- Build Release e suíte completa do núcleo.
- Prova de falha controlada do harness: stack trace no stderr e saída 1.
- Executável exportado testado em pasta isolada, incluindo smoke de 524.288 hexes, save/load e edição.
- Conferências gráficas do formato GPU, relevo/editor, recorte de rios e geração atual.

[Formato v2](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.32-alpha/docs/Map_Data_Format_v2.md) · [Clima e altitude](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.32-alpha/results/Clima_Altitude_v43.md) · [Bordas e pântanos](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.32-alpha/results/Bordas_Pantanos_v44.md) · [Maioria dos biomas e grupos de montanhas](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.32-alpha/results/Maioria_Biomas_Agrupamentos_v45.md).
