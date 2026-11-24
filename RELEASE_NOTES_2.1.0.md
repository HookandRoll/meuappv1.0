# Alpha 2.1 - Atmospheric Currents and Humidity

Jota Fantasy Map Maker para Windows x64, com runtime .NET e recursos incorporados no EXE portátil.

- O clima acoplado passa ao principal: mapas novos usam a receita v73, com Hadley, Ferrel e Polar nas camadas baixa e alta, transporte de vapor e nuvens, chuva e reciclagem pelo solo.
- Umidade recebe uma camada própria, baseada na umidade relativa média do ar próximo ao solo. Gradiente fixo de vermelho (0%) a claro (50%) e azul (100%), com porcentagem na inspeção. Chuvas continua mostrando precipitação anual em mm.
- Seletores reorganizados: Terreno, Biomas, Elevação, Temperatura, Umidade, Chuvas, Regional, Tectônicas, Ventos Baixos e Ventos Altos. Locks permanece disponível no editor.
- Chuva final com pesos de 50% no centro, 15% em cada um dos seis vizinhos e 5% em cada um dos doze hexes do segundo anel. O espalhamento respeita subidas de até 500 m e caminhos pelo terreno, sem saltar cristas.
- Deserto e badlands ficam abaixo de 150 mm/ano. Arbustal aceita a faixa-base de 150 até menos de 680 mm. Pradaria fica abaixo de 20 °C e savana a partir de 20 °C, respeitando as demais regras e a prioridade de florestas e habitats.
- Capacidade nominal de vapor 40 a 25 °C por unidade de massa de ar, com aporte proporcional. Intensidades Hadley/Ferrel/Polar em 1,0/0,8/0,6.

[Como funcionam as correntes e a umidade](https://github.com/HookandRoll/meuappv1.0/blob/v2.1.0-alpha/docs/Correntes_Atmosfericas.md).

## Compatibilidade

Arquivos anteriores mantêm sua receita e seus dados; crie um mapa novo para usar a v73. O formato dos saves permanece o mesmo. A receita experimental v68, retirada antes desta publicação, continua exigindo sua build histórica. Vento e umidade são reconstruídos em segundo plano ao reabrir mapas acoplados.

## Downloads

- [EXE portátil Windows x64](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.1.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.1-Atmospheric-Currents-and-Humidity.exe)
- [Relatórios e capturas de validação](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.1.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.1-Validation.zip)
- [Checksums SHA-256](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.1.0-alpha/SHA256SUMS.txt)

## Validação

Compilação sem avisos ou erros, teste da receita v73 e regressão v72 aprovados. Os 48 mundos legados de referência permanecem idênticos bit a bit. O EXE portátil passou por criação, dez seletores, camada de umidade, save/load e reconstrução exata dos campos de vento e umidade. [Relatório](https://github.com/HookandRoll/meuappv1.0/blob/v2.1.0-alpha/results/Validacao_Alpha_2.1.md).
