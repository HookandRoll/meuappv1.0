# Elevação com feathering — gerador v27

O ajuste atua no relevo físico: a influência tectônica deixa de concentrar tanta altitude no eixo das colisões. O objetivo é manter cordilheiras reconhecíveis, com picos localizados e encostas que passam por montanhas, colinas e terras mais baixas.

## Implementação

- A força de elevação de cada contato passa a considerar sua intensidade. Contatos fracos não recebem a mesma crista dos mais fortes.
- Duas passagens de suavização separável espalham parte da influência para as encostas. O campo final combina 45% da crista original e 55% do campo suavizado. O raio acompanha o tamanho médio das placas.
- A parcela adicional de altitude usa `1450 × influência²`, em lugar de `1800 × influência^1,5`. Isso reduz especialmente as elevações intermediárias e a concentração excessiva nos eixos.
- A escultura dos continentes permanece separada desse ajuste. A comparação v26/v27 verifica igualdade exata do array de terra/água.
- Chuva e drenagem usam a altitude final. A alteração não é um efeito de shader.
- Os dois buffers de suavização são temporários; no limite de 8.388.608 células representam 64 MiB adicionais durante essa etapa. Não são salvos no mundo.
- As receitas de versões anteriores são preservadas. Mundos antigos mantêm sua versão; crie um mundo v27 para avaliar o ajuste.

## Comparação controlada

Seed 1234, 1024 × 512, 38% de terra, três continentes, sem wrap horizontal. A distribuição de terra/água ficou idêntica.

| Relevo em terra | v26 | v27 |
|---|---:|---:|
| Plano | 55.091 | 56.115 |
| Ondulado | 38.567 | 40.722 |
| Colinas | 77.893 | 79.988 |
| Montanhas | 22.690 | 20.641 |
| Picos | 4.988 | 1.763 |
| Bioma alpino | 4.820 | 1.404 |

Esses números descrevem esta seed, não uma proporção imposta a todos os mundos. O bioma também depende de temperatura e chuva; a sequência de cores não é uma faixa obrigatória em toda encosta.

Capturas e mundos comparáveis: `results/elevation-feather-v27/`. As camadas 0, 1 e 6 mostram terreno, elevação e tectônica.

## Validação

- Build .NET: zero erros e zero avisos.
- Smoke test completo: passou, incluindo 524.288 hexes, salvar/reabrir, emendas regionais e edição M3.
- Matriz de geração incluindo v27: três seeds, 30/40/50% de terra, com e sem wrap; determinismo, quota de terra, rios, persistência, locks e undo/redo regional.
- Mapa máximo 4096 × 2048: geração e renderização concluídas em 94,89 s; 68.005 hexes alpinos, 18.431 segmentos fluviais e nenhum término de rio sem destino. Captura: `results/generator-v27/maximum-map.png`.
