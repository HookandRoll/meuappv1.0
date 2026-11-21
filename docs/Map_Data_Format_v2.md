# Estrutura de dados e renderer — esquema 2

Esta estrutura separa os campos do mapa. Na Alpha 1.1.32, o gerador v45 reúne classificação por desnível, clima revisado, swamp e propagação dos biomas. O formato também prepara os demais biomas e características para integração futura, sem exigir reutilizar os IDs existentes.

## Campos independentes por hex

| Campo | Tipo | Capacidade |
| --- | --- | --- |
| `Biomes` | `BiomeType : ushort` | 65.536 IDs, incluindo `None` |
| `Relief` | `ReliefType : ushort` | 65.536 IDs |
| `Landforms` | `LandformType : ushort` | 65.536 IDs |
| `Features` | `GeographicFeatures : ulong` | 64 características combináveis |

Bioma, relevo visual, forma geográfica e características coexistem: por exemplo, floresta temperada sobre um planalto com cânion e escarpa. Água continua independente; gelo marinho não precisa virar terra. Os IDs existentes não foram renumerados. Valores futuros de 16 bits e bits ainda não cadastrados são preservados ao copiar e salvar.

`LandformType` reserva planície, planície ondulada, planalto, escarpa/serra, morros, montanhas, pico e vale. Features reservam vulcão, escarpa, cânion, mesas rochosas e gelo marinho. A classificação das formas geográficas está ativa na v41; a geração automática dessas features ainda não foi ativada.

## Planos enviados à GPU

Cada plano usa RGBA8, amostragem nearest e bytes exatos sem correção de cor:

| Plano | R | G | B | A |
| --- | --- | --- | --- | --- |
| Terreno | Elevação low | Elevação high | Bioma low | Bioma high |
| Superfície | Água | Relevo low | Relevo high | Flags de desenho |
| Geografia | Forma low | Forma high | Densidade da costa | Estado de locks |
| Features low | Bits 0–7 | 8–15 | 16–23 | 24–31 |
| Features high | Bits 32–39 | 40–47 | 48–55 | 56–63 |
| Clima | Temperatura low | Temperatura high | Chuva low | Chuva high |
| Detalhes | Ícone low | Ícone high | Flags de zoom | Distância da terra |

Elevação e temperatura mantêm o deslocamento de `short.MinValue`. Os IDs não compartilham bits com água, costa, neve ou zoom. `MapRenderFormat` centraliza a codificação compartilhada pelo mapa e pela prévia do editor.

Superfície/geografia usam camadas de um `Texture2DArray`. Nas flags de desenho da superfície, 32 indica neve, 64 símbolo de relevo e 128 vegetação; 16 identifica o relevo colocado sobre contorno, com a direção de descida + 1 nos três bits baixos. O bit 8 identifica ramificações e grupos no interior alto, com arte centrada. Nesses grupos o ID de relevo enviado à GPU é uma escolha de arte derivada; a classificação salva permanece independente. Esses metadados são derivados para o renderer e não alteram os IDs salvos. Texturas de features no tamanho do mapa só são alocadas quando há características; caso contrário, usam um zero de 1 × 1. A prévia empilha cinco planos verticalmente em cada faixa de 32 linhas, mantendo a quantidade de camadas igual à quantidade de faixas, inclusive no maior atlas.

Catálogos de lookup com endereço de 16 bits mapeiam bioma → solo/vegetação, relevo → arte e ícone → arte. IDs sem arte usam fallback seguro, sem acessar camadas inválidas. A inspeção identifica valores desconhecidos pelo ID. A capacidade numérica não implica que todas as artes possam residir simultaneamente na GPU; os limites de textura do dispositivo continuam existindo.

## Arquivo `.world` v2

O ZIP mantém manifesto, entidades esparsas, tectônica e prévia. Os arrays deixam o bloco único e passam a entradas independentes `layers/<nome>.bin`, em little-endian:

- `elevation` e `temperature`: 2 bytes com sinal por hex.
- `biomes`, `relief` e `landforms`: IDs de 2 bytes.
- `features`: flags de 8 bytes.
- `rainfall`, `roads` e `locks`: 2 bytes sem sinal.
- `water` e `flags`: 1 byte.

Os tamanhos são validados antes de alocar o mapa. `landforms` e `features` são opcionais na leitura: ausência significa `None`. Rios e costa mantêm sua reconstrução habitual. Novas camadas podem ser acrescentadas sem deslocar as existentes, mas ainda exigem regras explícitas de leitura, escrita e compatibilidade.

Saves v1 continuam abrindo: seus IDs de um byte são expandidos sem renumeração; locks antigos de hex inteiro incluem os novos campos. Ao salvar, o formato passa a v2, mantendo `GeneratorVersion` e geografia. A escrita por temporário continua atômica. Executáveis antigos que só suportam v1 não abrem os novos arquivos v2.

## Edição e verificações

Formas e features participam da cópia de rascunho e do undo/redo. O orçamento de snapshots usa o tamanho real da célula. A forma geográfica pode ser atribuída no editor de hex e possui lock próprio; features têm lock separado e aparecem na inspeção quando presentes. A v41 recalcula formas geográficas e relevo na geração e na regeneração completa, respeitando os locks; o campo específico Forma geográfica recalcula somente essa classificação. Features permanecem preservadas. As cópias das faixas polares antigas também os preservam.

Build sem erros ou avisos; suíte completa do núcleo, smoke de 524.288 hexes, revisão visual e fluxo do editor com 20 regenerações passaram. Os testes específicos cobrem IDs 32.769/60.000/65.535, bit 63, features combinadas, arquivos v1/v2, migração, camadas opcionais, corrupção, cópia, undo/redo, locks e regeneração. A conferência na GPU lê as texturas e os pixels produzidos pelo shader real, além da prévia e dos ícones de 16 bits. O editor também verifica alteração independente de forma geográfica, undo/redo, lock e cancelamento.

```text
dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj -- --format-only
dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj
godot --path . -- --verify-map-format
godot --path . -- --validate-editor-current
godot --headless --path . -- --smoke-test --current-generator-only
godot --path . -- --ocean-frame-review
```
