# Jota Fantasy Map Maker

**Alpha 2.0 - Wind and Climate Simulation** · Mapas de fantasia para RPG, com editor de terreno, tectônica, clima e rios no Godot 4 .NET.

Mapas novos usam o **gerador v63**, com clima médio anual. No formulário de terreno completo, a temperatura pode variar de −20 a +20 °C e a precipitação de −50 a +50%, em passos de 1. O relevo começa a reter vapor acima de 1.800 m e bloqueia sua passagem pela crista a partir de 2.800 m. Parte da umidade bloqueada volta para o lado de onde veio e se espalha em direção a terrenos mais baixos e secos.

O alcance lateral do vapor é de 2 hexes para cada lado em mapas de até 512 × 256 e chega a 31 hexes no Atlas máximo. Sobre o oceano, a condensação é metade da calculada para o mesmo vapor na v58.

Oceanos e lagos também aumentam a chuva junto às margens, em todas as direções. Rios umedecem os solos vizinhos, afetando os biomas depois que a hidrologia foi calculada. O efeito diminui a cada hex de distância.

A precipitação anual em terra inclui o equivalente em água da neve. Faixas tropicais úmidas e temperadas também recebem mais precipitação de fundo. O gerador só forma desertos a partir de 5 °C de temperatura média anual.

A camada **Ventos** mostra a direção predominante do transporte de vapor por latitude. Clique em um hex para ver os pesos em cada direção. A partir do Atlas v58, o vapor que sai por uma borda lateral continua na borda oposta, na mesma latitude.

As camadas **Hadley: solo** e **Hadley: altitude** mostram o [protótipo de circulação em duas camadas](docs/Prototipo_Hadley_Ventos_2D.md), com troca de massa e movimento entre superfície e altitude. O cálculo ocorre em segundo plano, usando uma grade reduzida de até 512×256. Essas camadas ainda não alteram a chuva nem os biomas gerados.

O experimento climático acopla ventos, temperatura, vapor, nuvens e água do solo nas duas camadas. Inclui reposição por tipo de superfície e histórico térmico na subida e descida do relevo. Os comandos diagnósticos e seus limites estão na [documentação Hadley](docs/Prototipo_Hadley_Ventos_2D.md). O clima experimental ainda não substitui o gerador padrão nem tem opção de salvamento no formulário.

