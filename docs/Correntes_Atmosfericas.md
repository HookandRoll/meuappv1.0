# Correntes atmosféricas e umidade na Alpha 2.1

Mapas novos de terreno completo usam a receita v73. Os ventos participam da geração de chuva, biomas e rios. As camadas de vento mostram a mesma circulação usada no clima; abrir um save antigo preserva a receita daquele mapa.

## Três células, duas camadas

Hadley, Ferrel e Polar são três faixas de circulação em cada hemisfério, com transições suaves em torno de 30° e 60° de latitude. Suas intensidades parametrizadas são 1,0, 0,8 e 0,6. Essas intensidades orientam o perfil de pressão; não são multiplicadores diretos da chuva nem uma garantia de velocidade fixa em cada faixa.

As três células usam as duas camadas do modelo. **Ventos Baixos** mostra o ar próximo à superfície, sujeito a atrito e desvio pelo terreno. **Ventos Altos** mostra a circulação em altitude. As camadas trocam massa, movimento, calor, vapor e água de nuvem por movimentos de subida e descida. Ambas transportam água e podem formar nuvens; altitude não é uma camada exclusiva de chuva.

Pressão e Coriolis orientam o movimento horizontal. O relevo afeta a camada baixa e a temperatura do ar: ele resfria ao subir e aquece ao descer, favorecendo condensação na subida e ar mais seco no lado protegido da montanha. A inspeção mostra direção, velocidade normalizada, pressão, movimento vertical e turbulência. Laranja indica subida e azul indica descida nas visualizações de vento.

O cálculo usa uma grade atmosférica reduzida, chegando a 512 × 256 nos mapas maiores. O campo é amostrado nos hexes e acompanha o contorno do Atlas. Não existe uma simulação separada de correntes marítimas nesta versão: oceanos e lagos fornecem água ao ar, mas não têm um campo de circulação oceânica.

## Umidade não é chuva

**Umidade** mostra a média da umidade relativa da camada baixa durante o período amostrado: vapor disponível dividido pela capacidade do ar na temperatura e massa daquele passo. A escala é fixa em todos os mapas: vermelho em 0%, claro em 50% e azul em 100%. O valor é limitado a 0–100% para visualização. Nuvens, água no solo e chuva não entram como vapor nessa razão.

**Chuvas** mostra a precipitação anual final em mm, após o espalhamento nos hexes e o ajuste do formulário. Um lugar pode ter ar úmido e pouca precipitação, ou receber chuva sem manter alta umidade relativa durante todo o período. A camada de umidade não é uma recoloração da chuva.

Os campos de vento e umidade são derivados e ficam em cache. Ao reabrir um mapa da receita acoplada, são reconstruídos em segundo plano sem substituir a chuva e os biomas salvos. Mapas antigos sem o clima acoplado não apresentam uma umidade inventada; a opção fica indisponível.

## Água e precipitação

A capacidade nominal é 40 unidades normalizadas de água a 25 °C por unidade de massa de ar, variando com temperatura e massa nas duas camadas. O aporte aquático acompanha o déficit de vapor, com alvos de umidade de 90% no oceano profundo, 95% no oceano raso, 85% nos lagos e 80% nas demais superfícies aquáticas. Há mistura horizontal de vapor e nuvens com coeficiente 0,08 e reciclagem de água pelo solo.

Quando a chuva terrestre anualizada da simulação ultrapassa 500 mm, 25% da nova chuva em terra retorna às nuvens baixas, descontado da infiltração. A condição usa o histórico da simulação, antes do bônus espacial de chuva.

O campo final de chuva recebe 50% do valor-base do próprio hex, 15% de cada vizinho do primeiro anel e 5% de cada vizinho do segundo. Em terreno livre, os pesos somam 200%; esse bônus é separado do balanço de água atmosférico. Uma contribuição pode subir até 500 m da origem; o segundo anel exige caminho pelo primeiro, respeitando também as subidas entre hexes. Descidas são livres e pesos bloqueados são descartados. [Detalhes do relevo e dos biomas secos](../EXPERIMENTAL_TERRAIN_RAIN.md).

Não há a antiga redução direta de chuva na faixa subtropical nesta receita. A latitude ainda participa da temperatura de referência e do perfil de circulação. As regras de gelo polar também continuam ativas. O modelo usa processos e unidades parametrizados, sem calibração meteorológica completa.
