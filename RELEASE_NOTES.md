# Jota Fantasy Map Maker — Alpha 1.1.30

Executável portátil para **Windows x64**: Jota-Fantasy-Map-Maker-Alpha-1.1.30.exe. Inclui runtime .NET e os recursos do aplicativo; não exige Godot ou SDK instalados para usar o aplicativo.

Mudanças desde a **Alpha 1.1.29**. Mapas novos usam o gerador **v30**. [Notas anteriores](https://github.com/HookandRoll/meuappv1.0/releases/tag/v1.1.29-alpha).

## Artes e apresentação

- Reformulação visual das texturas de terreno, água, relevo, vegetação e ícones, revisada a partir do walkthrough do Argon.
- Três variações por arte: 42 texturas de terreno/água, 15 de relevo, 18 de vegetação e 84 de ícones — 159 recursos usados pelo renderizador.
- Vegetação e ícones vegetais baseados no **Yughues Isometric Plants**, com sua declaração de CC0 e créditos preservados no repositório.
- Distribuição mais densa e irregular de florestas e relevo, com escala, posição e variações determinísticas por hex.
- Sobreposição entre hexes em ordem de pintura, reduzindo cortes nas árvores e nos maciços de montanhas.
- Relevo com rocha, neve e contornos preservados, mesclando as encostas com a cor do bioma.
- Texturas do solo e da água amostradas em duas escalas em coordenadas do mundo para reduzir a repetição aparente.
- Paleta de interface mais quente e ajustes de cor dos rios.
- [Walkthrough de conferência com capturas](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.30-alpha/docs/Art_Walkthrough_1.1.30.md) e [créditos](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.30-alpha/THIRD_PARTY_NOTICES.md).

## Clima, biomas e elevação — gerador v30

- Chuva regional baseada em latitude, umidade transportada e variação espacial ampla, retirando a penalização direta por altitude da chuva base.
- Relevo mantém influência por condensação e sombra de chuva, com intensidade limitada e perfil atmosférico suavizado.
- Mistura gradual dos sentidos de vento nas faixas de latitude e transporte periódico de umidade com wrap.
- Menos perturbações artificiais nos limiares dos biomas; planície física deixa de impedir floresta por si só. Temperatura continua respondendo à altitude.
- Suavização física de todo o campo final de elevação, incluindo costas, margens de lagos e fundo aquático. A distribuição de terra/água e a quota permanecem preservadas.
- No comparativo 1024×512, seed 1234, o maior desnível entre vizinhos terrestres caiu de 859 para 510 m; a cota máxima permaneceu próxima de 4.100 m. São resultados desse caso, não garantias para toda seed.
- O degelo continua sendo usado no escoamento dos rios; ele não escreve no mapa de chuva.

## Verificações e ferramentas de desenvolvimento

- Validação M0–M3 ampliada para o gerador atual, incluindo comparações regionais e fluxo integrado do editor.
- Benchmark reproduzível para os seis tamanhos da interface, com geração, renderização, edição, memória, camadas e round-trip de save/load.
- Checagem explícita de todas as 159 texturas exigidas pelo pacote: presença das variantes, imagens não vazias e transparência das sobreposições.
- Capturas de conferência em visão geral e aproximada; benchmark gráfico 1024×512 aprovado com as novas artes nesta máquina.
- EXE portátil isolado aprovado no smoke completo, incluindo as 159 texturas, salvar/reabrir e edição. Benchmark gráfico do próprio EXE sem erros de recursos ou shaders: aproximadamente 1.272 FPS em pan/zoom e p95 de edição de 2,3 ms, numa execução local com RTX 5070 e VSync desligado.
- Gerador v30 já validado com smoke, 384 comparações regionais, 20 operações do editor e geração renderizada em 4096×2048.
- Protótipo independente Forest Lab incluído no código-fonte para estudos de floresta; ele não faz parte do EXE principal.

## Compatibilidade

Saves existentes preservam seus dados e sua versão de geração. As novas artes aparecem ao abrir mapas antigos; criar um mapa novo aplica também a receita de clima e relevo v30. Abrir e salvar não regenera silenciosamente a geografia.

Esta entrega publica as artes e as correções já implementadas. A próxima revisão específica de geografia fica para uma etapa posterior.
