# Alpha 2.0.4 Experimental — Hadley, Ferrel e Polar

Nesta build, **Mapa → Novo mapa → Terreno completo** usa três circulações parametrizadas no mesmo fluido de duas camadas. A receita fica salva como gerador v67. Mapas v64 continuam com o Hadley isolado da build anterior; abrir um save não muda seu clima.

## O que foi implementado

O perfil de pressão tem três trechos suaves em cada hemisfério, com intensidades próprias e transições padrão em 30° e 60°. A circulação superficial é orientada para o equador na Hadley, para o polo na Ferrel e para latitudes menores na Polar. A camada superior recebe o gradiente oposto para a circulação de retorno. A transferência de massa, Coriolis, atrito, relevo, vorticidade, transporte térmico e água continuam no mesmo solver.

| Parâmetro | Padrão | Papel |
| --- | ---: | --- |
| HadleyStrength | 1,0 | Contraste de pressão da célula tropical |
| FerrelStrength | 0,8 | Contraste oposto da célula intermediária |
| PolarStrength | 0,6 | Contraste de pressão da célula polar |
| TropicalEdgeDegrees | 30° | Transição Hadley/Ferrel |
| PolarEdgeDegrees | 60° | Transição Ferrel/Polar |

O contraste de cada trecho é duas vezes sua intensidade, com interpolação cossenoidal que zera a derivada nas transições. O perfil remove sua média latitudinal, usando ponderação uniforme da grade, para não acrescentar um deslocamento uniforme ao forçamento vertical. A intensidade zero remove o contraste de pressão do trecho, sem eliminar correntes produzidas por temperatura, relevo ou pelas células vizinhas.

Temperaturas dinâmicas e anomalias continentais deformam a pressão durante a simulação; continentes e montanhas continuam desviando os fluxos. Não são três simuladores independentes: ar, momento, calor, vapor e nuvens atravessam as transições.

## Mistura horizontal de umidade (v67)

Vapor e nuvens passam a trocar água entre células vizinhas por difusão conservativa da concentração (água por massa de ar), em ambas as camadas. O coeficiente é 0,08 nas unidades da grade de referência; a discretização inclui o tamanho da grade. Cada fluxo sai de uma célula e entra na vizinha, sem uma fonte extra de água. O wrap e os limites norte/sul acompanham o transporte existente. Isso representa mistura não resolvida pela grade, não uma nova corrente de vento explicitamente simulada.

O tempo de precipitação permanece 3. O experimento 3→6 na seed 1155827384 aumentou o armazenamento de nuvens, mas reduziu a chuva média das terras baixas do interior; por isso não foi adotado. Os ventos continuam transportando água pela advecção existente, agora acompanhada da mistura horizontal.

Mapas v66 mantêm a mistura anterior (coeficiente zero); mapas novos usam v67.

## Ajuste de transporte de umidade (v66)

A capacidade de vapor aumenta 50% em ambas as camadas, mantendo a dependência da temperatura e os alvos de umidade relativa. Sobre oceano profundo ou raso, cada passo precipita 50% da fração de água de nuvem usada sobre terra; a água restante fica na nuvem e continua sendo transportada ou pode reevaporar. Nas células mistas, a fração de oceano pondera a redução. Não é um reembolso de água após contabilizar chuva.

Os mapas v65 preservam a capacidade e a taxa de precipitação anteriores. Crie um mapa novo para usar os ajustes.

## Chuva

As zonas orientam **pressão e circulação**, não valores diretos de precipitação. A chuva continua sendo calculada por vapor, resfriamento, condensação, água de nuvem e precipitação gradual. O gerador não chama a chuva de fundo do modelo anual v63 nem sua penalidade subtropical.

Os alvos de umidade permanecem em 90/95/85/80%, a reposição aquática efetiva em 0,36 e a conversão de chuva em 10,5, comum a todas as seeds. O histórico térmico do relevo e a reciclagem pelo solo permanecem ativos. Não se aumenta a chuva por um multiplicador próprio da Ferrel ou da Polar.

## Limite da aproximação

**Esta é uma estrutura de três células prescrita e parametrizada.** Não é uma simulação completa na qual a Ferrel emerge de perturbações baroclínicas e fluxos de calor e momento resolvidos. O modelo orienta sua circulação média pelo perfil de pressão. Os limites padrão são zonas de referência suaves, e não fronteiras rígidas para o transporte.

Portanto, esta build volta a usar uma organização zonal da circulação. Isso é diferente de recolocar uma faixa que reduz diretamente a chuva. O modelo continua experimental, com unidades normalizadas e sem calibração meteorológica completa.

## Testar

Execute `Jota-Fantasy-Map-Maker-Alpha-2.0.4-Experimental-Hadley-Ferrel-Polar.exe` e crie um mapa novo de terreno completo. Inspecione **Chuva**, **Biomas**, **Ventos — 3 células**, **Circulação: solo** e **Circulação: altitude**.

A exportação desta variante usa `MapmakerCloudTransport.csproj` e uma identidade de assembly própria. Isso separa o cache do runtime incorporado e permite manter uma build anterior aberta durante os testes.

```powershell
dotnet build MapmakerCloudTransport.csproj
dotnet run --project tests/GeneratorChecks -- --three-cells-only
dotnet run --project tests/GeneratorChecks -- --hadley-main-only
dotnet run --project tests/CompatibilityChecks
```

O teste de interface do executável usa `--hadley-main-review`: cria pelo formulário, confirma a receita selecionada, verifica o renderer primário, salva e reabre o mapa e exige que os hashes do vento recalculado sejam iguais. O EXE é separado das builds anteriores.
