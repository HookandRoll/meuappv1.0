# Chuva regional, biomas coerentes e relevo suave — gerador v30

20/11/2026. Correção local do gerador. O EXE publicado como Alpha 1.1.29 continua com a receita v29; os mapas novos criados pelo projeto recompilado usam v30. As versões anteriores continuam disponíveis para reproduzir seus saves. Nenhum arquivo de arte foi alterado por esta correção.

## Diagnóstico

O degelo não escreve em `Rainfall`: acumulação de neve e fusão são usadas no escoamento anual dos rios. O acoplamento estava no clima e na classificação dos biomas:

- A chuva base descontava diretamente 0,09 mm por metro de altitude, além do transporte de umidade e da parcela orográfica posterior.
- A exposição marítima usava amostras do mesmo campo de escultura que sustenta o relevo.
- A classificação de biomas recebia perturbações adicionais de chuva e temperatura, incluindo ruído relativamente fino. Planícies físicas também podiam bloquear florestas independentemente do clima.
- O tratamento suave da v29 ampliava as cordilheiras, mas não suavizava o campo final de altitude inteiro nem os cortes costeiros criados pela seleção da quota de terra.

Na camada Chuva, azul significa mais chuva e ocre significa menos. Portanto, semelhança visual com Elevação não significa necessariamente mais precipitação nos cumes: os casos medidos da v29 tinham correlação negativa entre chuva e altitude.

## Alterações

- A chuva regional base passa a depender de latitude, variação espacial ampla e umidade transportada, sem entrada direta de altitude.
- O relevo mantém influência por condensação nas subidas e perda de umidade atrás das barreiras, com acréscimo orográfico limitado. O perfil atmosférico é suavizado antes de medir subidas.
- Transporte nos dois sentidos com mistura gradual das faixas de vento, preservando a solução periódica de umidade em mapas com wrap.
- Removido na v30 o vínculo entre planície física e proibição de floresta. Reduzida a perturbação extra dos limiares dos biomas para um sinal amplo de chuva de baixa amplitude; retirado o ruído térmico extra da classificação. Temperatura ainda depende da altitude.
- Suavização do campo físico final de elevação por duas passagens separáveis em cada eixo, com raio proporcional ao mapa, misturando 85% do campo suavizado com 15% do original.
- Transição costeira gradual por distância à água, incluindo ilhas e bordas de lagos; fundo submerso também suavizado. A máscara de água e a quota de terra permanecem exatamente iguais às da v29.
- Escrita continua respeitando região, campos e locks. Rios e clima são calculados sobre a altitude física final.

O modelo continua sendo uma aproximação de geração, não uma simulação atmosférica completa. A suavização pode reduzir cotas e a área ocupada por picos; não promete preservar cada altitude antiga. Os cumes principais e a distribuição de água foram verificados nos comparativos.

## Comparação controlada

38% de terra, três continentes, detalhe costeiro 50, sem wrap. Mesmo terreno inicial e seed em cada par.

| Dimensões / seed | Maior desnível terrestre v29 → v30 | P95 dos desníveis v29 → v30 | Correlação chuva/altitude v29 → v30 | Fronteiras de bioma v29 → v30 |
|---|---:|---:|---:|---:|
| 512×256 / 1234 | 1.181 → 711 m | 309 → 266 m | −0,425 → −0,234 | 8,64% → 6,29% |
| 512×256 / 1 | 1.101 → 852 m | 245 → 211 m | −0,337 → 0,001 | 9,08% → 4,83% |
| 512×256 / −77 | 1.034 → 840 m | 217 → 205 m | −0,231 → −0,127 | 8,45% → 6,19% |
| 1024×512 / 1234 | 859 → 510 m | 176 → 152 m | −0,486 → −0,211 | 5,28% → 3,68% |

Desníveis medidos entre vizinhos terrestres. Fronteiras de bioma são a fração das arestas terrestres que separam biomas diferentes; redução dessa medida indica menos fragmentação, sem impor um bioma único. A correlação usa apenas células terrestres. São resultados dos casos testados, não metas de contagem para todas as seeds.

No mapa 1024×512, a cota máxima ficou em 4.103 m contra 4.104 m antes; células Peak passaram de 8.107 para 7.329. A água permaneceu idêntica e houve zero términos de rio desconhecidos sem pai. Terreno, Elevação, Chuva e Biomas foram capturados no renderer real e inspecionados.

Capturas, métricas e mapas prontos: `results/climate-relief-v30`. Exemplo: `1024-seed-1234-v30.world`.

## Validação

- Build sem erros ou avisos.
- Smoke completo aprovado, com matriz v18–v30, três seeds, três percentuais de terra e wrap ligado/desligado; repetibilidade, quota, fluxo, destinos fluviais, persistência, locks e histórico.
- 384 comparações regionais aprovadas na v30, após salvar/reabrir: restauração exata de água, elevação, relevo, temperatura, chuva e bioma; preservação dos demais campos e exterior da seleção; inclui chunks parciais e wrap.
- Editor v30 com renderização real aprovado: fluxo integrado de pintura, locks, undo/redo, salvar/cancelar/reabrir, mais 20 operações de regeneração. A regeneração de rios foi exercitada sobre redes reais, verificando adjacência, confluências e ausência de ciclos.
- Logs: `.godot/v30-climate-review-final.log`, `.godot/v30-smoke-final.log`, `.godot/v30-regions-validation-final.log` e `.godot/v30-editor-validation-final.log`.
- Geração máxima 4096×2048 concluída com renderização em 110,59 s: 3.187.671 células terrestres, 19.883 trechos fluviais e zero términos desconhecidos sem pai. Captura `results/generator-v30/maximum-map.png` inspecionada; log `.godot/v30-maximum-final.log`. Tempo de uma execução local, não média de benchmark.

Comandos adicionais: `--climate-relief-review`, `--validate-regions-current` e `--validate-editor-current`. As opções de validação específicas da v29 continuam disponíveis para os casos legados.
