# M5: edição manual de redes

Data: 24/11/2026. Este relatório registra a implementação inicial e seus testes. O status atual é M5 concluído no escopo ajustado: veja o [relatório de fechamento](M5_Fechamento.md).

## Entrega inicial

A aba **Editar mapa → Redes** permite escolher estrada ou rio, tipo/largura e o modo **Apagar trechos**. Arrastar produz uma prévia e interpola os hexes entre os pontos do mouse. Soltar aplica uma operação de histórico. Voltar sobre o traço encurta o caminho, evitando ciclos na entidade fluvial.

- Estradas: trilha, terra e pedra. Pintar substitui o tier de cada aresta; apagar zera os dois lados.
- Rios: cinco larguras nas receitas atuais. Receitas antigas continuam armazenando suas três classes. Os caminhos são entidades persistentes e o cache de arestas é reconstruído após a operação.
- Apagar ou substituir um trecho de rio preserva os segmentos restantes e suas larguras/vazões. Fragmentos encerrados pelo corte ficam com término Custom; afluentes são religados ao segmento sobrevivente do pai quando o ponto de encontro continua presente.
- Ambos os hexes precisam estar dentro da seleção, se houver. Um lock de rede em qualquer lado congela a ligação. O painel de locks também oferece Estradas.
- O desenho manual permite subir terreno e terminar em terra; não recalcula geografia ou clima.
- Undo/redo, salvar/reabrir e cancelar rascunho incluem as redes.

## Renderer

Estradas usam braços geométricos por aresta, do centro ao ponto médio compartilhado, com largura/cor por tier e malhas agrupadas por chunk. É uma representação provisória do candidato modular A, sem novas imagens.

Os rios mantêm o renderer B já implementado: ribbons curvos agrupados por chunk, com correnteza e recorte costeiro. Esta entrega não substitui esse visual nem conclui a comparação dos candidatos A/B prevista no plano inicial. O cache visual de estradas evita reconstruções em edições comuns de terreno quando a rede não mudou.

## Testes do núcleo

`dotnet run --project tests/GeneratorChecks -- --network-only`: aprovado, código 0.

- Arestas espelhadas e edição entre dois chunks.
- Troca dos três tiers e exclusão de estrada.
- Lock no vizinho e borda da seleção para ambas as redes.
- Rio manual subindo terreno e preservação das cinco larguras.
- Corte no meio do caminho, preservação das arestas externas e das larguras restantes.
- Criação de afluente e retirada da referência ao pai apagado.
- Rejeição de ciclo antes de alterar os dados.
- Wrap horizontal, undo/redo e save/load.
- Alteração de uma rede procedural real v73, sem mudar arestas externas; undo restaura exatamente entidades e caches.

## Testes renderizados

`--network-review`: aprovado em 1280×800 e 1100×700, código 0. A segunda execução acrescentou o corte de um rio pelo mesmo fluxo do editor.

Foram exercitados os seletores, traço interpolado, prévia, três tiers, rio de nível 5, undo/redo, exclusão parcial de estrada e rio, salvar/reabrir e cancelar rascunho. Capturas com grade e sem grade foram inspecionadas; a aba Redes e seus botões de arquivo cabem nas duas janelas.

Evidências locais: `results/m5-networks/networks-grid.png`, `networks-natural.png`, `verification.json`; logs `.godot/m5-network-ui.log` e `.godot/m5-network-ui-1100.log`.

Build principal aprovada, zero erros. NU1900 informa falha na consulta de vulnerabilidades do NuGet. No ambiente restrito, Godot também registrou falhas de certificados, cache de shader e gravação da preferência de mapa recente; o arquivo `.world` temporário foi salvo, reaberto e comparado com sucesso.

## Revisão anterior ao fechamento

As observações abaixo foram registradas antes da decisão de encerramento. O [fechamento do M5](M5_Fechamento.md) registra o escopo aceito para a Alpha 2.2.

Escopo ajustado pelo usuário em 24/11/2026: o sistema de rios já está feito. Seu renderer de curvas será mantido. Estradas ficam para quando existirem pontos que precisem ser conectados, reaproveitando a infraestrutura de redes e curvas com uma lógica de caminhos própria. A ferramenta provisória criada nesta entrega não recebe novos desenvolvimentos agora. Não foi solicitada sua remoção.

A pendência específica é conferir o contrato de regeneração fluvial regional: locks, âncoras congeladas na borda, trechos externos preservados e fallback quando não há rota. A edição manual, histórico e save/load já passaram nos testes desta entrega. A matriz completa de regeneração não foi repetida.

A leitura do código identificou uma diferença concreta a resolver ou aceitar explicitamente antes do fechamento: quando a busca regional não alcança a saída, `RerouteRiver` conserva o trecho original. A seção 11 do plano descreve término Custom e sinalização de anomalia. Esta revisão apenas registra a diferença; não altera o comportamento dos rios.

Comparar um renderer modular novo ou ampliar a matriz de desempenho de redes manuais deixa de ser requisito desta etapa. A geração e o renderer de rios existentes, somados ao benchmark atual documentado no M4, são a base já entregue.

## Verificação da entrega

- PASS, funcionalidade: desenho, apagar, histórico, save/load e cancelar exercitados no núcleo e no fluxo renderizado.
- PASS, propósito: aba própria reúne redes sem apertar os pincéis de terreno; hint explica direção do rio, seleção e locks.
- PASS, identidade: madeira, fonte e controles existentes reutilizados; geometria de estrada identificada como provisória.
- PASS, conferência visual: capturas reais com e sem grade, painel em duas resoluções e build executada.

## Reprodução

```powershell
dotnet run --project tests/GeneratorChecks -- --network-only
dotnet build Mapmaker.csproj --no-restore
& '.\Godot_v4.7.2-stable_mono_win64_console.exe' --path . --log-file .godot/m5-network-ui.log -- --network-review
& '.\Godot_v4.7.2-stable_mono_win64_console.exe' --path . --resolution 1100x700 --log-file .godot/m5-network-ui-1100.log -- --network-review
```
