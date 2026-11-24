# Validação Alpha 2.1

- Projeto principal Mapmaker.csproj compilado sem avisos ou erros, versão 2.1.0-alpha.
- WorldGenerator.Version e criação normal selecionam a receita v73. Referência anual legada v63 preservada nos comparativos.
- Teste v73: limites de 500/501 m, caminhos e cristas, descidas, wrap, pesos bloqueados, limites de biomas, seeds 1155827384 e -77, save/load e regeneração.
- Regressão v72: capacidade 40 e aporte proporcional nos seis tamanhos e nos modos Mundo/Região; anéis, teto, bordas e conservação de água.
- Compatibilidade: 48 mundos de referência idênticos bit a bit, incluindo terreno, clima, rios, regiões e ícones.
- EXE portátil: código de saída 0; criação pela interface com generator=73, dez seletores, camada de umidade com valores finitos em 0–100%, save/load exato e reconstrução com hashes idênticos de vento e umidade.
- Capturas: alpha21-layers.png, alpha21-humidity.png e mapas de terreno, chuva, biomas e ventos no pacote de validação.
- A média de umidade é um diagnóstico adicional: não altera as equações de transporte, chuva nem o balanço de água.

A regra de chuva v73 foi validada nesta entrega em mapas pequenos; os testes do ajuste atmosférico v72 já cobrem os parâmetros dos seis tamanhos. Esta entrega não repete uma bateria geográfica nova no Atlas máximo.
