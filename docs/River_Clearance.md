# Corredor limpo nos rios

Vegetação e ícones são recortados por uma máscara que acompanha o desenho real dos rios. As partes das artes dentro do corredor ficam transparentes, revelando o solo por baixo; as partes fora dele permanecem. Isso inclui artes de hexes vizinhos que invadem o leito. Relevo e textura de solo não são recortados.

A máscara reutiliza os mesmos meshes dos rios, com os mesmos pontos, larguras variáveis, curvas Hermite e confluências. O shader expande os vértices para acrescentar uma margem em cada lado de `0.08 + largura * 0.25` unidades do mapa. Os buffers de geometria são compartilhados: não há uma segunda cópia das curvas/triângulos para a máscara.

Uma `SubViewport` transparente, do tamanho da área renderizada da janela, desenha o corredor com bordas suavizadas. Ela tem um mundo 2D separado e acompanha a transformação da câmera. A textura é atualizada ao mudar a geometria, pan, zoom, tamanho da janela ou visibilidade; a cena parada não exige redesenhar a máscara a cada frame. Não é criada uma textura proporcional à resolução total do mundo.

O shader do terreno consulta a máscara uma vez por fragmento e passa a visibilidade resultante à composição dos 11 hexes vizinhos. A opacidade da vegetação e dos ícones é multiplicada por essa visibilidade. O shader de máscara compartilha o mesmo recorte costeiro do desenho da água.

O efeito é visual, aplica-se a mapas existentes sem regeneração e não altera biomas, posições dos ícones, dados dos rios ou a porcentagem de terra. Ao trocar para um mapa sem rios, a máscara é desativada para não deixar recortes antigos.

## Verificação

- Build sem erros ou avisos.
- Revisão no renderer com um rio passando por cinco níveis de largura, curvas, confluência e floresta densa.
- Comparação de pixels entre artes sem recorte, artes recortadas e solo sem artes: 1.481 pixels de vegetação e 380 pixels de ícones testados dentro do corredor; nenhum pixel alterado fora da margem nos dois casos.
- Conferência do alinhamento por amostras do mesh após pan, zoom e redimensionamento de 1280 × 800 para 1440 × 900.
- Troca para mapa sem rios e conferência de que a hidrologia permaneceu intacta.
- Revisão do atlas completo e smoke test da aplicação.

Reproduzir com Godot: `--path . -- --river-clearance-review`. As capturas de antes/depois ficam em `.godot/river-clearance-vegetation-before.png`, `river-clearance-vegetation-after.png`, `river-clearance-icons-before.png` e `river-clearance-icons-after.png`.
