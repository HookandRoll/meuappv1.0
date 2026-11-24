# Build experimental — Hadley principal

EXE: `Jota-Fantasy-Map-Maker-Alpha-2.0.1-Experimental-Hadley-No-Bands.exe` (206,108,304 bytes).

Novo mapa → Terreno completo usa Hadley acoplado como clima principal, sem os três cinturões prescritos de pressão e sem a penalidade subtropical de chuva do clima antigo. Ventos usa o mesmo campo da simulação. A conversão da chuva é fixa em 10,5 e os mapas são salvos com receita v64.

Validação: criação normal idêntica ao experimento de referência, seeds 1234 e −77, fronteiras fechadas e periódicas, regeneração completa e regional, seleção e locks. 48 mundos legados idênticos. No EXE portátil isolado, foram verificados formulário, chuva/biomas acoplados, renderer de ventos, save/reabertura e igualdade dos hashes do vento recalculado.

SHA-256: `cb4d475f5e3b3af68acfe33738c800f8dff99aec60d21ab37da6c99dcd7810d7`.

A versão Alpha 2.0 anterior permanece separada. Mapas antigos mantêm o clima salvo; crie um mapa novo para testar. O gradiente térmico equador–polos continua no modelo, que permanece parametrizado e experimental.
