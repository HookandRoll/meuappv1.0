# Árvores no zoom máximo e desempenho

Conferência em 24/11/2026, gerador v73. O objetivo desta alteração é facilitar a leitura do terreno no zoom máximo, mantendo a cobertura da visão distante.

## Ajuste visual

- A compactação anterior, no canto superior esquerdo do hex, continua ativa.
- Entre zoom 28 e 44, uma máscara reduz gradualmente a opacidade de aproximadamente metade dos grupos de árvores. No zoom 44, os grupos selecionados ficam ocultos.
- A seleção usa hash das coordenadas e da seed: mover a câmera ou reabrir o mesmo mundo preserva os grupos selecionados. A proporção de 50% é estatística, sem cota exata em cada enquadramento.
- A máscara atua nas artes de árvores temperadas, tropicais, taiga, savana, pântano, oásis, mangue e floresta sazonal. Arbustos e juncos continuam visíveis.
- A alteração é visual, sem modificar os dados do mundo ou o formato do save.

Capturas do mesmo enquadramento, com a máscara desativada e ativada, nos zooms 22, 36 e 44, em `results/vegetation-zoom-m4/zoom-*-before.png` e `zoom-*-after.png`. Inspeção das capturas em 36 e 44 confirmou o desaparecimento gradual dos grupos selecionados e a preservação de terreno, relevo e ícones.

## Desempenho

Uma execução local, mapa Região 1024×512, 524.288 hexes, seed 1234, três continentes e 38% de terra. Godot 4.7.2 .NET, build Debug, OpenGL Compatibility, RTX 5070, janela 1280×800, VSync desligado. Cada enquadramento mede 180 frames de movimento. O modo adicional inclui zoom máximo fixo em 44.

| Medida | Resultado |
|---|---:|
| Visão geral, FPS médio | 512,1 |
| Zoom intermediário, FPS médio | 862,4 |
| Zoom máximo, FPS médio | 1.148,1 |
| Zoom máximo, p95 por frame | 1,06 ms |
| Pior troca de camada | 50,12 ms |
| Upload integral | 1,99 ms |
| Edição, p95 | 1,96 ms |
| Geração | 29,13 s |
| Salvar | 0,112 s |
| Abrir e verificar igualdade | 0,175 s |
| Memória gerenciada retida pelo mapa | 23,36 MiB |

Os limites obrigatórios de navegação (30 FPS), camadas (100 ms), geração (60 s), abertura (5 s) e memória do mapa (200 MiB) para 1024×512 foram atendidos. O alvo ideal de troca de camada abaixo de 16 ms não foi atingido em todas as camadas. Save/load preservou os arrays e as entidades esparsas. O processo encerrou com código 0.

São medições desta máquina e deste mapa, sem comparação controlada de desempenho com a versão anterior e sem repetir a matriz de tamanhos. As capturas antes/depois desativam somente a máscara, sobre o mesmo mundo.

## Verificação da entrega

- PASS, escopo visual: capturas reais confirmam árvores ocultas no zoom máximo e transição parcial no zoom 36.
- PASS, propósito: clareiras visuais permitem ler terreno e detalhes; a máscara fica zerada até zoom 28.
- PASS, identidade: artes, cores e distribuição compacta existentes foram preservadas; nenhuma arte nova foi criada.
- PASS, execução: build concluída, shader renderizado, benchmark e round-trip aprovados. `git diff --check` sem erros.

A build registrou NU1900 por indisponibilidade da consulta de vulnerabilidades do NuGet. O Godot registrou avisos de erro ao ler certificados do sistema e gravar cache do shader; não houve erro de compilação do shader, e a renderização e o benchmark concluíram.

## Reprodução

```powershell
dotnet build Mapmaker.csproj --no-restore
& '.\Godot_v4.7.2-stable_mono_win64_console.exe' --path . --log-file .godot/vegetation-zoom-m4.log -- --benchmark-size=1024 --vegetation-zoom-review
```

Métricas completas em `results/vegetation-zoom-m4/1024x512.json`; log em `.godot/vegetation-zoom-m4.log`.
