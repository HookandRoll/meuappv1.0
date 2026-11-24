# Vegetação, umidade e três células: etapa v68

Registro histórico: a implementação v68 foi removida a pedido do usuário e substituída pelo retorno de chuva terrestre às nuvens, receita v69. Os números abaixo descrevem somente o experimento anterior. A build Alpha 2.0.5 continua disponível para abrir saves v68; o código atual não recalcula essa receita.

Data: 24/11/2026. Decisão local: manter v67 como padrão e oferecer v68 como experimento opcional na Alpha 2.0.5. A mistura horizontal trouxe ganhos nas três seeds; a formulação vegetal conserva água, mas ainda retém mais água no solo/raízes e recicla menos que v67 durante a janela padrão.

## Seeds no tamanho máximo

Seeds `1155827384`, `1234` e `-77`; referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos, atmosfera 512×256, três continentes, 25% de terra, costa 80, sem offsets. Cada variante completa o acabamento de biomas, os rios e os biomas fluviais.

As comparações aceitas exigem hash SHA-256 idêntico de elevação, água e temperatura de referência. Uma primeira tentativa da seed 1155827384 foi interrompida por divergência desse hash e descartada. A repetição instrumentada passou para v66/v67/v68, e a execução da formulação vegetal final voltou a corresponder à superfície de referência. A divergência inicial não foi reproduzida nem teve sua causa identificada; estes resultados não certificam determinismo universal da geração. Logs: `climate-matrix-1155827384.log`, `climate-surface-max-debug.log` e `climate-matrix-final-1155827384.log`.

O indicador de interior usa maioria de terra, altitude terrestre média até 300 m e distância de pelo menos três células atmosféricas de água. As amostras têm 9.873, 9.137 e 6.481 células, respectivamente. Chuva abaixo de 260 indica secura atmosférica, sem equivaler a um bioma final. Médias na grade não são ponderadas por área esférica.

| Seed | Receita | Chuva média no interior | Interior abaixo de 260 |
| --- | --- | ---: | ---: |
| 1155827384 | v66, sem mistura | 926,77 | 32,01% |
| 1155827384 | v67, mistura 0,08 | 969,51 | 20,68% |
| 1155827384 | v68, vegetação | 945,59 | 21,56% |
| 1234 | v66 | 1151,88 | 41,55% |
| 1234 | v67 | 1213,41 | 28,39% |
| 1234 | v68 | 1195,88 | 30,17% |
| -77 | v66 | 557,43 | 60,76% |
| -77 | v67 | 649,84 | 32,06% |
| -77 | v68 | 616,75 | 36,52% |

## Biomas finais

Percentual dos hexes terrestres ativos. Denominadores: 3.734.788 para 1155827384, 3.843.223 para 1234 e 3.821.766 para -77. Florestas seguem `Biomes.IsForest`: floresta temperada, selva, taiga, pântano (`Swamp`), floresta sazonal, floresta temperada chuvosa e mangue. Os JSONs preservam as contagens de todos os biomas, inclusive os não apresentados aqui.

| Seed | Receita | Deserto | Badlands | Estepe | Florestas |
| --- | --- | ---: | ---: | ---: | ---: |
| 1155827384 | v66 | 10,13% | 4,49% | 10,98% | 39,81% |
| 1155827384 | v67 | 10,02% | 3,63% | 8,45% | 42,52% |
| 1155827384 | v68 | 10,48% | 3,80% | 9,63% | 41,06% |
| 1234 | v66 | 16,84% | 5,05% | 8,58% | 41,49% |
| 1234 | v67 | 14,63% | 4,32% | 8,39% | 43,90% |
| 1234 | v68 | 16,60% | 4,81% | 8,40% | 42,47% |
| -77 | v66 | 29,46% | 5,67% | 14,71% | 24,43% |
| -77 | v67 | 28,89% | 4,70% | 12,67% | 27,38% |
| -77 | v68 | 32,82% | 5,13% | 11,58% | 25,39% |

