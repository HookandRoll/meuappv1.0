# Validação Alpha 2.2

Data: 24/11/2026. EXE portátil Windows x64: `Jota-Fantasy-Map-Maker-Alpha-2.2-World-Geography.exe`. Receita de geração v73 e formato de save preservados.

## Build e compatibilidade

- Projeto principal compilado sem erros, com aviso NU1900 de consulta de vulnerabilidades do NuGet indisponível no ambiente restrito.
- Exportação Release com runtime .NET e PCK incorporados. Identificação atualizada para Alpha 2.2 - World Geography.
- Testes do núcleo de redes aprovados: arestas, chunks, tiers, larguras, locks, seleção, cortes, afluentes, wrap, histórico e save/load, incluindo edição de uma rede procedural v73.
- Os 48 mundos legados de referência continuam idênticos bit a bit, incluindo campos densos, rios, regiões, ícones e texturas.

## Fluxos no EXE portátil

- `--network-review`: aprovado. Estradas dos três tiers, desenho interpolado de rio, prévia, undo/redo, exclusão parcial de estrada e rio, salvar/reabrir e cancelar rascunho. Capturas com e sem grade inspecionadas.
- `--hadley-main-review`: aprovado, código 0. Criação pela interface com generator=73, dez seletores, umidade finita em 0–100%, save/load exato e reconstrução idêntica dos campos de vento e umidade.
- `--benchmark-size=1024 --release-validation --vegetation-zoom-review`: aprovado, código 0, com os ajustes de árvores e montanhas presentes no pacote final.
- Formulário exportado inspecionado: método de geração apresentado como Escultura continental. Busca textual no EXE de 206.169.040 bytes retornou zero ocorrências do nome de referência removido, tanto em ASCII quanto em UTF-16LE.

## Desempenho do pacote final

Uma execução local, Região 1024×512, 524.288 hexes, seed 1234, três continentes e 38% de terra. Godot 4.7.2 .NET, Release, OpenGL Compatibility, RTX 5070, janela 1280×800 e VSync desligado. FPS médio em 180 frames de movimento por enquadramento.

| Medida | Resultado |
|---|---:|
| Visão geral | 139,9 FPS |
| Zoom intermediário | 141,2 FPS |
| Zoom máximo | 275,3 FPS |
| Zoom máximo, p95 | 6,71 ms/frame |
| Pior troca de camada | 39,94 ms |
| Edição, p95 | 8,52 ms/frame |
| Geração | 13,93 s |
| Preparação do renderer | 2,25 s |
| Salvar | 0,199 s |
| Abrir e verificar igualdade | 0,129 s |
| Memória gerenciada retida pelo mapa | 21,35 MiB |

Os limites obrigatórios de navegação, camadas, edição, geração, abertura e memória para 1024×512 foram atendidos. O alvo ideal de troca de camada abaixo de 16 ms não é atendido por todas as camadas. Não foi repetida a matriz de todos os tamanhos nem uma nova validação do Atlas máximo. Os números não representam garantia para outras máquinas.

As montanhas maiores à distância exigem um alcance maior de amostragem no shader e têm custo em relação ao visual anterior. A comparação por zoom está em `Montanhas_Zoom_Metade_Vulcao.md`; o benchmark acima é da versão final com essa mudança ativa.

## Evidências

- Logs: `.godot/alpha22-network-exe.log`, `.godot/alpha22-main-exe.log`, `.godot/alpha22-performance-exe.log`.
- Capturas de redes: `dist/results/m5-networks/`.
- Capturas de criação, terreno, chuva, biomas, vento e umidade: `dist/.godot/`.
- Métricas e capturas do benchmark exportado: `dist/results/alpha22-performance/`.
- Comparações das montanhas: `results/mountain-zoom-half-volcano/`.

O pacote público de validação reúne cópias dos relatórios, métricas, logs e capturas correspondentes. Os checksums SHA-256 acompanham os downloads da release.

## Escopo encerrado

M4 e M5 concluídos no escopo ajustado pelo usuário. O gerador geográfico é considerado completo; a conexão automática de pontos por estradas fica para uma etapa futura. O fallback regional que conserva o trecho fluvial original quando a busca falha permanece documentado em `M5_Fechamento.md`, sem alterar a geografia nesta publicação.
