# Alpha 2.0.1 Experimental — Hadley principal

Nesta build, **Mapa → Novo mapa → Terreno completo** usa a simulação Hadley acoplada como clima principal. Ela produz a chuva usada pelos biomas e pelos rios, com temperatura e umidade nas duas camadas, água de nuvem, precipitação gradual, reciclagem pelo solo e histórico térmico sobre o relevo.

Os cinturões prescritos de pressão de três células estão desligados e a chuva não passa pela penalidade subtropical do clima anual antigo. O gradiente térmico equador–polos continua fazendo parte do protótipo; isso não é uma atmosfera sem influência da latitude.

## Testar

1. Execute `Jota-Fantasy-Map-Maker-Alpha-2.0.1-Experimental-Hadley-No-Bands.exe`.
2. Crie um mapa novo com conteúdo **Terreno completo**. O formulário identifica o clima Hadley sem bandas prescritas.
3. Confira **Chuva**, **Biomas** e **Ventos — Hadley**. **Hadley: altitude** mostra o retorno na camada superior.
4. Salve e reabra o arquivo `.world` para continuar os testes. A receita Hadley fica registrada como gerador v64.

As metas de umidade continuam em 90% no oceano, 95% no oceano raso de até 200 m, 85% nos lagos e 80% nas demais fontes. A taxa aquática efetiva é 0,36. A conversão da chuva usa o fator fixo **10,5**, comum a todas as seeds, com o teto de 5000 anterior ao slider de precipitação. Não há ajuste automático para voltar à média do clima antigo.

Os mapas antigos mantêm sua receita. Abrir um mapa v63 não recalcula seu clima; crie um mapa novo para testar o Hadley como principal. Use esta build experimental para regenerar mapas v64. A versão Alpha 2.0 anterior não suporta regeneração v64.

## Cálculo e limites

A camada Ventos de um mapa v64 usa o mesmo campo Hadley que produziu seu clima, aproveitando o resultado já calculado na criação. Depois de reabrir um save, o campo pode precisar ser recalculado em segundo plano. Editar o relevo invalida esse campo. Regenerar clima resolve a circulação global, mas aplica as mudanças somente à seleção e respeita os locks.

A geração de Atlas grandes e o primeiro recálculo de ventos após abrir podem levar alguns minutos. O modelo continua experimental, com unidades e processos parametrizados e sem calibração meteorológica completa. A temperatura anual usada na classificação dos biomas mantém a fórmula de referência por latitude, altitude e influência marítima; as temperaturas atmosféricas evoluem dentro da simulação.

A build usa arquivo e título próprios. O EXE Alpha 2.0 já publicado permanece separado.