A v67 reduz os três grupos secos e aumenta as florestas nas três geografias. Na seed 1155827384, por exemplo, o número de desertos cai somente de 378.318 para 374.271 hexes, enquanto as florestas crescem de 1.486.656 para 1.588.148. A queda de 11,32 pontos percentuais no indicador atmosférico seco não pode ser interpretada como uma queda equivalente de desertos finais.

## Reciclagem vegetal

A implementação final usa cobertura contínua condicionada à água e à temperatura, reserva de raízes inicialmente vazia, transferência conservativa solo/raízes e transpiração limitada pela reserva. As raízes devolvem água ao solo quando sua capacidade diminui. Nenhum bioma seco remove vapor atmosférico. Os biomas finais não entram como causa do clima que os classifica.

As duas parcelas de evaporação são somadas em `SoilEvaporation`. `SoilWater` nos resumos inclui solo e raízes; `rootWater` detalha a parcela nas raízes, portanto não deve ser somada novamente a `SoilWater`. A tabela usa unidades normalizadas do solver e evapotranspiração acumulada em toda a execução, incluindo aquecimento.

| Seed | Água terrestre final v67 → v68 | Evapotranspiração v67 → v68 |
| --- | ---: | ---: |
| 1155827384 | 1.136.164 → 1.263.899 | 147.344 → 99.186 |
| 1234 | 1.144.835 → 1.327.445 | 181.205 → 124.873 |
| -77 | 840.971 → 954.193 | 171.808 → 101.718 |

Esses valores sustentam manter v68 opcional: maior armazenamento não se converteu em maior reciclagem na janela padrão. A primeira formulação, com evaporação direta menor, piorou mais esse efeito e foi descartada antes da build. Os resultados v68 deste relatório e o EXE usam a formulação final de `HadleyCoupledClimateSimulation.Vegetation.cs`.

## Ferrel e Polar

Sensibilidade na seed 1234 máxima, sobre a mesma superfície, com Hadley 1,0. Uma intensidade muda por vez; os demais parâmetros permanecem iguais à v67. Valores de chuva incluem a conversão comum 10,5 e antecedem o teto de visualização. Estas execuções não reclassificam biomas finais para cada intensidade.

| Ferrel / Polar | Chuva 35–60° | Chuva 60–90° | Chuva no interior baixo | Interior seco |
| --- | ---: | ---: | ---: | ---: |
| 0,8 / 0,6, referência | 936,16 | 414,19 | 1213,41 | 28,39% |
| 0,6 / 0,6 | 766,34 | 411,57 | 1182,04 | 28,21% |
| 1,0 / 0,6 | 1191,75 | 410,76 | 1262,26 | 30,11% |
| 0,8 / 0,4 | 928,25 | 427,66 | 1217,69 | 26,54% |
| 0,8 / 0,8 | 942,51 | 394,15 | 1207,91 | 30,67% |

Ferrel mais forte aumenta a chuva média temperada, mas também aumenta a fração seca desse interior. Polar 0,4 reduz o indicador seco nesta seed, sem estabelecer uma calibração válida para outras geografias ou um alvo meteorológico. Mantidos 0,8 e 0,6; o comparativo específico está feito, mas escolher novos padrões requer ampliar essa sensibilidade para outras seeds e definir o critério de calibração.

## Estabilização

Seed 1234 máxima, v67, passo externo 0,25. Todas as execuções começam do mesmo estado inicial e usam uma janela de 240 passos, com 60 unidades de tempo. O aquecimento de 240, 480, 960 e 1680 passos corresponde a 1, 2, 4 e 7 períodos do forçamento sazonal. Assim, as janelas têm a mesma fase. Não são anos meteorológicos calibrados.

| Aquecimento | Amostragem | Chuva média no interior | Interior seco |
| ---: | ---: | ---: | ---: |
| 240 | 240 | 1213,41 | 28,39% |
| 480 | 240 | 1302,21 | 28,09% |
| 960 | 240 | 1312,87 | 29,20% |
| 1680 | 240 | 1291,67 | 27,97% |

Das 2.594 células secas na janela padrão, 1.844 continuam secas na janela mais longa: 71,09%. A média cresce 6,45%, enquanto a fração seca cai somente 0,42 ponto percentual. Aumentar o tempo não elimina o interior seco nesse caso.

