# Redes fluviais v21 — planícies e mapas grandes

## Problema

Em planícies e depressões preenchidas virtualmente, o desempate por distância hexagonal produzia frentes retas. Muitos cursos paralelos atingiam o limiar de exibição e apareciam como um pente. O efeito ficava ainda mais evidente porque cada célula atravessada por rio podia receber uma faixa de campo fértil.

## Alteração

- Um microrelevo determinístico de até ±45 m é amostrado somente na superfície auxiliar de drenagem. Ele fornece vales de concentração em planícies quantizadas, sem modificar a altitude editável, relevo ou máscara terra/água.
- O preenchimento mantém a prioridade pela altitude e usa resistência espacial contínua e positiva para desempatar caminhos nas áreas planas. O ruído é determinístico e contínuo na costura horizontal. Ele apenas orienta a drenagem auxiliar, sem alterar altitude ou máscara de água.
- Depois do preenchimento, cada célula terrestre escolhe o vizinho admissível mais baixo da superfície auxiliar. A árvore usada pelo flood serve para garantir saída; ela deixou de ser usada diretamente como rede final. Empates preservam uma ordem de drenagem acíclica.
- O limiar de fluxo visível acompanha a resolução: fator máximo entre 1 e `sqrt(height/256)` sobre o limiar anterior.
- Nascentes visíveis precisam de comprimento de curso suficiente: pelo menos `max(6, height/128)` passos a jusante elegíveis.
- Nascentes próximas são espaçadas por `max(3, height/256)` hexes. A seleção prioriza cursos longos.
- Cada nascente aceita mantém seu caminho completo até o destino/confluência elegível. A seleção visual não interrompe arbitrariamente o tronco.
- O acúmulo hídrico acontece antes dessa seleção. A água dos pequenos cursos invisíveis continua contribuindo para os rios principais. Larguras e fluxos não são calculados apenas com as nascentes exibidas.
- As receitas das versões anteriores continuam disponíveis para regeneração dos respectivos saves. A nova receita usa `GeneratorVersion = 21`.

Essas regras reduzem o excesso de canais exibidos sem converter automaticamente as planícies em lagos. Um conjunto denso de linhas não era uma nova classe de lago: era uma rede de drenagem auxiliar exibida com densidade excessiva.

## Verificação

Build sem avisos/erros. Matriz de seeds, percentuais e wrap ampliada para v21, mantendo os testes das versões 18, 19 e 20. Verificações incluem determinismo, quota de terra, caminhos sem ciclos, fluxo, save/load, locks, exterior da seleção e undo/redo.

Comparação no mapa 4096×2048 utiliza o mesmo terreno e o mesmo clima, recalculando somente rios pelas receitas v20 e v21. Métricas e capturas da mesma região em `results/river-network-v21`. Log: `.godot/v21-river-maximum.log`.

Para experimentar, criar um mundo novo v21. Abrir um mundo antigo preserva seus rios salvos; salvar não os substitui silenciosamente.


## Resultado medido no mapa máximo

Mesmo terreno e clima, seed 1234, 4096×2048:

| Métrica | Receita v20 | Receita v21 |
|---|---:|---:|
| Trechos entre nascentes/confluências/destinos | 86.386 | 14.061 |
| Hexes com aresta fluvial | 1.302.688 | 241.401 |

Redução de aproximadamente 81,5% nos hexes ocupados pela rede. Contagem de trechos não equivale a número de rios independentes, pois afluentes e confluências dividem uma rede em entidades.

As capturas `dense-v20.png` e `dense-v21.png` mostram a mesma posição e zoom. A ocupação excessiva diminuiu; alguns alinhamentos longos com direções da grade ainda aparecem em planícies. A alteração não promete eliminar toda direção preferencial da grade hexagonal.

Build e smoke final aprovados. Geração v21 completa do máximo: etapa de rios terminou em 103,2 s de tempo total; o teste ainda inclui o recálculo v20 e várias instalações/capturas para comparação. Seu tempo total de comparação não representa o tempo normal de criação de um mapa.
