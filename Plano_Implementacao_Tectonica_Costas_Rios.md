# Plano de implementação — tectônica, costas e rios

Revisão: 18/11/2026. Base inspecionada: workspace do Mapmaker, gerador v17.

Este documento registra a análise e o plano original. A implementação inicial da geração v18 foi realizada em 18/11/2026; consulte [o relatório da implementação](results/Implementacao_Gerador_v18.md) para distinguir o que está entregue das extensões futuras. Os números de calibração sugeridos neste plano continuam sendo hipóteses, salvo medições identificadas no relatório.

## 1. Decisão geral

### Escopo corrigido após esclarecimento do usuário

O projeto chegou ao M3. Esta proposta é um aprimoramento da geração do M2, integrado às garantias de edição do M3; não é um novo sistema de simulação permanente. O M4 continua tratando apresentação e o M5 continua sendo o marco de rios e redes. As fases de hidrologia deste documento são planejamento futuro, não pré-requisitos para melhorar a geografia agora.

**A tectônica existe somente durante a geração ou regeneração explicitamente solicitada.** Gerar placas simplificadas, calcular colisão/afastamento/cisalhamento, transformar esses resultados em continentes, montanhas, cordilheiras e ilhas, e descartar os dados tectônicos temporários. Não há atualização por frame, avanço temporal, estado geológico persistente, edição de placas ou necessidade de carregar tectônica ao abrir um mundo.

Implementação preferida: uma estrutura auxiliar grosseira para placas e contatos, convertida numa lista compacta de feições (domínios continentais, eixos de cordilheiras, fossas, rifts e arcos de ilhas). Aplicar essas feições diretamente aos scores e ao relevo já usados pelo gerador. Não criar seis campos tectônicos do tamanho do mundo. O clima consome o relevo final; as ilhas recebem as curvas/âncoras temporárias. Liberar cada estrutura após seu último consumidor.

Salvar somente o terreno resultante e seed/configurações/versão necessárias à reprodução. Regenerar uma região pode reconstruir a receita global temporária para manter coerência, mas só grava na seleção e respeita locks. Pintar, navegar, salvar e reabrir não executam tectônica.

As sugestões abaixo de campos tectônicos e layers são conceitos de cálculo e ferramentas opcionais de desenvolvimento, não arrays persistentes nem features obrigatórias. Esta definição de escopo prevalece sobre sugestões exploratórias posteriores.


**Aproveitar os três sistemas, nesta ordem: tectônica → escultura continental e costeira → relevo final → clima → drenagem e rios → biomas → apresentação.** A direção proposta é boa: as cordilheiras, os arquipélagos e as bacias passam a compartilhar causas espaciais, em vez de parecerem camadas sorteadas sem relação.

Construir uma **tectônica procedural estática**, inspirada nos efeitos das placas. As placas recebem movimentos hipotéticos usados para gerar relevo; não deslocaremos continentes ao longo de milhões de anos. Uma simulação temporal real exigiria transporte de crosta, conservação de área, criação/destruição de material e reconstrução da malha, fora deste recorte.

Manter Azgaar como único método selecionável. Tectônica será uma configuração desse método, não um quarto gerador. Preservar as implementações antigas para abrir e regenerar mundos antigos com sua versão registrada.

As três revisões melhoraram a integração e corrigiram unidades, parte do empacotamento dos rios e o uso de buffers. Entretanto, alguns exemplos continuam incompatíveis com o código ou não entregam o comportamento prometido. Não devem ser copiados literalmente.

## 2. O que já existe e será reaproveitado

| Componente atual | Uso proposto |
|---|---|
| `HexGrid` | Vizinhança, coordenadas, distância com wrap e `SetSharedEdge`, que escreve os dois lados da aresta. |
| `WorldGenerator.ContinentalSculpt.cs` | Campos contínuos, curvas, scores, cristas, ruído e mecanismos legados versionados. |
| `WorldGenerator.Continents.cs` | Crescimento conectado, orçamento de terra e separação de massas. Adaptar suas restrições para receber a fundação tectônica. |
| `WorldGenerator.Islands.cs` | Etapa independente de ilhas, crescimento conectado e reservas marítimas entre ilhas e continente. |
| `WorldGenerator.cs` | Pipeline, unidades de relevo/clima, classificação de biomas e entrada da regeneração. |
| `WorldGenerator.GenerationArea.cs` | Máscaras de escrita, recorte e preservação de campos. Não resolve sozinho dependências globais de vento e rios. |
| `WorldData.cs` | Arrays por campo, manifesto, cópia de edição, `RiverEdges` e entidades esparsas. |
| `UndoManager.cs` | Snapshots densos por chunk. Precisará incluir entidades esparsas modificadas. |
| `WorldFile.cs` | Save transacional e container `.world`. Precisará interpretar entidades fluviais e reconstruir caches ao carregar. |
| Editor em snapshot e preview simplificado | Continuar editando sem executar tectônica, clima ou drenagem global durante cada movimento do pincel. |
| Renderer atual | Terreno contínuo, chunks, layers e coordenadas de mapa para integrar o desenho dos rios. |

