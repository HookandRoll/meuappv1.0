# Correções e performance do gerador v63

## Implementação

1. Regiões usam lookup por ID em tempo constante. Renomear e substituir o catálogo atualizam o cache. A escolha da âncora tem fallback, e cancelar o editor invalida a textura e redesenha os rótulos.
2. Salvar reutiliza a geografia válida, sem recalcular `SurfaceHash`. Alterações diretas nos arrays de água/elevação devem chamar `InvalidateGeography()`.
3. Capturas acima de metade do mapa usam snapshots de chunks comprimidos com Deflate, preservando todos os campos. O orçamento de 128 MiB usa o tamanho efetivo dos snapshots e pode remover até um comando único que ultrapasse esse limite. Cancelamento e undo/redo continuam restaurando os campos e as entidades esparsas.
4. A busca de desvio de rios tem limite de expansões proporcional à seleção. Ao abortar, preserva caminho, classes e vazões do trecho original. Pinceladas de água atualizam a costa dos seis vizinhos, com captura para undo e respeito aos locks de flags.
5. Atlas do gerador v37 em diante rejeita wrap horizontal. Os testes de Atlas usam manifestos válidos; mapas regionais e versões legadas mantêm cobertura de wrap.
6. Os quatro passes de vento executam em paralelo. Cada um guarda chuva e retornos individuais de vapor; a redução reproduz a ordem original das adições de float.
7. A altura auxiliar de drenagem é calculada uma vez, em paralelo. Células fora do domínio são marcadas como visitadas. A chave de prioridade já continha altura, distância, desempate e célula, e foi preservada.
8. Contatos tectônicos são filtrados por limites verticais e aplicados em faixas exclusivas de 32 linhas. A ordem dos contatos em cada célula permanece igual.
9. Ilhas usam Top-K em lotes, com memória limitada para a fila e um bitset de elegibilidade. Se os candidatos de um lote forem rejeitados, a busca continua abaixo do último rank, preservando o resultado da ordenação completa.
10. A geração inicial constrói a geografia durante o clima e reutiliza o catálogo. Regenerações regionais continuam classificando sua superfície temporária sem substituir o cache do mundo original.
11. Texturas e símbolos de relevo são calculados por linhas. Cada célula oceânica escreve sua própria informação costeira. A busca de posições de ícones é paralela; a escolha final mantém a dependência dos ícones à esquerda/acima.

## Preservação numérica

Mantivemos vapor positivo abaixo de `1e-6` e arcos positivos abaixo de `1e-5`: descartar esses valores pode alterar resultados arredondados ou a escolha de candidatos. O ruído de arco só é omitido quando o arco é exatamente zero. Também evitamos somar totais de vapor por passe, pois isso altera o agrupamento das operações de float.

`tests/CompatibilityChecks/reference.json` contém hashes SHA-256 produzidos pelo código anterior às alterações. A comparação cobre 48 mundos, versões 34, 53, 56, 57, 59, 60 e 63, seeds 1234 e -77, regiões com/sem wrap e Atlas. Inclui presets de referência 128 e 512, com Atlas ampliado no preset maior. Os hashes incluem todos os arrays densos, entidades esparsas/rios, IDs e registros de regiões, ícones e todos os buffers visuais.

## Validações

- `dotnet build Mapmaker.csproj --no-restore`: aprovado, zero avisos e erros.
- `dotnet run --project tests/GeneratorChecks`: aprovado na versão final, incluindo os novos checks de regressão. Execuções das etapas 5 a 11 também concluíram com sucesso.
- `dotnet run --project tests/CompatibilityChecks`: 48 mundos idênticos, bit a bit, à referência anterior.
- `--verify-editor-bugfixes` no Godot: aprovado para apagar continente, alternar camadas, cancelar, restaurar textura/rótulos, atualizar costa adjacente, respeitar locks e desfazer.
- `--memory-atlas-only`: duas operações com snapshots em 6144×3072, undo/redo aprovados. Pico de working set de 714,03 MiB; memória gerenciada após a segunda operação de 602,72 MiB. Esse teste usa dados sintéticos para isolar o histórico.
- O teste de regressão também verifica cancelamento de snapshot comprimido e descarte de um comando bruto que sozinho exceda 128 MiB.

## Regenerações completas no Atlas máximo

`dotnet run --project tests/GeneratorChecks -- --regenerate-atlas-memory` concluiu duas regenerações completas consecutivas, com `UndoManager.CaptureFullWorld()` e `Commit()`, no Atlas de 6144×2784 e 15.044.776 hexes úteis. A geração inicial levou 165,51 s.

| Execução | Tempo de regeneração | Memória gerenciada após coleta | Pico acumulado de working set |
| --- | ---: | ---: | ---: |
| Primeira | 169,02 s | 591,61 MiB | 3993,85 MiB |
| Segunda | 164,16 s | 601,90 MiB | 4476,48 MiB |

Ambas concluíram sem exceções. Este teste mede o núcleo do gerador e o histórico; o benchmark com Godot abaixo inclui os buffers visuais e a abertura do mapa. O limite de 128 MiB se aplica aos snapshots retidos pelo histórico, não à memória total do gerador, às capturas pendentes ou aos buffers temporários.

## Benchmark de geração e abertura

Comando: `godot --path . -- --benchmark-size=4096 --release-atlas`. Atlas real de 6144×2784, 15.044.776 hexes úteis, seed 1234, gerador v63, terra 25%, detalhe costeiro 80%. Godot 4.7.2 .NET, renderer Compatibility, NVIDIA GeForce RTX 5070.

| Medida | Antes | Depois |
| --- | ---: | ---: |
| Geração | 264,890 s | 169,759 s |
| Preparar renderer e abrir | 42,134 s | 10,018 s |
| Total | 307,024 s | 179,777 s |
| Pico de working set do processo | 4.243.001.344 bytes | 4.324.368.384 bytes |

A execução final levou 41,4% menos tempo no total. A geração caiu 35,9% e a abertura caiu 76,2%. O pico total de memória subiu 1,9%; os buffers locais do clima e o cache antecipado de geografia têm custo de memória.

São medições locais de uma execução por versão, sem repetição estatística. A referência inicial coincidiu com a suíte original de checks; o benchmark final iniciou depois do encerramento dos checks. Os tempos não representam garantia de ganho em outros computadores. Os logs do Godot registraram restrições do ambiente para gravar `user://logs` e ler certificados, mas ambos os benchmarks concluíram e produziram os resultados e capturas.

## Revisão de texto e interface

A [revisão antislop](../anti-slop/audit-001-2026-11-22.md) não encontrou ajustes adicionais no escopo alterado. Os comandos e o contrato de invalidação do cache estão documentados no README.
