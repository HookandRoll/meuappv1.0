# Geração v18 — implementação e verificação

Data: 18/11/2026. Alterações locais no workspace; sem commit ou publicação de release.

## M3 confirmado

O gate do M3 passou antes desta implementação, no gerador v17, com build sem avisos e smoke headless/renderizado. Evidências em [M3_Fechamento.md](M3_Fechamento.md).

O smoke foi ampliado para repetir o fluxo de edição nas versões 17 e 18: seleção cruzando quatro chunks, pinceladas, formas/força, locks, regeneração, campo único, undo/redo, salvar/salvar como, cancelar e reabrir. A nova geração não substitui a versão registrada nos mundos antigos.

## Implementado

| Sistema | Implementação |
|---|---|
| Fundação tectônica | Placas temporárias com sementes espaçadas, movimentos em direções hexagonais adjacentes, ajuste simultâneo de velocidade por vizinhas reais e classificação normal/tangencial dos contatos. |
| Geografia | Posição dos núcleos continentais favorecida pela crosta procedural; domos, colisões, cristas, rifts e fossas aplicados com influência espacial limitada. |
| Referência tectônica | Placas e segmentos de fronteira compactos, imutáveis, serializados em `tectonics.json`. Nenhum array de tectônica por hex fica no mundo. |
| Layer | “Tectônica” mostra flechas de movimento, círculos de afastamento e X de colisão. Tooltip explica a legenda e a natureza de referência original. Permanece igual ao editar o terreno. |
| Costa | Eventos localizados e espaçados de península, baía e vale costeiro; a escultura continua usando campos contínuos e quota de terra conectada. |
| Ilhas | Etapa própria, com reservas de água e espaçamento do v17, favorecendo corredores próximos às fronteiras convergentes. |
| Chuva | Transporte simplificado de umidade por amostragem a barlavento; precipitação adicional nas subidas e redução de umidade disponível atrás de barreiras. Usa relevo final, não a layer tectônica. |
| Neve | Doze passos climatológicos temporários: precipitação acumulada em neve, fusão limitada ao estoque e perdas. Não há simulação permanente nem água adicionada por uma constante. |
| Drenagem | Priority-Flood com superfície auxiliar, ordem determinística e desempates espaciais. Elevação do terreno não é sobrescrita pelo preenchimento. |
| Lagos | Componentes lacustres existentes recebem um sangradouro único na drenagem auxiliar; contribuições são somadas e evaporação é descontada no outlet. Sem excedente, não há fluxo fluvial de saída. |
| Rios | Entidades persistentes com caminhos, largura por aresta, fluxo anual e ligação de afluentes. `RiverEdges` e índice por hex são reconstruídos no load. |
| Fertilidade | Na geração/regeneração de bioma, células quentes de deserto/estepe atravessadas por rio podem virar campo; chuva armazenada não é falsificada e locks de bioma são respeitados. |
| Visual fluvial | Curvas quadráticas dentro dos hexes, sockets compartilhados, malhas agrupadas por chunk e shader de correnteza com UV ao longo do curso. |
| Editor | Campo “Rios” em regeneração, lock específico de rios, undo/redo de entidades e caches, restauração visual após cancelar. |
| Regeneração regional | Mantém arestas externas, pontos de confluência e locks; pode redirecionar trechos internos entre âncoras fixas. Novos cursos independentes devem caber na região permitida. |

## Adaptações ao plano

- A tectônica é uma receita estática de criação. Não simula evolução temporal, não move placas durante o jogo e não recalcula ao salvar ou abrir.
- A malha auxiliar de placas é limitada a 128 × 64 amostras. Os contatos são interpolados em triângulos para evitar contornos em degraus. A grade auxiliar é descartada; ficam apenas metadados e segmentos.
- A crosta é uma influência procedural de domínios e domos, com um tipo predominante por placa para orientar subducção. Não é um modelo físico detalhado de idade, densidade e composição da crosta por célula.
- A chuva orográfica é uma aproximação espacial limitada, não um solver atmosférico global. O vento depende da faixa de latitude.
- A neve usa um ano climatológico com estoque inicial zero. O saldo retido não vira uma geleira persistente; não existe calendário climático em execução.
- O balanço dos lagos calcula excedente anual sobre a geometria existente. Não altera dinamicamente nível/área do lago, não simula salinidade e não cria novos lagos hidrológicos nesta versão.
- Na regeneração regional, a continuidade com os trechos protegidos prevalece. Se uma busca interna não conseguir substituir o caminho, o trecho anterior é conservado. Rios editados não precisam obedecer ao relevo novo a qualquer custo.
- O visual atual usa curvas dentro do corredor lógico. Meandros de escala maior, deltas, espuma elaborada e LOD específico de rios continuam como refinamentos visuais.
- Erosão que altera altitude, simulação glacial dinâmica e edição de placas ficaram fora deste recorte, conforme o escopo de geração estática.
- O M5 completo ainda inclui ferramentas de desenho/apagamento manual de redes e estradas. Esta entrega implementa geração, persistência, proteção, regeneração e visual de rios; não declara o M5 fechado.