No código atual, `RiverEdges` existe em RAM e no undo denso, mas **não é gravado em `dense_data.bin`**. O save escreve 14 bytes por hex e guarda entidades esparsas separadamente. O carregamento reconstrói costa, mas ainda não reconstrói rios. A afirmação da nova especificação de que esse array já está serializado precisa ser corrigida.

## 3. Revisão da tectônica

### 3.1. Aproveitar

- Sementes bem distribuídas, territórios conectados e grafo de adjacência.
- Movimento representado por uma direção ou duas direções hexagonais adjacentes.
- Classificação por velocidade relativa normal e tangencial.
- Efeitos distintos de convergência, divergência e cisalhamento.
- Influências com largura, deslocamento para o lado correto e intensidade variável.
- Campo tectônico consumido pelas etapas seguintes e layer de inspeção.

### 3.2. Separar placa, crosta e continente

Uma placa pode carregar crosta continental e oceânica. Isso é uma distinção geológica básica: não convém transformar todo contorno de continente em limite de placa. Referência: [USGS — What is a tectonic plate?](https://pubs.usgs.gov/gip/dynamic/tectonic.html).

**Mudança proposta:** `PlateId` identifica movimento; `ContinentalFraction` ou um campo equivalente identifica a crosta de cada célula. O tipo predominante pode continuar nos metadados da placa, mas as interações devem consultar a crosta local nos dois lados da fronteira.

Os 40% de placas continentais não garantem 40% de terra: placas têm áreas distintas, plataformas podem estar submersas e o orçamento final controla a área emersa. `Continents` deve continuar significando massas principais separadas, não quantidade de placas.

Gerar o campo de placas e os domínios de crosta primeiro. Depois escolher os núcleos continentais dentro dos domínios favoráveis, respeitando a quantidade solicitada. Assim a tectônica influencia a posição global de verdade.

### 3.3. Corrigir escala e contagem

A fórmula publicada `round(sqrt(area) * 0.06) + 4` não produz os exemplos do comentário:

| Dimensão | Resultado efetivo aproximado |
|---|---:|
| 128 × 96 | 11 |
| 256 × 192 | 17 |
| 512 × 256 | 26 |
| 1024 × 512 | 28, limitado pelo teto |

Ela satura cedo e confunde resolução com complexidade geológica. Recomendo um parâmetro `PlateCount` automático com override, baseado na extensão normalizada do mundo e no perfil de geração. Definir uma curva e exemplos coerentes após comparar mapas; não tratar 6–28 como lei física.

Nesta primeira versão, manter a convenção atual: alterar apenas `HexDistance` muda a escala informada, não redesenha continentes. Larguras tectônicas serão proporcionais às dimensões do mapa, com limites mínimos em hexes. Um futuro modo de escala física deve ser explícito e versionado.

### 3.4. Territórios e movimentos

O Dijkstra multiorigem é utilizável, com custo estritamente positivo, desempate por ID e ruído periódico no eixo horizontal. Ruído por hex e fila hexagonal ainda podem produzir direções preferenciais: fronteiras curvas não são garantidas só por usar ruído.

Primeiro implementar com custo espacial compartilhado e bem definido. Se cada placa tiver resistência diferente, especificar como isso altera a competição; não inserir um multiplicador por placa supondo que qualquer variante de Dijkstra manterá as mesmas propriedades.

Calcular vetores usando os centros reais do `HexGrid`; não usar deslocamentos crus de coluna/linha, que distorcem ângulos. Com wrap, usar o menor deslocamento horizontal também para normais, centroides e distâncias. Centroide de placa atravessando a costura exige coordenadas desembrulhadas ou média circular.

**As 3–5 placas influentes podem servir como simplificação artística, mas não substituem todas as vizinhas reais.** Classificar todas as fronteiras compartilhadas. Para ajustar velocidade, usar prioritariamente contatos reais, ponderados por comprimento e orientação. Placas distantes não devem gerar colisões locais.

O produto escalar entre direções, sozinho, não determina empurrão: duas placas paralelas podem se mover juntas sem comprimir a fronteira. Usar velocidade relativa projetada na normal do contato. Calcular ajustes a partir de uma cópia das velocidades base e aplicar simultaneamente, evitando resultado dependente da ordem das placas.

Manter a regra de uma ou duas direções adjacentes como controle estilístico. Ela limita os ângulos possíveis, especialmente com peso secundário fixo em 0,75. Se aparecerem alinhamentos repetidos, variar deterministicamente esse peso mantendo as direções permitidas.

### 3.5. Fronteiras e propagação

- Processar cada aresta compartilhada uma vez.
- Suavizar a normal ao longo de um trecho de fronteira antes de classificar: a normal de cada degrau hexagonal pode alternar artificialmente entre colisão e afastamento.
- Incluir caso neutro e possibilidade de componentes oblíquas; evitar mudanças bruscas por limiar.
- Escolher explicitamente a placa que subducta em contato oceano–oceano, por idade/densidade procedural e desempate estável. O tipo de crosta sozinho não escolhe o lado.
- Completar o caso divergente com crostas locais distintas, ausente da tabela.
- Não associar automaticamente velocidade baixa a cadeia antiga: idade/erosão é outro parâmetro. Velocidade relativa de convergência importa mais que duas etiquetas “Alta”.
- Acrescentar `TrenchDepth` ou equivalente: o contrato atual promete fossas, mas não possui um campo específico para sua profundidade.
- Manter arcos vulcânicos deslocados para a placa superior; não colar as ilhas à linha da fossa.
- Fazer propagação limitada por raio e lado da fronteira, preferencialmente com campos contínuos. Evitar somar cada célula de fronteira independentemente e superaquecer trechos sinuosos; usar distância ao trecho e composição limitada de influências.

`TectonicField` não é imutável apenas por ter propriedades `init`: arrays e objetos internos continuam mutáveis. Encapsular buffers após construção e expor leitura; metadados finais devem deixar de ter setters públicos.

## 4. Revisão de ilhas, baías e penínsulas

### 4.1. Aproveitar a composição, substituir fórmulas inseguras

Os cinco operadores são úteis como interface conceitual: elevar domo, elevar crista, escavar bacia, escavar vale e abrir passagem. Podemos implementá-los sobre o `ContinuousField` atual, sem abandonar a ideia Azgaar.

**O `AddHill` revisado continua sem decaimento garantido.** Para `0 < x < 1` e expoente menor que 1, `pow(x, p) > x`; por exemplo, `0,5^0,95 ≈ 0,518`. Mesmo valores maiores que 1 tendem a 1 quando o expoente é menor que 1. Jitter até 1,12 e boosts não consertam isso. O limite de parada 0,05 pode nunca ser atingido antes de visitar quase todo o mapa.

O `AddRange` tem risco parecido: `pow(h, linePower) - 0,06` não garante queda para todos os parâmetros e valores. A fila finita encerra a execução, mas não impede uma crista de ocupar o mundo.

**Implementação proposta:** amplitude assinada × perfil de distância com suporte finito. Exemplo: `delta = amplitude * (1 - smoothstep(0, radius, distance))`, zero fora do raio. Perturbar distância/raio com ruído contínuo limitado. Para cristas, medir distância à curva. `AddPit` e `AddTrough` reutilizam a geometria com sinal negativo. Se houver BFS, impor raio máximo além de perda monotônica garantida.

Embaralhar vizinhos reduz viés da ordem de visita, mas não elimina a geometria de 60° do grafo. Preservar o campo contínuo que o gerador atual já utiliza. Stamps e buffers reutilizáveis são aproveitáveis; prever reset seguro quando o contador se esgotar e evitar `stackalloc` repetido dentro de loops extensos.

O passeio de `AddRange` pode não alcançar o destino: limite de passos e exclusão de visitados podem interrompê-lo. Uma cordilheira incompleta pode ser aceitável; um estreito que promete conectar dois mares precisa verificar chegada e conectividade, com fallback determinístico ou rejeição do evento.

### 4.2. Resolver o conflito entre percentil e continentes separados

A especificação diz que `FindSeaLevel` define o percentil exato. No código atual, o método usa histograma aproximado; o Azgaar v17 segue outro caminho, `BuildSeparatedContinents`, com crescimento conectado, quotas e máscara explícita de água.

Recomendação: **preservar o orçamento conectado**, adaptando scores e âncoras à tectônica. Um percentil global simples pode juntar continentes, submergir ilhas planejadas e destruir canais. Se algum dia substituirmos esse sistema, precisaremos revalidar todas essas garantias.

Usar nível provisório apenas para localizar a costa candidata. Aplicar eventos antes de consolidar a máscara, proteger canais e ilhas aceitos e ajustar a quota final nas áreas elegíveis. Não recalibrar o mar globalmente depois e supor que os eventos permanecerão intactos.

Se as restrições tornarem a quota impossível, registrar alvo e área obtida. Nunca fechar silenciosamente os canais obrigatórios para acertar o percentual.

### 4.3. Preservar a maioria das costas limpas

Os percentuais 40/35/25 devem representar a mistura dos **eventos escolhidos**, não toda a costa. Distribuir eventos em trechos espaçados, com orçamento de extensão afetada, distância mínima e tamanho variável.

Ponto de partida para calibração: manter 70–80% da extensão litorânea sem recorte adicional forte. Medir a extensão da união das áreas afetadas, pois poucos eventos grandes podem alterar quase toda a costa. O limiar `roughness < 0,25` sozinho não garante essa proporção; calibrar pela distribuição efetiva do campo.

Fiordes devem ser raros e favorecer costa montanhosa com contexto frio/glacial. Frio ou rift isoladamente não basta para chamar qualquer canal de fiorde. Baías de rift e vales afogados podem usar outra receita visual.

Evitar suavização global após aceitar ilhas e estreitos: ela pode apagar ilhotas e fechar canais. Suavizar antes ou restringir a operação mantendo máscaras protegidas. Atenuar bordas por interpolação para um piso oceânico negativo, nunca simplesmente multiplicando scores negativos por zero.

### 4.4. Ilhas continuam sendo uma etapa própria

Usar `VolcanicArcMask` para escolher corredores, não para elevar uma faixa inteira. Dentro de cada corredor, colocar ilhas individuais com intervalos variáveis, tamanhos desiguais, curvas e falhas na sequência. Incluir ilhas oceânicas ocasionais fora dos arcos para evitar repetição.

Reaproveitar `coastGap`, `islandGap` e o orçamento de terra do v17. Medir distância entre **contornos**, não apenas entre sementes. Validar depois de toda compensação de terra. Uma ilha pode nascer pequena e virar ponte se o crescimento posterior ignorar a reserva.

O `AddStrait` de 1–3 anéis é possível para uma passagem específica; não deve ser o mecanismo padrão para produzir arquipélagos. Separar ilhas recortando um único bloco recria o aspecto de peças de quebra-cabeça mostrado nas capturas anteriores.

## 5. Revisão de chuva, degelo e rios

### 5.1. Unidades e fonte da verdade

Manter altitude em metros, temperatura em décimos de °C e precipitação em mm/ano. Usar `WaterType` para identificar água: altitude negativa não é uma definição universal, e o sistema inclui pântanos. Os limiares de bioma citados no MD são heurísticas, não contratos universais de altitude.

Criar entidades `River` persistentes com caminho ordenado, destino, afluente/pai e largura por trecho ou informação suficiente para derivá-la. `RiverEdges` e `HexToRivers` serão caches reconstruídos. Uma única `WidthClass` por rio, como no modelo antigo, não expressa crescimento de largura ao longo do curso: estender o contrato de forma explícita.

Os dois bits por aresta indicam **porte e conexão**, não direção de fluxo. Direção vem da ordem do caminho ou de `flowTo` auxiliar. Reutilizar `HexGrid.SetSharedEdge`; o `SetEdgeTier` do exemplo atualiza só um lado e só aumenta valores, portanto não atende redução, exclusão nem reconstrução.

Os quatro bits superiores podem tecnicamente conter flags, mas recomendo mantê-los reservados inicialmente e derivar nascente/confluência/foz das entidades. Se forem usados, criar contrato de cache e auditoria dos consumidores: `RiverEdges != 0` passaria a ser verdadeiro mesmo sem uma aresta ativa. “Grande rio” também não implica navegabilidade garantida; isso depende de regras futuras de profundidade, declive e obstáculos.

### 5.2. Chuva orográfica precisa de uma etapa concreta

O documento menciona chuva orográfica, mas o exemplo de rios apenas lê `Rainfall`; não implementa transporte de umidade. Criar ventos predominantes, entrada de umidade sobre água e perda por precipitação ao subir o **relevo final**. Reduzir umidade disponível a sotavento.

Usar altitude e seu gradiente, não aplicar chuva diretamente em `TectonicRidge`: uma cadeia erodida ou um terreno editado deve ter efeito compatível com sua altura atual. Definir número limitado de iterações, troca periódica na costura e condição de parada; não depender da ordem de varredura dos chunks. Medir balanço de umidade e evitar dupla aplicação da chuva base e do acréscimo orográfico.

### 5.3. Degelo não pode inventar água

O código revisado ainda usa `rainFactor * 1,65 + 0,25` e um termo glacial constante. Isso produz escoamento mesmo com precipitação zero e pode exceder a água disponível.

Primeira entrega: escoamento anual limitado pela precipitação efetiva após perdas, com fração nival estimada e indicador de contribuição de neve. Não apresentar isso como uma simulação sazonal de geleiras.

Extensão: passos mensais climatológicos, partição chuva/neve, estoque de equivalente de água na neve, fusão limitada pelo estoque e perdas. Se usarmos aquecimento inicial por ciclos anuais, limitar iterações e distinguir equilíbrio periódico de acumulação glacial persistente. Uma geleira pode liberar água antiga apenas se houver estoque inicial e redução contabilizada. A neve armazena precipitação e a libera posteriormente; não é uma fonte independente. [USGS — Snowmelt Runoff](https://www.usgs.gov/water-science-school/science/snowmelt-runoff-and-water-cycle).

Para fluxo físico médio, usar `volumeAnualM3 = runoffMm * areaKm2 * 1000`, depois converter para m³/s. Com distância entre centros `d`, a área plana de um hex regular é `sqrt(3)/2 * d²`; a 5 km, aproximadamente 21,65 km². É a métrica do mapa, não correção esférica de uma projeção terrestre.

Decisão inicial: usar volume anual e limiares visuais calibrados para resolução; registrar parâmetros. Alterar escala física poderá mudar volume/porte derivado, mas não deve mudar automaticamente a silhueta ou editar rios salvos. A mudança ocorre em geração/regeneração explícita. Não copiar os limiares 14/45/140 sem essa definição de unidade.

### 5.4. Priority-Flood: usar, com contrato completo

É adequado à grade hexagonal: o trabalho original admite malhas irregulares e grades com seis vizinhos. A superfície preenchida deve ser auxiliar, preservando a elevação editável. [Barnes, Lehman e Mulla — Priority-Flood](https://arxiv.org/abs/1511.04463).

Corrigir os seguintes pontos do pseudocódigo:

1. O comentário inclui bordas abertas, mas o código semeia apenas oceanos. Definir saídas para mapas sem oceano, mundos editados e bordas físicas. Com wrap, a costura leste–oeste não é saída.
2. Não usar `-1` como altura física de todo corpo de água: oceano tem superfície de referência; lagos precisam de nível de superfície e sangradouro distintos da profundidade do fundo.
3. Usar prioridade composta estável, por exemplo altura + ID, e ordem de vizinhos definida. Empates não podem depender de ordem interna da fila.
4. Preferir hierarquia de drenagem/parentes e ordem topológica para resolver áreas planas. Um epsilon fixo acumulado por milhares de células pode inventar rampas virtuais extensas. Se utilizado, demonstrar sua segurança numérica.
5. A lista atual só acumula terra. Fluxo que entra em um lago para ali: o código não encaminha água do lago ao sangradouro. Representar lagos como nós de bacia com entrada, superfície, evaporação e saída.
6. O texto sobre lagos endorreicos exige balanço hídrico próprio; não é um resultado automático do preenchimento. Separar escolha de saídas, balanço e roteamento, com iterações limitadas quando dependentes.
7. Definir destino para `Wetland` e distinguir bacias pequenas resolvidas apenas virtualmente de lagos novos que alteram `WaterType`.

Primeiro implementar lagos existentes como terminais explícitos e rios chegando ao oceano. Depois entregar conectividade lago → sangradouro → rio e balanço endorreico. Não declarar o sistema completo antes dessa segunda parte.

### 5.5. Acúmulo, entidades e erosão

Acumular em ordem topológica do grafo acíclico de drenagem, incluindo nós lacustres. Usar precisão suficiente para bacias grandes. Em confluências, somar contribuições sem duplicar o trecho compartilhado; selecionar tronco por fluxo e desempate estável.

Nem todo rio deve nascer em pico: limiar de área contribuinte/escoamento permite nascentes em terras baixas úmidas. Espaçar nascentes e remover cursos visuais irrelevantes sem retirar sua contribuição ao balanço. Um único `flowTo` não representa deltas com bifurcação; deixar deltas para extensão explícita.

Adiar o rebaixamento arbitrário de 20–120 m. Aplicá-lo depois dos rios invalida declives, clima, biomas e possivelmente terra/água. Se incluirmos erosão, será etapa limitada antes da consolidação final, seguida de recálculo de drenagem e campos dependentes, respeitando locks. Não executar ciclos até convergir sem limite.

Fertilidade fluvial deve ser dado derivado ou camada própria, não aumento fictício da chuva regional. Alteração de bioma por rio deve acontecer somente na geração/regeneração autorizada e preservar biomas travados.

### 5.6. Visual recomendado

Adotar ribbons agrupados por chunk como candidato principal para curvas contínuas e zoom próximo. Comparar um protótipo simples com as peças modulares previstas no M5 antes de fechar a escolha; o renderer permanece substituível.

- Gerar uma geometria canônica por rio, depois recortar para chunks, com pontos e tangentes idênticos nas emendas.
- Ancorar travessias no ponto médio das arestas compartilhadas e confluências no mesmo ponto lógico.
- Limitar meandros ao corredor dos hexes do caminho; splines podem ultrapassar seus pontos de controle e entrar em hexes sem rio.
- Usar comprimentos acumulados para UVs e animação contínua, inclusive entre chunks e na costura do mundo.
- Controlar largura em curvas fechadas, junções, cabeceiras e foz; evitar auto-interseções e sobreposição escura de afluentes.
- Aplicar LOD e culling; “nitidez infinita” não é literal, pois malha, tesselação e antialiasing têm custo.
- Verificar ordem de desenho com vegetação, ícones, grid e seleção. Uma fita sobreposta não respeita automaticamente a costa desenhada pelo shader.

Uma textura global RGBA8 a 4× em cada eixo, num mapa 4096×2048, ocupa **512 MiB por cópia**, sem contar staging ou outras texturas; a 2×, 128 MiB. Preferir tiles ou malha para evitar esse custo global. Máscara e SDF não são o mesmo contrato. O exemplo chama R de SDF, mas o consome como máscara e ignora A; definir uma representação e seu cálculo de largura. `map_position` precisa de origem/extensão consistentes com o raster e amostragem periódica na costura. A inclusão no shader existente pode evitar um passe extra, mas não garante sozinha clipping correto ou custo desprezível.

## 6. Pipeline final proposto

```text
Validar manifesto, versão e parâmetros
  → seeds independentes por subsistema
  → placas, crosta local, movimentos e fronteiras
  → campos tectônicos e âncoras continentais
  → scores continentais, cristas e costa provisória
  → eventos costeiros espaçados e reservas de canais
  → continentes conectados + ilhas independentes + quota de terra
  → máscara final terra/água, costa e elevação
  → temperatura e transporte orográfico de umidade
  → drenagem auxiliar e bacias
  → runoff, neve quando habilitada e balanço lacustre
  → acúmulo, seleção de rios, entidades e caches
  → biomas, praias e efeitos fluviais autorizados
  → geometria visual, preview e save
```

Descobrir um novo lago altera água e pode afetar clima. Na primeira entrega, priorizar lagos já definidos pela morfologia. Quando houver criação hidrológica de lagos, estabelecer uma segunda passagem limitada para clima e drenagem; não criar uma dependência circular sem política de término.

Etapas de criação: `Blank` não executa esses sistemas; `Land` executa tectônica/morfologia necessárias à máscara; `Relief` acrescenta elevação; `Terrain` acrescenta clima, rios e biomas. O mesmo manifesto nas etapas progressivas deve preservar os campos anteriores.

## 7. Passo a passo de implementação

Cada fase deve terminar com resultados inspecionáveis antes de iniciar a seguinte. Os nomes de arquivos novos abaixo são propostas.

| Fase | Trabalho concreto | Entrega e condição de avanço |
|---|---|---|
| 0 — Baseline | Fixar casos v17, capturas e hashes; registrar configurações e tempos. Atualizar o planejamento que hoje diz “sem tectônica”. | Baseline reproduzível; saves antigos continuam válidos. |
| 1 — Contratos | Definir configurações tectônicas/hidrológicas, unidades, entidade River/Lake, caches e versões. Separar RNG por domínio e desempates. | Esquema documentado e leitura de manifesto antigo com defaults legados. |
| 2 — Placas | Criar `WorldGenerator.Tectonics.cs`: sementes, territórios, crosta local, adjacência, movimentos, classificação e campos limitados. | Layers de placa, crosta, vetores, limites, cristas, fossas e arcos; testes de wrap. |
| 3 — Geografia global | Alimentar scores, âncoras e relevo com tectônica; adaptar quotas e separação; manter opção tectônica desligada para comparação. | Cordilheiras coerentes, planícies amplas, continentes solicitados e orçamento de terra verificado. |
| 4 — Costas e ilhas | Operadores limitados, eventos espaçados, baías/penínsulas e arcos de ilhas independentes. | Maioria de costa limpa, nenhuma regressão de canais estreitos artificiais ou pontes entre ilhas. |
| 5 — Clima | Criar módulo de vento/umidade sobre relevo final, mantendo unidades atuais. | Experimento controlado com barreira montanhosa: maior chuva a barlavento e sombra a sotavento. |
| 6 — Drenagem mínima | Criar `WorldGenerator.Rivers.cs`: superfície auxiliar, flats, saídas, runoff anual e acúmulo; lagos terminais. | Grafo sem ciclos, destinos válidos e conservação de fluxo, com visual de debug. |
| 7 — Persistência fluvial | Entidades River tipadas, índice por hex, cache compartilhado e largura por trecho; serialização, load e undo de entidades. | Rio gerado ou editado sobrevive a salvar/reabrir e desfazer/refazer. |
| 8 — Lagos e neve | Nós lacustres com sangradouro e evaporação; opção de climatologia mensal e estoque nival. | Lago com saída repassa fluxo; lago fechado tem balanço explícito; degelo não excede reserva. |
| 9 — Visual | Protótipo modular mínimo e ribbons por chunk; comparar e consolidar o renderer. | Zoom distante/próximo, foz, confluências e emendas corretos sem perda de interação. |
| 10 — Editor e regeneração | Integrar modos, campo rios, locks, bordas da seleção, snapshot e atualização ao concluir. | Fluxo completo editar → regenerar → salvar/cancelar → undo/redo sem alteração externa indevida. |
| 11 — Calibração | Rodar matriz de seeds/tamanhos/percentuais, medir memória e tempo, revisar capturas. | Relatório de qualidade, desempenho e limitações; só então habilitar a nova geração por padrão. |

Persistência e regeneração não ficam sem projeto até as fases 7 e 10: seus contratos são definidos na fase 1 e orientam as estruturas desde o início. Essas fases representam a conclusão da integração.

**Primeiro recorte recomendado:** fases 0–4. Entregar placas visíveis em debug, montanhas orientadas por elas e ilhas costeiras/oceânicas melhores, sem misturar ainda o risco da hidrologia. O segundo recorte cobre clima e rios lógicos persistentes; o terceiro fecha lagos, neve, visual e edição completa.

## 8. Determinismo, arquivos e compatibilidade

Introduzir a próxima versão disponível do gerador, atualmente candidata a v18. Não alterar o resultado da v17. Defaults novos só valem para novos mundos; o algoritmo registrado governa a regeneração de saves antigos.

Usar seeds derivadas de seed principal + domínio estável (`tectonics`, `coast`, `islands`, `climate`, `rivers`) + ID de feição. Não usar `GetHashCode()` de strings como hash persistente nem um RNG global compartilhado entre threads. Para prometer reprodução entre runtimes, fixar o algoritmo do PRNG e seus testes.

Ordenação de candidatos, reduções paralelas, empates e concatenação de entidades devem ser estáveis. Definir se a garantia cobre uma build/runtime ou plataformas distintas; resultados de ponto flutuante exigem validação antes de prometer igualdade bit a bit universal.

Salvar parâmetros tectônicos/hidrológicos e entidades fluviais. A receita tectônica temporária é reconstruída por versão somente quando a geração precisar dela; edição e persistência de placas ficam fora do escopo. Não regenerar rios a partir da seed ao abrir: reconstruir somente caches das entidades salvas, preservando intervenções manuais.

O `.world` atual tem limite de 8 MiB para entidades esparsas. Estimar o volume de caminhos em mapas grandes e ajustar formato/limites com validação, sem aceitar arquivos ilimitados. Se o contrato ficar incompatível, incrementar `WorldSaveVersion` e implementar migração; adicionar arrays ao denso exige alteração explícita do formato. Não renumerar os enums dos métodos antigos.

## 9. Edição, locks e regeneração regional

Tectônica e hidrologia têm alcance global. Uma bacia não cabe necessariamente no halo local de biomas usado hoje. Recalcular dados auxiliares globais ou a bacia afetada pode ser necessário; **escrever fora da seleção continua proibido** na operação regional.

Na regeneração:

1. Reconstruir ou reutilizar a fundação tectônica global determinística; nunca sortear placas dentro de cada chunk.
2. Usar o mundo atual como condição de contorno, incluindo terreno editado e campos preservados pelo modo escolhido.
3. Congelar entradas/saídas e trechos externos dos rios. Uma aresta é protegida se qualquer um dos seus hexes estiver travado ou fora do domínio de escrita permitido.
4. Recalcular somente trechos internos compatíveis com essas condições; preservar IDs e links externos sempre que possível.
5. Se não existir conexão possível, manter a parte protegida e registrar término `Custom`/conflito visível, conforme a regra de edição, sem atravessar montanhas ou alterar o exterior silenciosamente.
6. Registrar campos densos e entidades afetadas numa operação única; reconstruir caches e renderizações após undo/redo.

No modo “Manter terra/água”, preservar a máscara conforme o contrato da UI; no modo “Manter terra/água + elevação”, preservar também altitude e relevo. Auditar a diferença entre o texto antigo, que menciona apenas oceano, e o código atual, que preserva o campo `Water`: adotar a preservação de todas as classes de água para não apagar lagos inesperadamente.

Durante pinceladas, atualizar apenas o draft e o preview. Ao salvar, consolidar o que a operação autorizou e recalcular caches/visuais necessários; **salvar não deve disparar uma regeneração global que desfaça pintura manual**. Alterações de altitude podem deixar rios manuais fisicamente improváveis, o que é permitido ao editor. Oferecer regeneração explícita quando o usuário quiser corrigi-los.

## 10. Desempenho e memória

**Não adotar os seis arrays globais da especificação tectônica.** O cálculo anterior de 192 MiB descrevia o custo daquela representação, não uma necessidade do projeto. A arquitetura escolhida usa placas/contatos temporários e feições compactas, aplicadas nos buffers existentes do gerador e descartadas ao terminar.

Ainda medir o pico temporário da geração: descartar no final elimina retenção, mas não o custo durante o cálculo. Evitar também duplicar buffers de scores e relevo já existentes. Nenhum dado tectônico entra no draft, histórico, GPU ou save por padrão. Uma visualização de diagnóstico, se necessária durante desenvolvimento, é transitória e sob demanda.

- Fazer estimativa de pico de memória por fase, não apenas tamanho final do mundo.
- Prototipar tectônica numa resolução auxiliar menor, amostrando campos contínuos no mapa final e verificando conectividade/limites. Comparar qualidade com resolução completa antes de adotar.
- Usar IDs compactos onde couberem, reutilizar buffers e liberar campos temporários após os consumidores terminarem.
- Não manter todas as máscaras se um campo combinado ou uma rede de feições esparsas for suficiente.
- Evitar inserir milhões de células oceânicas na fila quando a fronteira oceano–terra basta; manter o interior oceânico marcado adequadamente.
- Medir filas, ordenação, alocações e reconstrução de meshes; evitar um Node por hex ou por segmento.
- Separar cálculo de dados do upload e alterações de cena, mantendo a UI responsiva. Progressos por fase e cancelamento devem deixar o mundo anterior intacto.
- Não prometer o mesmo tempo da v17. Medir tempo total, tempo por fase, pico de memória e resposta do editor antes de estabelecer o limite de lançamento.

## 11. Matriz de validação

Automatizar pelo menos seeds 1234, 1 e uma seed negativa; acrescentar um lote maior fixo para regressão. Cobrir 30%, 40% e 50% de terra, extremos suportados de percentual/continentes, wrap ligado/desligado e mapas compactos, 1024×512 e grandes até o limite de memória validado.

| Área | Verificação objetiva |
|---|---|
| Repetibilidade | Mesma versão e parâmetros produzem os mesmos campos/entidades; ordem dos chunks não altera resultado. |
| Placas | Toda célula tem placa válida, territórios conectados, fronteiras únicas e movimentos permitidos. |
| Wrap | Territórios, ruído, vetores, ilhas, fluxo e geometria contínuos na costura. |
| Costas | Medir área de terra, massas principais, extensão afetada por eventos e espaçamento de ilhas; revisar capturas para padrões artificiais. |
| Operadores | Influência zero fora do raio; estreito aceito realmente conecta águas; quota não fecha passagem protegida. |
| Clima | Chuva não negativa, unidades corretas e resposta a uma cordilheira artificial sob vento conhecido. |
| Drenagem | Sem ciclos; flats e mínimos resolvidos; teste sem oceano; nenhum destino inválido não explicado. |
| Lagos | Fluxo recebido/repassado correto, sangradouro único quando aplicável, evaporação/armazenamento contabilizados. |
| Neve | Estoque não negativo; água liberada limitada à entrada e reserva; cenário sem precipitação não cria água. |
| Rede | Arestas espelhadas, direção coerente, afluentes sem duplicação de fluxo e largura por trecho reconstruível. |
| Save e histórico | Round-trip de entidades e caches; exclusão/redução de rios; undo/redo atravessando quatro chunks. |
| Edição regional | Hash de campos externos preservado, entidades externas intactas, locks dos dois lados e términos Custom quando necessários. |
| Visual | Meandros dentro do corredor, emendas sem fendas, mesma largura/UV e ausência de linhas longas atravessando o mapa no wrap. |
| Desempenho | Tempo por fase, memória máxima, quantidade de vértices/draw calls e tempo de frame durante edição. |

Critérios de beleza não são demonstrados só por testes de contagem. Produzir pranchas comparativas da mesma seed com tectônica desligada/ligada, layers de diagnóstico e aproximações de ilhas, costas, cordilheiras e confluências.

## 12. O que usar, adaptar ou adiar

| Proposta | Parecer |
|---|---|
| Tectônica antes da geografia | Usar como fundação procedural estática. |
| Uma placa = uma crosta = um continente | Substituir por movimento da placa + crosta local + massas emergentes. |
| Influência de 3–5 vizinhas | Usar como controle limitado; considerar todas as fronteiras reais na geologia. |
| Movimentos em direções adjacentes | Usar, monitorando repetição angular e viés da grade. |
| Campos de crista/rift/arco | Usar, acrescentando lado de subducção, fossa e escalas explícitas. |
| Operadores Hill/Range/Pit/Trough/Strait | Usar o conceito; substituir os exemplos de propagação por campos limitados. |
| Percentil global como solução de tudo | Não adotar sobre a máscara atual; preservar quotas conectadas e reservas. |
| Arcos de ilhas separados | Usar; não formar ilhas cortando uma massa única em canais finos. |
| Priority-Flood e fluxo acumulado | Usar com saídas, flats, lagos, ordem determinística e conservação definidos. |
| Degelo multiplicador + constante | Não usar como hidrologia; trocar por água disponível/estoque. |
| RiverEdges como única fonte | Não usar; entidades persistentes são a fonte, arestas são cache. |
| Ribbons por chunk | Prototipar como opção principal, comparando continuidade e desempenho. |
| Textura global 4× para todo mapa | Evitar no mapa máximo; considerar tiles/LOD se necessária. |
| Erosão automática posterior aos rios | Adiar; requer recálculo controlado e proteção de edição. |
| Deltas, geleiras dinâmicas e evolução tectônica temporal | Extensões posteriores com contratos próprios. |

## 13. Referências e escopo da revisão

Documentos locais revisados:

- [Placas tectônicas](Especificacao_Sistema_Placas_Tectonicas.md).
- [Ilhas, baías e penínsulas](Especificacao_Ilhas_Bahias_Peninsulas_Estilo_Azgaar.md).
- [Rios, chuva, degelo e shader](Especificacao_Sistema_Rios_Chuva_Degelo_Shader.md).
- Especificação do protótipo v0.5.1, especialmente contratos de redes, caches, edição regional, persistência e M5.

As avaliações de compatibilidade foram feitas contra o código local listado na seção 2. As fórmulas e limites de memória foram analisados diretamente; não são benchmarks executados nesta revisão. As decisões de arquitetura e os parâmetros de calibração são recomendações para este projeto.

O [repositório oficial do Azgaar](https://github.com/Azgaar/Fantasy-Map-Generator) é referência para uma implementação futura. Esta revisão não certifica os trechos dos MDs como cópias fiéis do Azgaar nem verifica equivalência com versões específicas dos arquivos citados. O algoritmo local continua sendo uma adaptação própria.
