# Rios e bacias — v22

Revisão: 19/11/2026. Implementação local; sem publicação de release.

## Rede em árvore

Além do cálculo de drenagem da v21, a geração propaga a atração dos vales com mais fluxo para células próximas admissíveis. A influência perde força com a distância, de forma que um tronco forte próximo pode capturar drenagem de canais menores. São três passagens limitadas, com recálculo integral do fluxo após cada alteração.

A busca só considera ligações adjacentes com ordem topológica estritamente a jusante, superfície preenchida não mais alta e subida física limitada a 25 m na resolução local. Isso preserva a ausência de ciclos e impede ligar rios simplesmente por estarem próximos do outro lado de uma cordilheira. A atração é aplicada sobre o grafo de seis vizinhos; sua propagação faz a procura alcançar além do vizinho imediato.

Fluxos continuam somando todas as contribuições, incluindo cabeceiras não exibidas. As entidades `River` guardam caminhos, vazões e ligação para o curso pai. A lógica permanece independente da malha visual.

## Desenho

- Relaxamento dos pontos de grau dois para arredondar os cantos da grade.
- Meandros espaciais determinísticos; amplitude menor em montanhas e maior em cursos estreitos de planície.
- Duas curvas cúbicas Hermite por célula, passando por âncoras comuns nas confluências. Pontos e sockets compartilhados também nas emendas de chunks e na costura horizontal.
- Largura contínua derivada da raiz da vazão, com mínimo/máximo visuais; nascentes afinam até uma ponta.
- Azul mais claro para riachos e progressivamente mais escuro para cursos largos.
- Correnteza discreta, recorte na costa e supressão da água dos rios no interior de lagos preservados.

As curvas trabalham na escala do corredor da rede. Não há promessa de remover toda direção preferencial da grade em grandes planícies; a mudança atua tanto na convergência dos cursos quanto nos cantos e no traço visual.

## Desertos

A decisão usa chuva e temperatura, não o bioma final, pois o tratamento de fertilidade pode mudar um hex de deserto para campo.

- Regiões quentes com menos de 450 mm/ano recebem redução adicional do escoamento local.
- Não são selecionadas nascentes visíveis locais nessas condições.
- Um canal em zona árida precisa de quatro vezes o limiar de vazão normal para permanecer visível.
- Água trazida de regiões úmidas continua podendo sustentar rios que atravessam desertos. Não foram adicionadas nascentes de oásis/água subterrânea nesta etapa.

## Verificação

- Build sem avisos ou erros.
- Matriz ampliada para v18–v22: seeds 1234, 1 e −77; terra 30%, 40% e 50%; wrap ligado/desligado.
- Determinismo, quota de terra, caminhos sem ciclos, fluxo, save/load, locks, exterior da seleção e histórico de edição.
- Cenário controlado: deserto quente uniforme não produz rios perenes; faixa úmida a montante fornece um rio que atravessa a zona árida; cabeceiras visíveis não nascem na zona seca.
- Comparação de redes sobre o mesmo terreno e clima 1024×512: capturas `results/river-basins-v22/basin-v21.png` e `basin-v22.png`; dados em `metrics.json` e mundo v22 disponível no mesmo diretório.
- Um caso comparativo teve 327 fozes oceânicas na receita v21 e 312 na v22. Trechos com ligação de afluente representam a maior parte da rede; a comparação não equivale a contar rios independentes pelo número de entidades.

Logs: `.godot/v22-final-smoke.log`, `.godot/v22-basin-review.log` e `.godot/v22-maximum.log`.

A receita nova exige criar um mundo v22. O novo desenho também se aplica a rios salvos anteriormente; a topologia dos rios antigos permanece preservada ao abrir/salvar.


## Mapa máximo

4096×2048, seed 1234: a geração dos dados v22 concluiu em 98,4 s numa execução local. A comparação adicional recalculou v20 sobre o mesmo terreno, instalou os dois renderers e capturou imagens, terminando em 138,55 s. Esse segundo número inclui trabalho de comparação que não faz parte da criação normal.

A rede v22 teve 14.630 trechos e 256.353 hexes com arestas fluviais; o baseline v20, 86.386 trechos e 1.302.688 hexes. A v22 não busca reduzir a contagem em relação à v21 a qualquer custo: permite afluentes coerentes e usa maior largura nas calhas principais.

Comparações em `results/river-network-v22`, incluindo `dense-v20.png`, `dense-v22.png` e `metrics.json`. Planícies costeiras ainda podem apresentar canais aproximadamente paralelos, pois nem toda drenagem de uma costa deve convergir para uma única foz. A nova etapa evita conexões arbitrárias através de divisores de bacia.
