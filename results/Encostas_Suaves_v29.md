# Encostas tectônicas suaves — v29

19/11/2026. Mudança local no projeto, sem atualização dos executáveis em `dist`.

O campo de elevação das cordilheiras ganha um alcance 1,65 vezes maior, mantendo a mesma intensidade da crista, a variação de largura e a queda suave por distância. O campo anterior continua como limite inferior. O novo perfil amplia a transição para as encostas sem diminuir nenhuma altitude existente. A escultura continental usa seu alcance original: as costas e a distribuição de água não mudam.

A suavização atua nos dados físicos de elevação, usados também por clima e drenagem. Não altera o shader nem reativa a atenuação da v27. Receitas antigas permanecem disponíveis pela versão do mundo.

Comparação com seed 1234 e três continentes. Desníveis medidos entre vizinhos terrestres junto às montanhas existentes da v28, usando a mesma seleção nas duas versões. P95 é o percentil 95 desses desníveis, em metros.

| Mapa / terra | P95 v28 → v29 | Média v28 → v29 | Picos v28 → v29 |
|---|---:|---:|---:|
| 512×256 / 40% | 620 → 415 | 158,2 → 118,0 | 1.884 → 2.810 |
| 512×256 / 50% | 660 → 422 | 161,5 → 118,1 | 2.494 → 3.657 |
| 1024×512 / 38% | 463 → 319 | 114,2 → 81,4 | 4.988 → 8.107 |

A área de picos aumenta porque as encostas altas ficam mais largas; nenhuma cota anterior é reduzida. Os três casos passaram as verificações de igualdade exata da água, preservação de altitude e ausência de términos fluviais desconhecidos sem pai. Terreno e Elevação foram capturados no renderer real e inspecionados. Artefatos em `results/gentle-ridges-v29`, gerados com `--gentle-ridge-review`.

Build sem avisos ou erros. Testes gerais em `.godot/v29-smoke.log`; comparação em `.godot/v29-gentle-review.log`. Novos mapas usam v29. Saves antigos conservam os dados e sua versão; há mapas v29 prontos no diretório da comparação.

Smoke completo aprovado: 524.288 hexes, save/load, geração regional, locks e histórico do editor, incluindo a matriz v18–v29 com três seeds, três percentuais de terra e wrap ligado/desligado.
