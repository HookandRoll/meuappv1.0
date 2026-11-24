# Fechamento do M5: geografia do mundo

Data: 24/11/2026. Versão: Alpha 2.2 - World Geography. Status: concluído no escopo ajustado pelo usuário.

O gerador geográfico é considerado completo. A entrega reúne tectônica, continentes, costas, ilhas, relevo, clima acoplado, biomas e rios, com inspeção, edição, locks, regeneração e persistência. M6 (campanha) e M7 (entrega completa do protótipo) permanecem etapas distintas.

## Escopo entregue

- Hidrologia e entidades fluviais com afluentes, larguras e cache de arestas sincronizado.
- Renderer de rios em curvas preservado, com animação e recorte junto à costa.
- Ferramentas manuais de desenhar/apagar redes, seleção, locks, undo/redo, salvar/reabrir e cancelar rascunho.
- A ferramenta provisória de estradas já implementada acompanha o código; gerar estradas para conectar pontos fica adiado até existirem esses pontos. Não bloqueia o fechamento.
- Vegetação compactada ao aproximar e aproximadamente metade dos grupos de árvores ocultos no zoom máximo, com seleção estável.
- Montanhas e picos maiores à distância e menores ao aproximar, usando metade das dimensões do vulcão.
- Artes separadas dos dados geográficos, mantendo a possibilidade de substituição de imagens.

## Verificação

Os [testes de redes](M5_Redes_Manuais.md) cobrem arestas compartilhadas, chunks, tiers/larguras, locks, seleção, cortes, afluentes, wrap, undo/redo e save/load, incluindo alteração de um rio gerado pela receita v73. O fluxo renderizado foi exercitado em 1280×800 e 1100×700.

O [fechamento do M4](M4_Fechamento.md), a [máscara de árvores](Vegetacao_Zoom_M4.md) e o [teste de escala das montanhas](Montanhas_Zoom_Metade_Vulcao.md) registram as evidências visuais e de desempenho de cada ajuste. A validação da build exportada é registrada separadamente em `Validacao_Alpha_2.2.md`.

## Diferença documentada do plano original

Quando a busca de rota na regeneração regional falha, o comportamento atual conserva o trecho de rio original. O plano original previa término Custom e sinalização de anomalia. Essa diferença permanece registrada, sem alteração de comportamento nesta publicação, dentro do fechamento solicitado pelo usuário para o gerador geográfico existente. Não representa aprovação de uma nova validação da matriz completa de regeneração.

Não é necessário substituir o renderer fluvial existente por outro candidato para encerrar esta etapa. As mudanças futuras de arte e a conexão automática de pontos por estradas ficam fora do M5 encerrado.
