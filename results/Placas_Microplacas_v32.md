# Placas grandes e microplacas — gerador v32

As fronteiras combinam as curvas amplas da v31 com duas escalas de ruído de interpolação linear. A malha auxiliar passa a ter de 64 × 32 a 192 × 96 amostras, permitindo representar quinas e trechos curtos mais recortados.

A distribuição usa placas grandes continentais e oceânicas, com microplacas posicionadas perto dos encontros entre as grandes. O campo de distâncias considera o tamanho relativo de cada placa. O tamanho também altera o alcance da influência da crosta no posicionamento continental e no relevo. O movimento relativo é projetado na normal de cada segmento de fronteira, fazendo a compressão variar com seu contorno.

Nas microplacas, os cinturões de relevo ficam mais estreitos, com intensidade e altura variando espacialmente pela seed. A contribuição de elevação considera o comprimento dos segmentos para limitar o acúmulo causado pela malha mais detalhada.

O tamanho é salvo em `tectonics.json`; referências antigas sem esse campo recebem tamanho 1. Mundos anteriores à v32 mantêm a receita de geração correspondente à sua versão.

## Verificação

- Build principal sem erros ou avisos.
- Três seeds, com e sem wrap: presença de placas grandes continentais e microplacas com fronteiras reais; mudanças de direção nos contatos; serialização preservando os dados.
- Referências tectônicas nos limites de 32 × 32 e 4096 × 2048 validadas.
- Geração em 256 × 128 e 1024 × 512: determinismo, salvar/abrir, regeneração regional e locks passaram, com e sem wrap.
- Comparação mantendo as mesmas fronteiras e neutralizando os tamanhos confirmou que as microplacas alteram o terreno efetivo.
- Biomas e proteções introduzidos na v31 continuam verificados.

Comandos: `dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj` e o mesmo comando com `-- --large`.
- Bateria completa `--smoke-test`: passou, incluindo as versões 31 e 32 na matriz de geração, 524.288 hexes, salvar/abrir, emendas regionais e edição M3.
