# Jota Fantasy Map Maker — Alpha 1.1.31

Executável portátil para **Windows x64**: `Jota-Fantasy-Map-Maker-Alpha-1.1.31.exe`. Inclui runtime .NET e os recursos do aplicativo; não exige Godot ou SDK instalados para usar o aplicativo.

Mudanças desde a **Alpha 1.1.30**. Mapas novos usam o gerador **v40**, reunindo as revisões v31–v40. [Release anterior](https://github.com/HookandRoll/meuappv1.0/releases/tag/v1.1.30-alpha).

## Tectônica e formação do terreno

- Fronteiras de placas com deformação por ruído em várias escalas: curvas amplas, quinas e trechos menores recortados, determinados pela seed.
- Placas grandes continentais e oceânicas com tamanhos diferentes; microplacas concentradas nos encontros entre placas maiores, influenciando o relevo local.
- Hierarquia, tamanhos e posições das placas com maior variação entre seeds, evitando mundos com a mesma distribuição global.
- A crosta das placas define a base de terra e mar. O terreno e a camada Tectônica compartilham o campo de fronteiras.
- Colisões continentais formam cordilheiras; subducções oceânicas podem formar arcos de ilhas e fossas. Afastamentos rebaixam o terreno e suprimem cristas nas faixas ativas.
- Correção da orientação das forças em fronteiras dobradas e microplacas. A intensidade considera o contato local, com envelopes que evitam inflar o relevo pela quantidade de segmentos.
- Remoção, nesse caminho de geração, de territórios continentais rígidos, cotas iguais por continente e orçamento separado de ilhas aleatórias.
- Plataformas, baías, penínsulas e costas detalhadas sobre a base geológica; preservação do relevo tectônico costeiro durante a suavização.

## Biomas e continentes

- Consolidação de pequenas manchas de bioma com regiões vizinhas maiores quando há compatibilidade climática.
- Preservação de praias, áreas alpinas, água e regiões sem vizinho climático adequado; habitats fluviais continuam aplicados depois da consolidação.
- Consolidação compatível com wrap, regeneração regional e locks.
- Separação de massas continentais unidas por canais que seguem o terreno, com reposição da quantidade de terra sem reconectar as massas.
- O controle de continentes orienta as placas continentais e a quantidade mínima de massas principais separadas, mantendo tamanhos diferentes.

## Mundo e Região

- Novo seletor **Mundo — atlas completo** e **Região — recorte aberto**.
- Região conserva o mapa retangular nas dimensões escolhidas.
- Mundo gera toda a área de um atlas com topo/base retos e laterais curvas. Placas, continentes, ilhas, relevo, clima e rios são calculados no domínio inteiro, sem copiar faixas do retângulo central.
- Todos os hexes úteis do atlas são reais, selecionáveis, editáveis, regeneráveis e salvos. Os cantos externos ao contorno ficam fora do domínio e da porcentagem de terra.
- Mundo amplia o tamanho de referência em 1,5 vez na largura e 1,35 na altura, arredondando para chunks. O maior preset de referência, 4096 × 2048, resulta em **6144 × 2784**, com **15.044.776 hexes úteis**.
- O formulário mostra o tamanho real e avisa sobre o maior uso de memória e tempo quando a área supera a referência de 4000 × 2000.
- Clipping, seleção, pincéis, regeneração e roteamento dos rios respeitam as bordas curvas. O formulário preserva o tamanho de referência ao restaurar configurações, evitando ampliar o mapa duas vezes.

## Polos e margens oceânicas

- Duas placas extras obrigatórias no Mundo: **Ártica** e **Antártica**, sempre oceânicas, cobrindo a parte reta do topo e da base.
- Fronteiras polares irregulares por seed e contatos permanentemente divergentes. Suas faixas permanecem submersas durante a geração e os ajustes continentais.
- Entre seis e doze pequenos fragmentos oceânicos em cada lateral curva, conforme o tamanho do atlas, com centros, velocidades e contatos reais.
- Faixas laterais com contorno interno por ruído, mantendo continentes e ilhas afastados da borda externa do atlas.
- Placas dos polos e das margens não consomem o total de placas comuns nem entram nos cálculos de escala baseados nesse total. Os logs também informam a contagem de placas comuns.
- Massas de gelo e sua textura ainda não foram implementadas; as placas polares ficam identificadas para essa etapa futura.

## Porcentagem de terra e mar

- A tectônica determina a distribuição do terreno, mas não substitui o percentual escolhido. O nível do mar e a correção final preservam a quantidade solicitada em hexes inteiros, inclusive depois da separação continental.
- A porcentagem usa todos os hexes úteis **fora das placas polares**. O oceano lateral entra na porcentagem de água; o oceano polar fica adicional.
- Conferência no aplicativo: alvo de 38% em um atlas de 768 × 352 resultou em **78.985 hexes de terra em 207.856 hexes não polares — 37,9999%**, diferença menor que um hex pelo arredondamento.
- A dica do controle de porcentagem explica a exclusão dos polos.

## Compatibilidade e verificações

- Saves anteriores mantêm seus dados, versão do gerador e receita de regeneração. Abrir e salvar não regenera silenciosamente a geografia.
- Suporte preservado às versões intermediárias de moldura visual, costa externa e faixas polares dos mapas já salvos.
- Novos campos de tamanho e papel das placas preservados na serialização; referências antigas recebem os valores padrão compatíveis.
- Suíte independente do núcleo incluída em `tests/GeneratorChecks`: biomas, fronteiras, forças tectônicas, variação de seeds, cotas de terra, separação continental, domínio do atlas, polos, margens, wrap, determinismo, regeneração, locks e salvar/abrir.
- Matriz de 36 casos do atlas e matriz polar nas versões 38, 39 e 40, com três seeds, com/sem wrap e alvos de 10/38/65% de terra; conferência independente da área não polar e ausência de terra nas margens reservadas.
- Smoke test da aplicação e revisão no renderer real aprovados durante o desenvolvimento; relatórios e capturas das revisões em `results/` e `docs/images/`.
- EXE portátil desta release testado em pasta isolada: smoke de 524.288 hexes com edição e salvar/abrir, além da revisão gráfica de Região/Mundo, porcentagem de terra e formulário de capacidade, sem depender dos arquivos do projeto.

[Conferência das margens e porcentagens](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.31-alpha/results/Margens_Oceanicas_v40.md) · [Atlas integral](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.31-alpha/results/Atlas_Integral_v37.md) · [Tectônica como base da geração](https://github.com/HookandRoll/meuappv1.0/blob/v1.1.31-alpha/results/Tectonica_Geracao_v33.md).