## Compatibilidade

O gerador atual passa de v17 para v18 para mundos novos. Fractal e Centro de Massas continuam indisponíveis na criação; código legado permanece para arquivos antigos. Os testes originais fixam explicitamente v17 e a nova matriz exercita v18.

O layout denso do `.world` continua em 14 bytes por hex. A referência tectônica é uma entrada adicional opcional no ZIP; rios usam `sparse_entities.json`. Saves antigos sem essas informações continuam abrindo. O teto de entidades esparsas passa a ser o maior entre 8 MiB e 64 bytes por hex validado (máximo de 512 MiB), e o JSON é gravado compacto. O save rejeita entidades acima desse teto antes de substituir o arquivo. O load reconstrói caches fluviais a partir das entidades salvas, sem regenerar geografia.

## Verificações

- `dotnet build --no-restore`: zero avisos e erros.
- Matriz de 18 configurações em 128 × 64: seeds 1234, 1 e −77; terra 30%, 40% e 50%; wrap ligado/desligado.
- Repetibilidade de elevação, chuva e entidades; quota de terra com tolerância de um hex.
- Save/load de referência tectônica e arestas fluviais; caminhos sem ciclos; fluxo não decrescente fora de perdas lacustres declaradas.
- Cenário seco com neve não produz água; cenário úmido produz rede fluvial; orçamento anual nunca excede a precipitação na função de runoff com estoque inicial zero.
- Regeneração regional preserva exterior e arestas travadas, e undo/redo restaura entidades/caches.
- Smoke completo com fluxo de edição para v17 e v18: `.godot/final-v17-v18.log`.
- Capturas de terreno, tectônica e chuva, mais arquivos de mundo em [natural-v18](natural-v18).
- Caso 1024 × 512 salvo/reaberto com comparação dos dados densos e da referência tectônica.

Os logs do ambiente apresentam o aviso já existente de certificados e, em algumas execuções renderizadas, falha de escrita do cache de shaders GLES3. Não houve falha de compilação do shader fluvial; imagens foram produzidas e conferidas. Não foi realizado benchmark no tamanho máximo de 4096 × 2048.

## Medições indicativas

Uma execução local, seed 1234; tempos medem criação dos dados, não o upload de texturas ou a construção das malhas:

| Mapa | Terra | Criação |
|---|---:|---:|
| 512 × 256 | 30% | 1,955 s |
| 512 × 256 | 40% | 1,645 s |
| 512 × 256 | 50% | 1,590 s |
| 1024 × 512 | 38% | 9,801 s |

Não são médias de benchmark. A geração ficou mais cara que a v17; continua executando em worker, fora das pinceladas do editor.

No caso 1024 × 512, a referência tectônica ocupa aproximadamente 202 KB de JSON, cerca de 18 KB comprimidos no `.world`. Esses números medem armazenamento, não o pico de memória do processo. Não há os seis campos tectônicos globais de 192 MiB propostos originalmente.

## Arquivos centrais

- `src/Core/TectonicReference.cs`
- `src/Core/WorldGenerator.ContinentalSculpt.cs`
- `src/Core/WorldGenerator.Islands.cs`
- `src/Core/WorldGenerator.Hydrology.cs`
- `src/Core/WorldGenerator.cs`
- `src/Core/WorldData.cs`, `WorldFile.cs`, `UndoManager.cs`
- `src/GodotApp/TectonicOverlay.cs`
- `src/GodotApp/RiverOverlay.cs`, `River.gdshader`
- `src/GodotApp/MapView.NaturalVerification.cs`

Para testar visualmente: criar um mundo novo com Azgaar e geração completa, trocar a camada para Tectônica e depois Chuva, aproximar o zoom para ver os rios, abrir o editor e usar Locks/Gerar. Mundos v17 continuam usando a receita antiga.
