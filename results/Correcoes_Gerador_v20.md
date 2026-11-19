# Correções de costa, rios e geração máxima — v20

- Foz: a malha fluvial usa o mesmo cálculo de contorno do shader do terreno para cessar sua opacidade na água. A geometria continua até o contorno suavizado, eliminando pontas retangulares soltas no mar.
- Lagos: nós inteiramente submersos não geram malha. Só são construídos meios-trechos na entrada/saída junto à terra; o shader recorta a parte aquática. Corrige também nascentes/confluências artificiais visíveis no interior do lago, sem apagar a lógica da drenagem.
- Faixas continentais muito estreitas: três passagens removem células com até dois vizinhos terrestres e redistribuem a área em margens com apoio de pelo menos três vizinhos. Mantém a quota de terra e a separação entre territórios. Em casos sem espaço, preserva as células necessárias para não perder área. Ilhas são adicionadas depois desse tratamento.
- Aviso na criação para 128×64 e 256×128, indicando menor espaço para detalhes e recomendando 512×256 ou maior.
- Margens lacustres: em novos mundos, lagos fornecem umidade à amostragem de vento. Margens baixas diretamente adjacentes a lago recebem o tratamento de fertilidade já usado em rios; deserto/estepe quente pode virar campo, respeitando locks. Isso não transforma todo entorno de lago em floresta nem elimina desertos regionais.
- Gargalo de 85%: a avaliação de arcos tectônicos passa a usar no máximo 128×64 amostras, interpoladas, e o conjunto de candidatos de ilhas passa a ter espaçamento proporcional ao mapa. Antes, cada uma das milhões de posições era comparada a todos os segmentos tectônicos.
- Progresso: atualizações durante a fase de ilhas e etapa explícita “Rios e lagos”.
- Versão do gerador incrementada para 20; receita de mundos antigos preservada. Correções visuais de rios também se aplicam a mundos antigos ao abrir.

## Verificação

Build sem erros/avisos. Smoke completo aprovado para a matriz v18/v19/v20: três seeds, três percentuais de terra, wrap ligado/desligado. Testes de repetibilidade, quota, save/load, locks, exterior da seleção e histórico do M3 continuam passando.

Captura sintética `results/river-fix-v20/mouth-and-lake.png` conferida: foz de oceano e de lago alinhadas à costa, sem prolongamento fluvial sobre a água. A cena inclui um rio inteiramente submerso que deixa de ser desenhado.

Comparação em 256×128, seed 1234: células terrestres com até dois vizinhos terrestres caíram de 73 (v19) para 40 (v20), incluindo pequenas ilhas e pontas naturais restantes. Imagens em `results/river-fix-v20/small-v19.png` e `small-v20.png`. O mapa do usuário não pôde ser reproduzido exatamente sem sua seed/arquivo; a correção trata a causa geral das faixas estreitas, sem afirmar reprodução daquela seed.

Criação de dados 1024×512 medida em 5,385 s numa execução local, contra cerca de 29 s na medição anterior de v19. Essas medidas não são médias de benchmark.

Geração completa dos dados 4096×2048: concluída em 97,28 s, 3.187.671 células terrestres (quota de 38%) e 86.386 trechos fluviais. Log `.godot/v20-max.log`. Esse tempo não inclui upload/renderer. A execução renderizada também concluiu, com o mapa aparecendo em 108,30 s no total, registrada em `.godot/v20-max-rendered.log`. Captura: `results/river-fix-v20/maximum-map.png`.

Geração e morfologia novas exigem criar um mundo v20. O desenho corrigido das fozes/lagos vale também ao abrir os arquivos anteriores. Pintura e salvamento não regeneram a geografia automaticamente.
