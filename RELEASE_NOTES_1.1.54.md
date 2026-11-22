# Jota Fantasy Map Maker Alpha 1.1.54

Esta versão reúne as mudanças desde a Alpha 1.1.32. O executável portátil para Windows x64 inclui o runtime .NET. Mapas novos usam o gerador v54.

- **Biomas e habitats.** Novos tipos de floresta, áreas áridas, manguezais, turfeiras, oásis e salares. Cânions e rochas de badlands têm arte própria.
- **Relevo e tectônica.** Revisão de planaltos, escarpas, bacias e contatos entre placas. As geleiras polares se ligam às bordas e diminuem antes das laterais. Vulcões se destacam sobre o relevo e a área vulcânica acompanha o terreno.
- **Ilhas e continentes.** Mapas novos preservam ilhas naturais e ganham ilhotas de 1 hex e ilhas de 3–9 hexes. No atlas, arcos mais finos e microplacas com influência variada formam arquipélagos com vários tamanhos. As placas continentais tendem a ficar mais afastadas. Ainda podem surgir canais estreitos em algumas seeds.
- **Regiões.** Camada com continentes, ilhas, arquipélagos, oceanos, mares e lagos, com seleção, renomeação e persistência no arquivo. Dois oceanos laterais orientam as bacias marinhas. O cercamento por terra continental distingue mares de água aberta.
- **Renderização.** A transição costeira atravessa a borda dos hexes em mares e lagos, preservando o centro das ilhotas. Artes de relevo e ícones revisadas.
- **Interface.** Menus com madeira e Faerie Moot, seed clicável para copiar e controles de terra e costa de 0 a 100%. Novos padrões de **25% de terra** e **80% de costa**, com passos de 5 e 10 pontos percentuais.
- **Saves e editor.** Catálogos de regiões são opcionais. Nomes e IDs podem ser reaproveitados após edições. Arquivos antigos continuam usando a receita registrada para regeneração regional. Mapas que já foram salvos com a fragmentação da v51 precisam de nova geração para obter a geografia atual.

O novo modelo climático ainda está pendente.

## Desempenho nos seis tamanhos do modo Mundo

Uma execução por preset com terreno completo, seed 1234, três continentes, 25% de terra e 80% de costa. Ryzen 7 5700X, GeForce RTX 5070, Windows e Godot 4.7.2 em OpenGL, com capturas de 1280×800. A geração inclui os rios. A coluna de renderização mede a preparação dos dados gráficos e o primeiro desenho do mapa. A memória é o pico do processo, incluindo o motor.

| Preset | Dimensões do atlas | Hexes ativos | Geração | Renderização inicial | Pico de RAM | Captura |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| 128×64 | 192×96 | 16.212 | 0,35 s | 2,55 s | 630 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-128x64.png) |
| 256×128 | 384×192 | 64.836 | 1,24 s | 2,58 s | 644 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-256x128.png) |
| 512×256 | 768×352 | 237.774 | 4,54 s | 3,19 s | 693 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-512x256.png) |
| 1024×512 | 1536×704 | 951.104 | 14,70 s | 3,86 s | 745 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-1024x512.png) |
| 2048×1024 | 3072×1408 | 3.804.408 | 53,45 s | 9,66 s | 1.359 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-2048x1024.png) |
| 4096×2048 | 6144×2784 | 15.044.776 | 199,37 s | 34,82 s | 3.104 MiB | [PNG](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/atlas-4096x2048.png) |

Os dados brutos estão no anexo [performance-atlas-v54.json](https://github.com/HookandRoll/meuappv1.0/releases/download/v1.1.54-alpha/performance-atlas-v54.json). Para repetir uma medição no Godot, passe `-- --benchmark-size=128 --release-atlas` e troque a largura pelo preset desejado.
