# Rios com destino e cordilheiras estreitas — v24

Data: 19/11/2026. Alterações locais no workspace; sem publicação de release.

## Rios

O corte principal ocorria na seleção da rede visível. O curso podia atingir o limiar numa área úmida e deixar de satisfazê-lo ao entrar numa área árida, sendo desenhado apenas até a transição. Isso produzia um término `Custom` no meio da terra, mesmo existindo um caminho completo no grafo de drenagem.

Na v24, um curso escolhido para exibição mantém sua continuidade até água, outro trecho visível ou uma borda física de saída. O limiar deixa de interromper a linha depois de ela ter começado. Cursos fracos que não se sustentam numa zona árida são omitidos por inteiro; seu escoamento continua contabilizado nos outros rios. Não foi criada água adicional para prolongá-los.

A rede visível que entra num lago termina na água. Um sangradouro com fluxo suficiente pode iniciar um curso de saída separado; células submersas arbitrárias deixam de ser selecionadas como cabeceiras. Terminais na borda física recebem `Boundary`, em vez de parecerem um término desconhecido no interior.

As pontas de nascente são legítimas e continuam em terra: a exigência de conexão aplica-se ao destino a jusante. Confluências mantêm um ponto lógico compartilhado entre o afluente e o curso pai. Términos `Custom` continuam admitidos no editor para situações manuais e para preservar trechos protegidos durante regeneração regional.

## Picos e bioma alpino

A influência ampla da colisão vinha sendo usada também como núcleo de picos. Sua largura era proporcional ao continente inteiro e vários segmentos sobrepostos elevavam uma área extensa até formar um platô alpino.

A v24 separa os dois efeitos:

- A influência ampla continua produzindo relevo e terras altas.
- A crista que sustenta os picos usa um corredor muito menor, proporcional à escala média das placas, seguindo os contatos convergentes.
- Em subducção, o deslocamento para o lado superior da placa também acompanha a escala das placas, aproximando a cordilheira da fronteira correta.
- A elevação gerada fora do núcleo estreito fica limitada a terras altas abaixo do limiar alpino. A elevação cresce progressivamente no núcleo; o bioma branco resulta da altitude/clima efetivos, não de uma troca cosmética de cor.
- A compressão das encostas é suave e monotônica, preservando os gradientes de altitude em vez de criar patamares planos com um corte rígido.
- A amostragem de relevo usada no clima utiliza a mesma fórmula, para que chuva e temperatura acompanhem a cordilheira afinada.

As alterações só afetam campos regeneráveis. Elevação preservada pelo modo de regeneração ou por locks continua sendo respeitada, assim como a pintura manual.

## Verificação

- Build sem erros ou avisos.
- Matriz de regressão v18–v24, três seeds, três percentuais de terra, wrap ligado/desligado.
- Repetibilidade, quota de terra, grafo sem ciclos, fluxo, entidades/níveis no save, locks, exterior da seleção e histórico do M3.
- Na v24, todos os cursos gerados sem rio pai devem terminar em oceano, lago, pântano ou borda física; confluências precisam compartilhar o ponto final/inicial.
- Comparação 1024×512, seed 1234, 38% de terra, três continentes:

| Métrica | v23 | v24 |
|---|---:|---:|
| Células de bioma alpino | 7.694 | 2.572 |
| Células de relevo Peak | 8.479 | 2.739 |
| Términos desconhecidos sem rio pai | 7 | 0 |

As imagens de terreno e tectônica foram conferidas. Capturas, mundos e métricas estão em `results/ridges-rivers-v24`. Esses números são do caso comparativo, não garantias de contagem para qualquer seed.

Logs: `.godot/v24-ridge-review-final.log`, `.godot/v24-final-smoke.log` e `.godot/v24-maximum-final.log`.

Para experimentar as duas correções, criar um mundo v24. Mundos antigos preservam seu terreno e sua rede salvos; abrir ou salvar não substitui silenciosamente as edições.


## Tamanho máximo

A execução 4096×2048 (seed 1234, 38% terra) concluiu os dados em 103,1 s e apresentou a imagem em 111,23 s, incluindo a preparação do renderer. Foram verificados 17.338 trechos fluviais com zero términos desconhecidos sem pai. O mapa teve 44.648 células alpinas sobre 3.187.671 células terrestres, aproximadamente 1,40% da terra.

Captura: `results/generator-v24/maximum-map.png`. Os tempos são de uma execução local, não médias de benchmark. O aviso preexistente de certificados e a falha de escrita do cache de shaders GLES3 não impediram a execução nem as capturas.
