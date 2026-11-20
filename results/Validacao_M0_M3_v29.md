# Validação dos gates M0–M3 com o gerador v29

Data: 19/11/2026. Execução local no Godot 4.7.2 .NET, build Debug, OpenGL Compatibility, NVIDIA GeForce RTX 5070. Nenhuma alteração na receita do gerador foi necessária. Os testes e o benchmark foram atualizados no projeto; este trabalho não reexporta nem substitui o EXE já publicado.

## Resultado

As três pendências da auditoria passaram: benchmark atual com tectônica e rios, regeneração regional comparada à geração original e fluxo integrado de edição na v29. Os gates obrigatórios de M0–M3 ficam aprovados para os casos abaixo. O alvo ideal de troca de camada abaixo de 16 ms ainda não foi alcançado em todas as camadas; o limite obrigatório é abaixo de 100 ms.

## M0 — desempenho e memória

O benchmark agora usa `WorldGenerator.Version`, em vez de herdar a versão 17 dos fixtures antigos. Rodou sem as suítes regionais ou de edição concorrendo por CPU/GPU.

Configuração: 1024×512, seed 1234, quatro continentes, 50% de terra, detalhe costeiro 50, sem wrap, geração completa e 3.618 trechos fluviais. Medidas de uma execução, sem VSync, com 240 frames de pan/zoom e 60 amostras de pincel ativo.

| Medida | Resultado | Gate |
|---|---:|---|
| Geração dos dados | 7,651 s | < 60 s: passou |
| Geração + preparação da primeira imagem | 8,623 s | Informativo |
| Pan/zoom | 1.464,9 FPS médios | ≥ 30 FPS: passou |
| Troca de camada mais lenta, incluindo submissão do frame | 19,952 ms | < 100 ms: passou; alvo < 16 ms pendente |
| Upload integral das texturas, incluindo submissão do frame | 1,742 ms | Informativo |
| Edição ativa, mediana | 2,052 ms/frame | Informativo |
| Edição ativa, p95 | 2,665 ms/frame | < 33,333 ms: passou |
| Memória gerenciada retida pelo mapa após coleta | 19,28 MiB | < 200 MiB: passou |
| Memória gerenciada total durante edição após coleta | 33,80 MiB | Informativo |
| Working set total do processo durante edição | aproximadamente 546 MiB | Inclui engine/renderização; não é a memória de dados do mapa |

A memória do mapa foi estimada pela diferença de `GC.GetTotalMemory(true)` antes e depois da geração. É memória gerenciada retida, não o pico de memória temporária nem a VRAM. Os tempos são medições deste computador, não garantia para outros equipamentos.

Artefatos: `.godot/v29-benchmark-validation.log`, `results/validation-v29/benchmark.json` e `results/validation-v29/editor-performance.json`. Capturas do benchmark estão em `.godot/benchmark-sculpt*.png` e `.godot/benchmark-editor-panel.png`.

## M2 — costuras e regeneração regional

**384 comparações aprovadas**, em 16 configurações:

- 128×64: seeds 1234, 1 e −77; 30% e 50% de terra; wrap ligado/desligado.
- 193×95: seed 1234, 38% de terra, wrap ligado/desligado; inclui chunks parciais.
- 512×256: seed 1234, 38% de terra, wrap ligado/desligado.
- Quatro regiões por configuração: cruzamento de quatro chunks, canto inicial, canto final e região central.
- Seis campos regenerados isoladamente: água, elevação, relevo, temperatura, chuva e bioma.

Cada caso gera um mundo de referência, salva e reabre o arquivo para descartar a calibração de nível do mar em cache. Em uma cópia, o campo escolhido é deliberadamente danificado dentro da seleção e depois regenerado. O teste exige igualdade exata desse campo com a geração original, preservação dos demais campos e zero alteração densa fora da seleção. A reconstrução do flag costeiro derivado é permitida apenas ao regenerar água. As redes fluviais são verificadas separadamente no fluxo do editor, pois possuem contratos de preservação de bordas e entidades.

Artefatos: `.godot/v29-regions-validation.log` e `results/validation-v29/regions.json`.

## M3 — editor e regeneração

O fluxo integrado `VerifyEditing(..., 29)` passou com renderização real. Exercita pintura, cinco ferramentas de elevação, formas/força, seleção cruzando chunks, inspeção individual, locks, visual de locks por pixels, undo/redo, regeneração, salvar, salvar como, cancelar rascunho e reabrir.

Também passaram **20 operações de regeneração pelo fluxo do editor**:

- Os três modos: tudo; preservar água; preservar água, elevação e relevo.
- Os sete campos isolados: água, elevação, relevo, temperatura, chuva, bioma e rios.
- Cada operação com wrap ligado e desligado.
- Um hex com lock integral dentro da seleção; comparação de todos os campos densos fora da seleção; preservação dos campos não escolhidos.
- Undo/redo das operações que produziram alterações, incluindo entidades esparsas, e save/load de cada resultado.
- A regeneração isolada de rios seleciona uma região contendo um rio real. Após cada operação, os rios são verificados quanto a ciclos no caminho, saltos entre hexes e âncoras compartilhadas nas confluências.

O smoke geral agora inclui explicitamente o editor com `WorldGenerator.Version`, além dos fixtures legados 17 e 18. A execução dedicada v29 é a evidência deste fechamento; não foi necessário repetir toda a matriz histórica após alterações restritas aos testes.

Artefatos: `.godot/v29-editor-validation-final.log` e `results/validation-v29/editor.json`.

## Reproduzir

```powershell
dotnet build Mapmaker.csproj --no-restore
.\Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . --log-file .godot/v29-regions-validation.log -- --validate-regions-v29
.\Godot_v4.7.2-stable_mono_win64_console.exe --path . --log-file .godot/v29-editor-validation-final.log -- --validate-editor-v29
.\Godot_v4.7.2-stable_mono_win64_console.exe --path . --log-file .godot/v29-benchmark-validation.log -- --benchmark --continental-sculpt
```

Executar o benchmark depois que as outras suítes terminarem. Build final: zero avisos e zero erros. As suítes dedicadas e o benchmark encerraram com código 0.
