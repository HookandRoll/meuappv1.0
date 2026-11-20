# Desempenho de todos os presets — gerador v29

Data: 19/11/2026. Todos os seis tamanhos da interface foram executados com renderização real, um processo novo por tamanho e sem benchmarks simultâneos.

Configuração comum: seed 1234, escultura continental, três continentes, 38% de terra, detalhe costeiro 50, geração completa com clima e rios, sem wrap. Godot 4.7.2 .NET, build Debug, renderer OpenGL Compatibility, NVIDIA GeForce RTX 5070, janela 1280×800 e VSync desligado.

Uma geração medida por tamanho, após aquecimento com um mapa 128×64 descartado antes da medição de memória. Os resultados abaixo são da série final: a primeira série foi interrompida para corrigir uma referência ao mundo de aquecimento que distorcia a memória incremental dos presets pequenos. A correção foi apenas no benchmark.

## Geração, renderização e edição

| Tamanho | Hexes | Geração dos dados | Dados + renderer | FPS visão geral | FPS aproximado | Edição p95 | Dados do mapa |
|---|---:|---:|---:|---:|---:|---:|---:|
| 128×64 | 8.192 | 0,167 s | 0,559 s | 1.967,7 | 2.011,7 | 1,41 ms | 0,16 MiB |
| 256×128 | 32.768 | 0,564 s | 0,979 s | 1.825,1 | 1.777,1 | 1,15 ms | 0,91 MiB |
| 512×256 | 131.072 | 1,550 s | 2,029 s | 1.429,4 | 1.610,4 | 1,17 ms | 4,56 MiB |
| 1024×512 | 524.288 | 6,374 s | 7,228 s | 809,1 | 1.624,1 | 2,03 ms | 18,30 MiB |
| 2048×1024 | 2.097.152 | 25,860 s | 28,010 s | 327,7 | 1.564,3 | 3,84 ms | 61,47 MiB |
| 4096×2048 | 8.388.608 | 106,470 s | 114,513 s | 95,4 | 1.399,9 | 12,11 ms | 222,37 MiB |

- FPS: média de 180 frames de pan/zoom em cada enquadramento. Visão geral mantém aproximadamente o mapa inteiro na tela; aproximado usa zoom entre 3,05 e 3,95. São testes de renderização sem VSync, não FPS mínimos nem limite visível do monitor.
- Edição: 60 amostras com pincel de elevação de raio 6, arrasto real e upload da prévia, até a submissão do frame. P95 significa que 95% das amostras ficaram abaixo desse tempo. O teste exige uma pincelada que entre no histórico.
- Dados + renderer soma geração e preparação dos buffers/texturas até dois frames após instalar o mundo. Não inclui a inicialização do processo, aquecimento ou coleta de memória feita pelo benchmark.
- Dados do mapa: diferença da memória gerenciada retida após coleta, antes/depois de gerar. Não representa a memória total do processo, o pico temporário de geração ou a VRAM.

## Camadas, upload e abertura do editor

| Tamanho | Pior troca de camada | Upload integral | Abrir editor |
|---|---:|---:|---:|
| 128×64 | 14,32 ms | 0,63 ms | 0,023 s |
| 256×128 | 13,76 ms | 0,75 ms | 0,023 s |
| 512×256 | 20,94 ms | 1,11 ms | 0,026 s |
| 1024×512 | 28,25 ms | 1,82 ms | 0,048 s |
| 2048×1024 | 34,66 ms | 5,63 ms | 0,124 s |
| 4096×2048 | 54,59 ms | 19,81 ms | 0,387 s |

Troca de camada: maior tempo de três passagens por todas as sete camadas, incluindo submissão do frame. O upload integral também inclui submissão. Abrir editor inclui a cópia de trabalho, prévia e dois frames.

## Salvar e reabrir

| Tamanho | Salvar | Abrir + verificar | Trechos fluviais |
|---|---:|---:|---:|
| 128×64 | 0,049 s | 0,032 s | 20 |
| 256×128 | 0,074 s | 0,041 s | 162 |
| 512×256 | 0,069 s | 0,080 s | 900 |
| 1024×512 | 0,095 s | 0,173 s | 3.150 |
| 2048×1024 | 0,217 s | 0,536 s | 7.559 |
| 4096×2048 | 0,604 s | 1,964 s | 18.302 |

Os arquivos são temporários, sem preview PNG. A comparação após abrir inclui todos os arrays densos e o JSON de entidades esparsas. Todos os seis round-trips passaram. O tempo de abertura inclui essa comparação, não apenas leitura/descompressão; os números não incluem a interface de progresso de salvamento nem garantem o mesmo desempenho em outro disco ou com cache frio.

## Interpretação

- Todos os tamanhos ficaram acima de 60 FPS médios na visão geral nesta máquina. O menor resultado foi 95,4 FPS no mapa máximo.
- Todos ficaram abaixo de 33,33 ms no p95 de edição e de 100 ms na pior troca de camada. O alvo ideal de camada abaixo de 16 ms foi atingido apenas nos dois presets menores.
- O tamanho de referência dos gates, 1024×512, passou em geração (<60 s), dados do mapa (<200 MiB), navegação (≥30 FPS), camadas (<100 ms) e save/load (<5 s).
- No mapa máximo, o maior custo é gerar: aproximadamente 1 min 46 s, mais 8 s de preparação do renderer. Seu mapa retém cerca de 222 MiB de dados gerenciados; o pico de working set total do processo observado foi aproximadamente 1,85 GiB. O limite de dados de 200 MiB foi especificado para 1024×512, não para 4096×2048.
- Nenhuma alteração na geração foi feita. Estes são resultados de uma execução por preset, neste equipamento e nesta build; não são médias de múltiplas seeds ou garantia para outros computadores.

## Evidências e reprodução

- Métricas completas, incluindo memória total durante edição, p95 de navegação, tempos por camada e tamanho dos saves: `results/performance-all-sizes-v29/<largura>x<altura>.json`.
- Logs da série final: `.godot/performance-v29-128.log`, `-256.log`, `-512.log`, `-1024.log`, `-2048.log` e `-4096.log`.
- Os seis processos da série final encerraram com código 0; build sem erros ou avisos.

```powershell
dotnet build Mapmaker.csproj --no-restore
foreach ($mapWidth in @(128, 256, 512, 1024, 2048, 4096)) {
    & .\Godot_v4.7.2-stable_mono_win64_console.exe --path . --log-file ".godot/performance-v29-$mapWidth.log" -- "--benchmark-size=$mapWidth"
    if ($LASTEXITCODE -ne 0) { throw "Benchmark falhou em $mapWidth" }
}
```
