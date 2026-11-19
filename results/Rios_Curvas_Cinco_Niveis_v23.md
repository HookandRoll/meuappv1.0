# Rios v23 — curvas nas arestas, cinco níveis e união de cabeceiras

Data: 19/11/2026. Implementação local, sem publicação de release.

## Alterações

### Cinco níveis reais

Os trechos persistentes agora admitem cinco níveis: 1 nascente, 2 riacho, 3 rio pequeno, 4 rio, 5 grande rio (major river). A classe depende do fluxo acumulado, com limites relativos 1/2/5/15/50 vezes o limiar da geração. Largura e azul têm transição contínua dentro de cada classe; a ponta da nascente continua afinando.

As larguras visuais foram recalibradas: aproximadamente 0,065–0,09 / 0,09–0,14 / 0,14–0,21 / 0,21–0,31 / 0,31–0,50 unidades de mapa. A largura máxima anterior era 0,85. O inspetor apresenta os cinco nomes.

O array `RiverEdges` preserva o contrato de dois bits por aresta como cache de conectividade/porte agregado (classes 1–2 → cache 1; 3–4 → cache 2; 5 → cache 3). A classe exata de cinco níveis fica em `RiverRecord.Tiers` e é preservada no save; não foram alterados os bits de estradas ou o layout denso do `.world`. Um índice derivado por ID permite consultar a classe exata no inspetor. Saves anteriores mantêm a interpretação original de suas três classes.

### União explícita de cabeceiras

Depois da etapa de atração dos vales, as cabeceiras procuram a cabeceira mais próxima admissível na ordem a jusante, usando um índice espacial. O algoritmo examina o curso a jusante da vizinha e tenta uma busca A* limitada para chegar até ele.

A busca respeita a ordem topológica a jusante e a superfície auxiliar de drenagem não crescente, evita água no meio da ligação e não atravessa montanhas para unir cursos que apenas parecem próximos na imagem. Há tolerância local de até 6 m no relevo físico quantizado, com destino não mais alto que a origem. O alcance de procura é proporcional ao mapa, mínimo de seis hexes; cada busca tem limite de expansão. Fluxos são recalculados após as uniões.

### Curvas no caminho lógico

A alteração não fica só no shader. Trechos terrestres longos com a mesma direção são candidatos a um arco lateral amostrado e convertido novamente para hexes vizinhos. A sequência nova entra e sai pelas arestas da grade e o cache de arestas é reconstruído a partir dela.

Os candidatos evitam montanhas, limitam subidas locais a 2 m por passo e não excedem a altitude da origem em mais de 2 m, e não atravessam água. Se o arco encontra outro curso, pode formar uma confluência em vez de cruzá-lo. Uma verificação da cadeia a jusante rejeita qualquer retorno ao novo caminho, impedindo ciclos. A soma de fluxos é refeita depois de aplicar os arcos.

Trechos com obstáculos ou sem caminho admissível são preservados. Isso não promete abolir toda reta: em áreas íngremes, as condições de altitude continuam prevalecendo. As curvas geométricas e as âncoras compartilhadas em arestas/confluências permanecem no renderer, suavizando os cantos dos novos caminhos.

A proteção de desertos da v22 continua ativa: nascentes visíveis locais são suprimidas nas condições quentes e áridas; grandes fluxos importados podem atravessar a região.

## Resultados e testes

- Build sem erros ou avisos.
- Matriz de regressão ampliada para v18–v23, seeds 1234, 1 e −77; terra 30/40/50%; wrap ligado/desligado.
- Determinismo, quota de terra, grafos sem ciclos, fluxo, locks, exterior da seleção e histórico do editor.
- Save/load compara as entidades completas, incluindo os cinco níveis, além do cache de arestas.
- No caso de revisão 1024×512, seed 1234: 179 uniões explícitas de cabeceiras e 409 arcos aplicados a trechos retos. São contagens desse caso, não uma garantia fixa para toda seed.
- No mesmo caso, os cinco níveis foram exercitados e preservados no arquivo. Capturas e mundo de teste em `results/river-basins-v23`.

Logs: `.godot/v23-final-smoke.log`, `.godot/v23-basin-final.log` e `.godot/v23-maximum-final.log`.

## Como experimentar

Criar um mundo novo usa a geração v23. Abrir um mundo antigo atualiza o desenho e a escala visual, mas preserva seus caminhos e a versão registrada. Salvar não substitui silenciosamente a hidrologia anterior.

Arquivos centrais: `WorldGenerator.RiverConfluence.cs`, `WorldGenerator.Hydrology.cs`, `RiverOverlay.cs`, `River.gdshader`, `WorldData.cs` e o inspetor em `MapView.cs`.


## Teste do tamanho máximo

A execução final em 4096×2048 concluiu a geração dos dados em 98,5 s e chegou à imagem renderizada em 106,83 s. Houve 1.013 uniões explícitas entre cabeceiras e 14.570 arcos aceitos nos trechos retos. As contagens são do caso seed 1234, 38% de terra, três continentes; não representam limites fixos do algoritmo.

Captura: `results/river-levels-v23/maximum-map.png`. O aviso de escrita do cache GLES3 já observado no ambiente não impediu a compilação do shader ou a captura.
