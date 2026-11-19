# Cordilheiras v26 — referência aprovada v19

Data: 19/11/2026. Alterações locais, sem publicação de release.

## Direção adotada

As imagens fornecidas pelo usuário (`results/natural-v19/land-40-layer-6.png` e `land-50-layer-6.png`) passam a ser a referência visual das cordilheiras. Não se trata de aumentar novamente uma faixa de largura quase fixa: a v26 recupera o perfil contínuo e irregular da v19, com variação de largura, estreitamentos e alargamentos ao longo das cadeias.

## O que foi alterado

- Desativados, para mundos novos v26, os núcleos estreitos separados e o tratamento geral de elevação introduzidos nas versões 24/25. O código permanece disponível para reproduzir os saves dessas versões.
- Restaurados o perfil de crista e o deslocamento de subducção da v19. O contorno volta a resultar do campo contínuo, do relevo e do clima, em vez de uma restrição que força uma faixa fina e regular.
- Em tamanhos maiores, o alcance e o deslocamento da crista acompanham a escala média das placas. Isso evita que a maior quantidade de placas mantenha cordilheiras com alcance proporcional ao continente inteiro, acumulando grandes massas elevadas. Em 512×256 com 21 placas, a escala coincide com a referência v19.
- Domos continentais de fundo continuam podendo gerar terras altas, mas sua elevação é comprimida suavemente fora da influência de cristas. Isso reduz a possibilidade de manchas alpinas independentes nas partes internas das placas. Dentro das cristas aprovadas, o perfil v19 é preservado.
- Mantidos o transporte contínuo de umidade da v25 (correção das listras), os cinco níveis fluviais, as curvas/confluências e a correção dos términos de rios.

## Comparação direta

Mesma seed 1234, dimensões 512×256, três continentes, detalhe costeiro 50 e geração completa:

| Terra | Versão | Hexes alpinos | Hexes Peak |
|---|---:|---:|---:|
| 40% | 19 | 1.042 | 1.884 |
| 40% | 25 | 1.512 | 1.606 |
| 40% | 26 | 1.044 | 1.884 |
| 50% | 19 | 1.021 | 2.494 |
| 50% | 25 | 1.512 | 1.753 |
| 50% | 26 | 1.023 | 2.494 |

A sobreposição dos hexes alpinos com a v19 (interseção/união) é de **97,91% em 40% de terra** e **99,80% em 50% de terra**. Essa comparação verifica posição e formato, além da contagem. Pequenas diferenças são esperadas porque costas e clima posteriores à v19 continuam ativos.

As seis capturas foram geradas no renderer real, com a mesma câmera. Arquivos em `results/ridges-v26`: `land-40-v19.png`, `land-40-v25.png`, `land-40-v26.png` e os equivalentes de 50%. Os mundos v26 também estão salvos no diretório.

## Validação

- Build sem avisos ou erros.
- Smoke com matriz de versões v18–v26, três seeds, três percentuais de terra e wrap ligado/desligado.
- Determinismo, quota, grafos fluviais sem ciclos, destinos válidos, save/load, locks, exterior da seleção e histórico do editor.
- Testes de umidade contínua e costura horizontal preservados.
- Teste visual de referência exige sobreposição alpina mínima de 95% com a v19 nos dois casos aprovados.

Logs: `.godot/v26-final-smoke.log`, `.godot/v26-reference-final.log` e `.godot/v26-maximum-final.log`.

Para avaliar o resultado, criar um mundo novo v26. A abertura de saves anteriores preserva seu terreno, clima, rios e versão de regeneração.


## Escala máxima

A execução final de 4096×2048 concluiu os dados em 97,8 s e apresentou a imagem em 106,96 s, com 18.221 trechos fluviais e zero términos desconhecidos sem pai. A captura `results/generator-v26/maximum-map.png` foi conferida junto às referências de 512×256. Os tempos são de uma execução, não médias de benchmark.
