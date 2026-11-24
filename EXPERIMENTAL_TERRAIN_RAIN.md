# Alpha 2.0.10 — chuva, relevo e biomas secos

Novos mapas usam a receita v73. Saves anteriores mantêm suas receitas, incluindo a v72 sem bloqueio de altitude. O EXE anterior permanece em `dist`.

## Espalhamento da chuva

Os pesos permanecem 50% no centro, 15% em cada um dos seis vizinhos e 5% em cada um dos doze hexes do segundo anel. O cálculo lê apenas o campo-base imutável.

Uma contribuição só pode subir até 500 m em relação ao hex de origem. Exatamente 500 m é permitido; 501 m é bloqueado. Descidas não têm limite. Para o segundo anel deve existir pelo menos um caminho de dois passos por um hex ativo do primeiro anel. O intermediário também precisa estar até 500 m acima da origem, e cada subida entre hexes precisa respeitar 500 m. Assim, a chuva não salta uma crista bloqueada para alcançar o vale seguinte. Se houver dois caminhos, basta um deles estar aberto; a contribuição é contada uma única vez.

O bloqueio vale para todas as superfícies e tamanhos, usando a elevação armazenada. Pesos bloqueados ou fora do domínio são descartados, sem redistribuição. Centro, teto, offset de chuva, capacidade nominal de vapor 40 e aporte atmosférico permanecem como na v72. O espalhamento segue sendo um bônus da chuva final, separado do balanço de água da simulação atmosférica.

## Biomas

- Deserto e badlands exigem chuva anual estritamente menor que 150 mm. Regras de suavização e uma correção final também respeitam esse limite. Badlands conserva os requisitos de relevo erodido e formação de manchas.
- O arbustal passa de 360–680 para 150–680 mm na classificação-base, com o teto exclusivo. Conserva temperatura de 12 até menos de 22,5 °C e a máscara regional; parte da faixa ainda pode virar estepe.
- Pradaria (`Grassland`, plains) fica abaixo de 20 °C; savana fica a partir de 20 °C. A divisão é aplicada à classificação, suavização e correção final com a temperatura do próprio hex. Florestas e os demais biomas mantêm prioridade quando seus requisitos são atendidos.
- Rios, habitats especiais e as regras polares continuam ativos.

## Validação

```powershell
dotnet run -c Release --project tests/GeneratorChecks -- --terrain-rain-only
dotnet run -c Release --project tests/GeneratorChecks -- --balanced-rain-only
dotnet run -c Release --project tests/GeneratorChecks -- --hex-rain-spread-only
dotnet build MapmakerTerrainRain.csproj -p:NuGetAudit=false
```

O teste v73 cobre os limites 500/501 m, cristas, teto relativo à origem, descidas, caminhos alternativos, wrap, descarte dos pesos bloqueados, preservação da v72, fronteira de 150 mm, expansão do arbustal, fronteira de 20 °C e preservação das florestas. Também gera mapas com as seeds 1155827384 e -77 e verifica save/load e regeneração integral.

Executável: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.10-Experimental-Chuva-Relevo.exe`. Crie um mapa novo para experimentar a receita v73.

Validação concluída: testes v73, regressões v71/v72 e compilação passaram. O autoteste do EXE terminou com código 0, confirmou `generator=73`, criação pela interface, camadas climáticas, save/load exato e reconstrução dos ventos. `results/native-terrain-rain-v73.stderr.log` ficou vazio. Esta build usa projeto/assembly `MapmakerTerrainRain`, com cache .NET separado da Alpha 2.0.9.