Executável portátil para Windows x64 na [release Alpha 2.0](https://github.com/HookandRoll/meuappv1.0/releases/tag/v2.0.0-alpha). Veja [as notas desta versão](RELEASE_NOTES_2.0.0.md). Os relatórios e imagens dos comparativos estão no arquivo de validação anexado à release.

As artes atuais têm três variações por elemento, vegetação isométrica e relevo sobreposto. Veja o [walkthrough visual](docs/Art_Walkthrough_1.1.30.md) e os [créditos das artes](THIRD_PARTY_NOTICES.md).

Os menus usam madeira desenhada e a fonte Faerie Moot. Na criação, terra e costa vão de 0 a 100%. Terra começa em 25% e avança de 5 em 5. Costa começa em 80% e avança de 10 em 10. A seed aparece no cabeçalho e é copiada ao clicar. [Detalhes e imagens](results/Menus_Sliders_v49.md).

A camada **Regiões** identifica e nomeia continentes, arquipélagos, ilhas, oceanos, mares e lagos. Use **Mapa → Renomear região selecionada…** para personalizar um nome. [Regras, limites e persistência](results/Regioes_Geograficas_v50.md).

A v52 preserva as ilhas naturais e acrescenta grupos pequenos de 3–9 hexes e ilhotas acompanhantes, com quantidade limitada e a mesma área terrestre. Corrige a fragmentação destrutiva da v51 e mantém o classificador de oceanos e mares da versão 2. [Correção, comparação e limites](results/Ilhas_Preservadas_v52.md).

A v53 aumenta a diversidade dos arquipélagos no atlas: mais ilhas pequenas e ilhotas, arcos vulcânicos mais finos e microplacas continentais com tamanhos variados, mantendo algumas ilhas principais grandes. [Comparações e causa dos canais continentais](results/Arquipelagos_v53.md).

A v54 dá preferência a placas continentais com centros mais afastados no atlas, abrindo mais espaço para bacias oceânicas. Quando as massas ainda se unem e faltam continentes, a geração pode abrir canais para separá-las.

## Abrir o protótipo

1. Instale o Godot **4.7.2 .NET** e um SDK .NET compatível com o alvo `net8.0`.
2. Abra `project.godot` no editor ou importe a pasta pelo gerenciador de projetos.
3. Pressione **F5**. O aplicativo abre sem mapa. Use **Mapa → Novo mapa** para criar um mundo ou **Mapa → Abrir mapa** para carregar um arquivo `.world`.

O VS Code pode abrir a pasta inteira. Também é possível compilar com `dotnet build Mapmaker.csproj`.

## Criar e salvar mapas

- Região usa os presets de **128 × 64** a **4096 × 2048** como retângulos. Mundo amplia o tamanho de referência e gera o atlas completo: a referência maior resulta em **6144 × 2784**, com **15.044.776 hexes úteis**. O formulário informa o tamanho real e avisa quando supera a referência de 4000 × 2000; mapas assim exigem mais memória e tempo.
- No formulário, escolha entre grade vazia, terra e água, relevo ou terreno completo com clima e biomas. Uma grade vazia pode ser preenchida depois em **Mapa → Gerar terreno**.
- Em **Tipo de mapa**, escolha **Mundo — atlas completo** (padrão), com topo/base retos e laterais curvas, ou **Região — recorte aberto**, retangular. No Mundo, placas, continentes, ilhas, relevo, clima e rios são calculados em toda a área útil; não há um retângulo central com faixas copiadas ou continuação visual de terreno. A porcentagem de terra e mar usa os hexes úteis fora das placas polares, excluindo também os cantos fora do contorno. O número de continentes orienta a quantidade mínima de massas principais separadas por água. Arquivos antigos mantêm suas versões e sua geografia.
- A barra de geração mostra a etapa em andamento e sua porcentagem. Os biomas usam clima e ruído em coordenadas globais, processados em chunks de 32 × 32 hexes. A camada **Terreno** suaviza as transições entre biomas terrestres. A faixa costeira usa a cor da terra vizinha e um contorno irregular sobre o oceano costeiro, que continua sendo inspecionado como água.
- Mapas novos usam **Escultura continental (estilo Azgaar)**, com gerador v51: a distribuição de crosta das placas define terra e mar; colisões formam cordilheiras e arcos de ilhas, e afastamentos rebaixam o terreno. As costas recebem detalhes sobre essa base geológica. O número de continentes orienta as placas continentais e a separação das massas principais por água, com tamanhos diferentes. O percentual de terra é ajustado globalmente, sem cotas iguais por continente. Bacias fechadas viram lagos; relevo, latitude e transporte de umidade influenciam o clima e os biomas. Os algoritmos anteriores continuam suportados nos arquivos existentes.
- No Mundo, as placas **Ártica** e **Antártica** são obrigatórias, adicionais às placas comuns e sempre oceânicas. Cobrem o topo e a base retos com fronteiras irregulares por seed e contatos de afastamento. No gerador v46, as faixas polares também recebem ilhas e continentes de gelo como exceção à reserva oceânica. As duas placas ficam fora do total de placas comuns e dos cálculos de escala baseados nesse total; toda a faixa polar, incluindo o gelo, fica fora da base da porcentagem de terra e mar. Geleiras automáticas ficam restritas aos polos. Veja a [validação das placas polares](results/Placas_Polares_v38.md) e o [ajuste de contagem e porcentagem](results/Cotas_Polares_v39.md).
- As duas laterais curvas do Mundo têm faixas de pequenas placas oceânicas, com limites irregulares e terreno sempre submerso para afastar as costas do contorno. Esse oceano lateral entra na porcentagem de água; apenas os polos ficam fora. Veja a [verificação das margens e da porcentagem](results/Margens_Oceanicas_v40.md).
- Use a roda do mouse para aproximar, arraste com o botão direito ou do meio para mover e clique para inspecionar um hex.
- O controle **Ícones** na barra superior alterna a camada de flora, fauna e detalhes. Os símbolos aparecem ao aproximar o terreno e são distribuídos pela seed conforme o bioma, o relevo e a água.
- **Salvar** (`Ctrl+S`), **Salvar como** (`Ctrl+Shift+S`) e **Abrir último mapa** ficam no menu **Mapa**. **Editar mapa** abre os pincéis de bioma, relevo, água e elevação, seleção regional, locks por campo, regeneração e undo/redo. É possível salvar as alterações ou cancelar o rascunho.
- Rios têm cinco níveis, curvas, afluentes, correnteza e foz recortada na costa. As camadas de **Tectônica** e **Chuva** ajudam a inspecionar a formação do terreno. Arquivos antigos preservam os dados e a versão de geração; abrir e salvar não regenera o mapa.

Em desenvolvimento, o [esquema de dados v2](docs/Map_Data_Format_v2.md) separa bioma, relevo, forma geográfica e características, com IDs de 16 bits e 64 features combináveis. Saves v1 continuam abrindo e são convertidos para v2 ao salvar, sem regenerar o mapa; arquivos v2 exigem uma build com suporte ao novo formato.

O renderer também abre um [corredor limpo ao longo dos rios](docs/River_Clearance.md), recortando a vegetação e os ícones junto ao leito, inclusive as partes das artes que vêm de hexes vizinhos. O efeito acompanha pan e zoom e não exige regenerar o mapa.

Ao aproximar o zoom, a vegetação se acomoda gradualmente no canto superior esquerdo de cada hex, em uma composição mais compacta que libera o centro e a direita para detalhes. A visão afastada mantém a cobertura ampla e irregular.

O [gerador v41 classifica formas geográficas por altitude e desnível](results/Relevo_Formas_Geograficas_v41.md): topos planos viram planaltos, quedas marcam serras/escarpas, e subidas identificam encostas e sopés. Pincéis de elevação/água atualizam a classificação dos vizinhos com locks e undo/redo; a regeneração oferece o campo **Forma geográfica**.

Os [símbolos de relevo acompanham os contornos das elevações](results/Contornos_De_Relevo.md): montanhas, morros e escarpas ficam nas quebras de declividade, com artes sobrepostas formando uma faixa mais contínua. As bordas têm prolongamentos para o lado alto e as regiões mais elevadas recebem grupos esparsos também no centro; escarpas usam sua arte própria. Essa distribuição visual também é recalculada ao abrir mapas existentes, sem mudar o terreno salvo.

O [gerador v42 remodela as margens de água](results/Margens_Agua_v42.md) com ruído espacial e altitude, varia a largura dos canais de separação e reduz o arredondamento visual das costas. A remodelagem preserva a área de cada massa terrestre e as conexões de terra e água; as cotas e as faixas oceânicas obrigatórias continuam respeitadas.

O [gerador v43 atualiza clima e altitude máxima](results/Clima_Altitude_v43.md): aplica a curva térmica e os cinturões de chuva da especificação de clima, reforça a sombra de chuva e calibra os cumes das cordilheiras ativas para máximos de 6.000–8.000 m por seed, com altitudes extremas raras. Arquivos anteriores mantêm sua receita de geração.

O [gerador v44 varia os corredores tectônicos e as bordas dos biomas](results/Bordas_Pantanos_v44.md), mantendo cotas, clima e locks. Pântanos arborizados passam a ocupar baixadas úmidas junto a rios, lagos rasos e enseadas abrigadas. O renderer usa ruído em várias escalas e um blend mais estreito para costas e biomas menos arredondados.

O [gerador v45 absorve faixas estreitas pela maioria dos biomas vizinhos](results/Maioria_Biomas_Agrupamentos_v45.md), com leitura regional e local, compatibilidade climática e proteção de habitats. As artes de montanhas comuns também formam grupos regionais, com menor densidade que os picos.

## Verificar a build

Após compilar, use o executável de console do Godot .NET. Substitua `godot` pelo caminho do executável se ele estiver fora do PATH.

```text
godot --headless --path . -- --smoke-test
godot --path . -- --benchmark
godot --path . -- --benchmark-large
godot --path . -- --verify-editor-bugfixes
godot --path . -- --benchmark-size=4096 --release-atlas
dotnet run --project tests/CompatibilityChecks
dotnet run --project tests/GeneratorChecks -- --memory-atlas-only
dotnet run --project tests/GeneratorChecks -- --regenerate-atlas-memory
```

Acrescente `--continental-sculpt` depois do comando de benchmark para medir a escultura continental e capturar as camadas Terreno e Elevação.

O teste verifica o estado inicial, a seleção de hexes, as arestas compartilhadas, o wrap, a geração e o ciclo de salvar e abrir do preset 1024 × 512. Também verifica o progresso por etapa, a continuidade de cordilheiras e estreitos na escultura e a regeneração de retângulos nas emendas dos chunks. Os benchmarks medem o carregamento inicial do renderer, pan/zoom, troca de camada e atualização integral das texturas em 1024 × 512 e 4096 × 2048. As imagens são gravadas em `.godot/`. As medições de latência vão até o sinal de conclusão do frame do Godot.

O núcleo em `src/Core` usa C# puro. `src/GodotApp` prepara as texturas e a interface, e o shader desenha terreno, relevo e vegetação com a arte de `tiles`, `relief` e `vegetation`. O `.world` é um ZIP versionado com a configuração de geração, os dados densos e as entidades esparsas. O arquivo é montado em um temporário antes de substituir o save anterior. A especificação na raiz lista os marcos seguintes.

Ao alterar os arrays `WorldData.Water` ou `WorldData.Elevation` diretamente, chame `InvalidateGeography()` antes de consultar regiões ou salvar. O editor e a regeneração já invalidam esse cache; o salvamento reutiliza a classificação enquanto ela estiver válida. Atlas a partir do gerador v37 exige `HorizontalWrap = false`; mapas regionais continuam aceitando wrap.

`CompatibilityChecks` compara hashes de 48 mundos à implementação anterior às otimizações, incluindo todas as camadas, rios, regiões, ícones e texturas. O teste `--memory-atlas-only` exercita snapshots e undo/redo no tamanho máximo; `--regenerate-atlas-memory` executa duas regenerações completas do Atlas máximo e informa o pico de memória do processo.

Para exportar o EXE portátil, instale os templates Windows .NET correspondentes ao Godot 4.7.2 e execute `godot --headless --path . --editor --export-release "Windows Desktop"`. O preset gera `dist/Jota-Fantasy-Map-Maker-Alpha-2.0-Wind-and-Climate-Simulation.exe`, com PCK e runtime incorporados.

## Agradecimentos

Agradecemos a Azgaar e a toda a equipe de colaboradores do **Fantasy Map Generator**. Suas receitas de mapas de elevação inspiraram a escultura continental do Jota Fantasy Map Maker. [Azgaar’s Fantasy Map Generator](https://github.com/Azgaar/Fantasy-Map-Generator)

Os testes específicos do gerador podem ser executados com `dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj` (SDK .NET 10). Eles verificam a consolidação de biomas, limites climáticos, wrap, determinismo, regeneração regional e locks.
