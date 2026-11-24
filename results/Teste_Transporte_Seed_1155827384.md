# Transporte de umidade — seed 1155827384

Alpha 2.0.4 experimental, receita v67. Atlas no último tamanho: referência 4096×2048, armazenamento 6144×2784, 15.044.776 hexes ativos e atmosfera 512×256. Usados os demais parâmetros padrão: 3 continentes, 25% de terra, costa 80, sem offsets de temperatura/chuva.

A mudança adotada é a mistura horizontal conservativa de vapor e nuvens, em ambas as camadas, com coeficiente 0,08. A advecção existente continua ativa. O tempo de precipitação permanece 3; capacidade de vapor +50% e regra oceânica de 50% permanecem iguais à Alpha 2.0.3.

O experimento preliminar que aumentava somente o tempo de nuvem para 6 foi descartado: nas terras baixas do interior a chuva média passou de 926,77 para 904,40 na grade atmosférica, apesar de maior vapor/nuvens. Isso não comprovou que os ventos eram fracos; mostrou que reter mais nuvem, isoladamente, não resolveu o caso.

## Comparação adotada

| Amostra | Chuva antes | Chuva depois | Células abaixo de 260 antes | Depois |
|---|---:|---:|---:|---:|
| Terras baixas do interior (9873 células) | 926.77 | 969.51 | 32.01% | 20.68% |
| Terras baixas do interior, latitude abaixo de 60 graus (8276 células) | 1064.61 | 1079.81 | 25.43% | 21.56% |

Amostra: células atmosféricas com maioria de terra, altitude média terrestre até 300 m e distância de pelo menos 3 células da água (incluindo lagos). Terras baixas não garantem ausência de relevo local. A chuva é a saída da grade multiplicada pela conversão 10,5, sem teto de visualização; o limiar 260 serve de indicador de secura, não é uma contagem final de biomas. As médias não são ponderadas pela área dos hexes nem pela área esférica.

As duas execuções reproduziram exatamente o mesmo campo de chuva de referência e a mesma superfície agregada. Dados por célula e os resumos completos estão em `results/transport-1155827384/` e `results/transport-1155827384-mixing/`.

## Validação

Testados: conservação e positividade da mistura, concentração uniforme com massas de ar diferentes, borda fechada e wrap, seleção exata da configuração v67, conservação de água no solver acoplado, regressão v65/v66, três células e relevo em 512×256, criação normal, bloqueios regionais e save/load. O EXE portátil criou v67 e reproduziu os hashes dos ventos depois de salvar e carregar, com saída 0.

Crie um mapa novo para usar v67. Saves anteriores preservam suas receitas. A comparação avalia esta seed com os parâmetros informados acima, não garante o mesmo ganho em todas as geografias.
