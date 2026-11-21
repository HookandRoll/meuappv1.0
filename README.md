# Jota Fantasy Map Maker

**Alpha 1.1.31** · Mapas de fantasia para RPG, com editor de terreno, tectônica, clima e rios no Godot 4 .NET.

Executável portátil para Windows x64 e histórico de mudanças na [release Alpha 1.1.31](https://github.com/HookandRoll/meuappv1.0/releases/tag/v1.1.31-alpha). Veja [todas as mudanças desde a Alpha 1.1.30](RELEASE_NOTES.md).

As artes atuais têm três variações por elemento, vegetação isométrica e relevo sobreposto. Veja o [walkthrough visual](docs/Art_Walkthrough_1.1.30.md) e os [créditos das artes](THIRD_PARTY_NOTICES.md).

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
- Mapas novos usam **Escultura continental (estilo Azgaar)**, com gerador v40: a distribuição de crosta das placas define terra e mar; colisões formam cordilheiras e arcos de ilhas, e afastamentos rebaixam o terreno. As costas recebem detalhes sobre essa base geológica. O número de continentes orienta as placas continentais e a separação das massas principais por água, com tamanhos diferentes. O percentual de terra é ajustado globalmente, sem cotas iguais por continente. Bacias fechadas viram lagos; relevo, latitude e transporte de umidade influenciam o clima e os biomas. Os algoritmos anteriores continuam suportados nos arquivos existentes.
- No Mundo v40, as placas **Ártica** e **Antártica** são obrigatórias, adicionais às placas comuns e sempre oceânicas. Cobrem o topo e a base retos com fronteiras irregulares por seed e contatos de afastamento. A geração mantém essas faixas submersas. As duas placas ficam fora do total de placas comuns e dos cálculos de escala baseados nesse total; seu oceano fica fora da base da porcentagem de terra e mar. Massas de gelo ficam para uma etapa futura. Veja a [validação das placas polares](results/Placas_Polares_v38.md) e o [ajuste de contagem e porcentagem](results/Cotas_Polares_v39.md).
- As duas laterais curvas do Mundo têm faixas de pequenas placas oceânicas, com limites irregulares e terreno sempre submerso para afastar as costas do contorno. Esse oceano lateral entra na porcentagem de água; apenas os polos ficam fora. Veja a [verificação das margens e da porcentagem](results/Margens_Oceanicas_v40.md).
- Use a roda do mouse para aproximar, arraste com o botão direito ou do meio para mover e clique para inspecionar um hex.
- O controle **Ícones** na barra superior alterna a camada de flora, fauna e detalhes. Os símbolos aparecem ao aproximar o terreno e são distribuídos pela seed conforme o bioma, o relevo e a água.
- **Salvar** (`Ctrl+S`), **Salvar como** (`Ctrl+Shift+S`) e **Abrir último mapa** ficam no menu **Mapa**. **Editar mapa** abre os pincéis de bioma, relevo, água e elevação, seleção regional, locks por campo, regeneração e undo/redo. É possível salvar as alterações ou cancelar o rascunho.
- Rios têm cinco níveis, curvas, afluentes, correnteza e foz recortada na costa. As camadas de **Tectônica** e **Chuva** ajudam a inspecionar a formação do terreno. Arquivos antigos preservam os dados e a versão de geração; abrir e salvar não regenera o mapa.

## Verificar a build

Após compilar, use o executável de console do Godot .NET. Substitua `godot` pelo caminho do executável se ele estiver fora do PATH.

```text
godot --headless --path . -- --smoke-test
godot --path . -- --benchmark
godot --path . -- --benchmark-large
```

Acrescente `--continental-sculpt` depois do comando de benchmark para medir a escultura continental e capturar as camadas Terreno e Elevação.

O teste verifica o estado inicial, a seleção de hexes, as arestas compartilhadas, o wrap, a geração e o ciclo de salvar e abrir do preset 1024 × 512. Também verifica o progresso por etapa, a continuidade de cordilheiras e estreitos na escultura e a regeneração de retângulos nas emendas dos chunks. Os benchmarks medem o carregamento inicial do renderer, pan/zoom, troca de camada e atualização integral das texturas em 1024 × 512 e 4096 × 2048. As imagens são gravadas em `.godot/`. As medições de latência vão até o sinal de conclusão do frame do Godot.

O núcleo em `src/Core` usa C# puro. `src/GodotApp` prepara as texturas e a interface, e o shader desenha terreno, relevo e vegetação com a arte de `tiles`, `relief` e `vegetation`. O `.world` é um ZIP versionado com a configuração de geração, os dados densos e as entidades esparsas. O arquivo é montado em um temporário antes de substituir o save anterior. A especificação na raiz lista os marcos seguintes.

Para exportar o EXE portátil, instale os templates Windows .NET correspondentes ao Godot 4.7.2 e execute `godot --headless --path . --editor --export-release "Windows Desktop"`. O preset versionado gera `dist/Jota-Fantasy-Map-Maker-Alpha-1.1.31.exe`, com PCK e runtime incorporados.

## Agradecimentos

Agradecemos a Azgaar e a toda a equipe de colaboradores do **Fantasy Map Generator**. Suas receitas de mapas de elevação inspiraram a escultura continental do Jota Fantasy Map Maker. [Azgaar’s Fantasy Map Generator](https://github.com/Azgaar/Fantasy-Map-Generator)

Os testes específicos do gerador podem ser executados com `dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj` (SDK .NET 10). Eles verificam a consolidação de biomas, limites climáticos, wrap, determinismo, regeneração regional e locks.
