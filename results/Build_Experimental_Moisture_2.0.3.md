# Ajuste experimental de umidade — Alpha 2.0.3

Novos mapas de terreno completo usam a receita v66. As receitas v63, v64 e v65 não recebem retroativamente os novos parâmetros.

- A capacidade de vapor aumenta 50% nas duas camadas, em todas as temperaturas; os alvos de umidade relativa permanecem iguais.
- Sobre oceano profundo ou raso, cada passo precipita metade da fração de água de nuvem usada sobre terra. A água não precipitada permanece nas nuvens; não é criada nem contabilizada como chuva.
- Nas células costeiras, a proporção de oceano pondera a redução. Uma célula com 50% de oceano precipita 75% da fração terrestre, nas mesmas condições.
- Intensidades Hadley/Ferrel/Polar continuam 1,0/0,8/0,6. O multiplicador global de conversão de chuva permanece 10,5.

Validação realizada:

- Compilação sem avisos ou erros.
- Teste de transporte: perda oceânica de 50%, costa mista de 75%, armazenamento inicial +50%, nuvens retidas, conservação de água e receita v65 idêntica ao controle anterior.
- Cenário controlado de oceano uniforme a 25 °C (128×64, 40 passos de aquecimento + 40 de amostragem): vapor médio 29,816 → 44,613; chuva anual média bruta 51,711 → 60,725. São unidades do modelo e este cenário não representa todas as seeds.
- Testes das três células: sentidos de circulação, continuidade das transições, determinismo, relevo, grade 512×256 com 400 passos, conservação de ar/água, chuva principal v66 sem máscara extra, save/load e regeneração regional com bloqueios.
- Regressão Hadley v64: seeds 1234 e -77 passaram em criação, receita diagnóstica, save/load, cache e regeneração regional com bloqueios.
- EXE portátil: criação pela interface com gerador v66, camadas, chuva/biomas e hashes idênticos dos ventos reconstruídos depois de salvar e carregar. Processo terminou com código 0.

EXE: `dist/Jota-Fantasy-Map-Maker-Alpha-2.0.3-Experimental-Hadley-Ferrel-Polar.exe`.
Crie um mapa novo para testar os ajustes.