Entre as janelas de 960 e 1680 passos, a média cai 1,61%, mas a diferença RMS de chuva por célula do interior é 335,33 unidades. As médias se aproximam de um patamar, enquanto a distribuição espacial continua mudando. Isso não demonstra convergência completa do clima. A v68 ainda precisa do mesmo comparativo prolongado; os resultados de duração acima são da v67.

## Diagnóstico visual

Arquivos por variante em `results/climate-matrix/<seed>/`: CSV por célula, JSON atmosférico, contagens finais de biomas, PNGs e limites de escala em `scales.json`. Os mapas de vapor, nuvens e evaporação mostram médias/taxas da janela; solo, raízes e cobertura mostram o estado final. Cada campo usa a mesma escala entre as variantes da mesma seed. A primeira linha da grade fica no norte; o desenho não usa a projeção curva do Atlas.

Exemplos da seed 1155827384:

| Vapor v67 | Vapor v68 |
| --- | --- |
| ![Vapor v67](climate-matrix/1155827384/v67-vapor.png) | ![Vapor v68](climate-matrix/1155827384/v68-vapor.png) |

| Evapotranspiração v67 | Evapotranspiração v68 |
| --- | --- |
| ![Evapotranspiração v67](climate-matrix/1155827384/v67-soilEvaporation.png) | ![Evapotranspiração v68](climate-matrix/1155827384/v68-soilEvaporation.png) |

| Solo v68 | Raízes v68 |
| --- | --- |
| ![Solo v68](climate-matrix/1155827384/v68-soil.png) | ![Raízes v68](climate-matrix/1155827384/v68-roots.png) |

Solo e raízes são campos distintos, cada um com seu próprio limite de escala. As imagens exportadas já ajudam a localizar reservas e fluxos; ainda falta integrá-las ao seletor de camadas da interface.

## Verificações

- PASS: água finita, conservação e positividade no armazenamento radicular e na transpiração; ausência de água criada em sistema vazio; devolução de reservas quando a cobertura diminui.
- PASS: seleção v68, determinismo, criação, save/load e regeneração completa; receita v67 idêntica ao controle sem vegetação.
- PASS: testes de transporte v66/v67, três células, Hadley v64 e regressões acopladas de evaporação, relevo, calor, nuvens, solo e estabilidade 512×256.
- PASS: 48 mundos de referência idênticos bit a bit em todas as camadas, rios, regiões e ícones.
- PASS: todas as execuções aceitas da matriz conservam água com erro relativo menor que 1e-12, abaixo do limite 1e-5, sem aplicações do limitador térmico de emergência.
- PASS: EXE portátil criou v68 com a opção explícita e v67 sem ela; ambos salvaram, reabriram e reconstruíram os mesmos hashes de vento. As duas execuções encerraram com código 0 e stderr vazio. Captura de biomas inspecionada.

## Build e pendências

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.5-Experimental-Vegetacao.exe`, 206.120.400 bytes, versão 2.0.5.0. SHA-256: `C7E85CE252C6E46C85E37BE006445C33FA90CD806A66907F2C172E411EA74AEC`.

`dist/Iniciar-vegetacao-experimental.cmd` ativa a receita v68. Abrir o EXE diretamente preserva v67 como padrão. Projeto e assembly separados: `MapmakerVegetation.csproj`. Logs de exportação e interface em `vegetation-export-contained.log`, `vegetation-ui.log` e `vegetation-ui-default.log`.

A build foi exportada localmente e testada. A restauração registrou indisponibilidade da consulta de vulnerabilidades NuGet neste ambiente; a compilação e a exportação concluíram. A exportação usou uma cópia do editor com cache dentro de `results/godot-editor`, pois o sandbox não permitia gravar configurações do editor no perfil do Windows. O autoteste do EXE recebeu acesso ao cache normal de extração do runtime .NET.

Continuam pendentes: duração prolongada da v68, sensibilidade de Ferrel/Polar em outras geografias, integração dos diagnósticos na interface, investigação da divergência inicial de hash e publicação no GitHub. Nenhum commit, push ou release foi feito nesta etapa.
