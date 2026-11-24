# Fechamento do M4

Data: 24/11/2026. Status: concluído para o escopo do protótipo no tamanho de referência 1024×512.

## Escopo entregue

- Terreno contínuo, transições entre biomas e costas suavizadas no shader.
- Alternância de grade para leitura natural ou estratégica.
- Relevo e vegetação com artes integradas ao renderer, sem um Node por hex. As artes são amostradas pelo shader, como alternativa ao lote MultiMesh sugerido no plano.
- Vegetação compactada no canto do hex ao aproximar e máscara estável que oculta aproximadamente 50% dos grupos de árvores no zoom máximo, com transição gradual.

## Evidências visuais

O [walkthrough da Alpha 1.1.30](../docs/Art_Walkthrough_1.1.30.md) registra a conferência de montanhas, vegetação, costa, variantes e integração das artes. As revisões seguintes estão documentadas em [continuidade das costas](Continuidade_Visual_Costas_v35.md), [contornos de relevo](Contornos_De_Relevo.md) e [árvores no zoom máximo](Vegetacao_Zoom_M4.md).

A última alteração foi conferida no aplicativo com capturas do mesmo enquadramento antes/depois da máscara em zooms 22, 36 e 44. A grade é controlada por uniform, preservando o mesmo terreno e decoração.

## Gate de desempenho

Benchmark atual: gerador v73, seed 1234, Região 1024×512, Godot 4.7.2 .NET, build Debug, RTX 5070, OpenGL Compatibility, janela 1280×800 e VSync desligado.

| Critério | Resultado | Situação |
|---|---:|---|
| Navegação na visão geral | 512,1 FPS médios | PASS, limite 30 FPS |
| Navegação no zoom máximo | 1.148,1 FPS médios | PASS |
| Troca de camada mais lenta | 50,12 ms | PASS, limite 100 ms |
| Edição, p95 | 1,96 ms/frame | PASS, referência 33,33 ms |

O gate exige desempenho próximo ao M0. A versão atual permanece acima dos limites obrigatórios, com terreno, decoração e rios ativos. Os números históricos foram obtidos com receitas e camadas diferentes e não são uma comparação controlada de regressão. O alvo ideal de troca de camada abaixo de 16 ms permanece uma melhoria opcional.

O relatório [Vegetacao_Zoom_M4.md](Vegetacao_Zoom_M4.md) inclui geração, memória, save/load, limitações, avisos do ambiente e comando de reprodução. A matriz antiga de todos os tamanhos permanece em [Desempenho_Todos_Tamanhos_v29.md](Desempenho_Todos_Tamanhos_v29.md); esta rodada não repete nem certifica novamente o Atlas máximo.

## Encerramento

Não há funcionalidade obrigatória do M4 pendente identificada. A conferência visual anterior, o ajuste solicitado de legibilidade e o benchmark atual sustentam o fechamento. Novos ajustes artísticos podem ser tratados como melhorias, sem bloquear as redes do M5.

O fechamento não certifica os gates de edição de redes do M5 nem a Definition of Done completa do protótipo.
