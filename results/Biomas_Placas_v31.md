# Biomas regionais e placas onduladas — gerador v31

A classificação por clima permanece como base. Depois do crescimento de florestas e da correção de faixas, uma busca por componentes conectados identifica regiões com até `clamp(altura / 32, 6, 32)` hexes. Uma região pequena pode adotar o bioma de uma região maior vizinha, pela quantidade de arestas compartilhadas, desde que todos os seus hexes aceitem o clima desse bioma com uma tolerância nas transições.

Praias, áreas alpinas, água e pequenas regiões sem um vizinho maior compatível permanecem. O ajuste fluvial acontece depois dessa limpeza, preservando habitats locais associados a rios e lagos. O wrap é considerado na conectividade. Regenerações usam uma margem adicional de duas vezes o limite de tamanho para reconhecer componentes e seus vizinhos; locks continuam sendo respeitados na cópia da seleção.

As fronteiras tectônicas recebem ruído contínuo em duas escalas, determinado pela seed e proporcional ao espaçamento entre placas. A deformação atua no campo que define as fronteiras, usado pelo relevo e pela camada tectônica. O cálculo da deformação é compartilhado entre as comparações de distância de uma mesma amostra.

As mudanças são ativadas a partir da versão 31 do gerador. Arquivos anteriores preservam seu comportamento de geração.

## Verificação

- `dotnet build Mapmaker.csproj --no-restore`: sem erros ou avisos.
- `dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj`: passou; componentes de um e dois hexes, clima incompatível, praias, alpino, água, conectividade no wrap, determinismo, fronteiras tectônicas, regeneração regional e locks.
- `Godot_v4.7.2-stable_mono_win64_console.exe --headless --path . -- --smoke-test`: passou; 524.288 hexes, salvar/abrir, emendas regionais, escultura continental e edição.

A avaliação visual dos contornos e do tamanho desejado para as regiões pode orientar ajustes posteriores de amplitude do ruído e limite de consolidação.
`dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj -- --large` também passou em 1024 × 512, com e sem wrap, incluindo reprodução da seed, regeneração regional e locks.
