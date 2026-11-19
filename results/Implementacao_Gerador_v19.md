# Geração v19 — mais placas e influência tectônica

## Alteração

A quantidade de placas passa a depender da área: `clamp(6 + round(sqrt(largura × altura) / 24), 10, 96)`.

| Mapa | Placas |
|---|---:|
| 128 × 64 | 10 |
| 512 × 256 | 21 |
| 1024 × 512 | 36 |
| 2048 × 1024 | 66 |
| 4096 × 2048 | 96 |

A contagem deixa de depender da quantidade de continentes. A escala em km por hex continua sendo metadado e não altera essa contagem.

- Núcleos continentais avaliam 32 posições candidatas em vez de 12, favorecendo mais fortemente crosta continental e evitando domínios oceânicos.
- Domos de crosta continental: amplitude de 0,65 para 1,45. Rebaixamento oceânico: −0,25 para −0,85.
- O alcance dos domos acompanha a área média das placas, evitando sobreposição crescente de domos enormes conforme a contagem aumenta.
- Influência dos contatos no score: fator de 0,08 para 0,15, mantendo os operadores com alcance finito.
- Ranking das ilhas aumenta a contribuição tectônica de 0,8 para 2,4. Corredores favorecem o lado superior da subducção, usando o mesmo critério das cordilheiras e fossas. Tamanho do corredor acompanha a escala média das placas.
- Permanecem as quotas de terra e as reservas marítimas entre ilhas e continentes.

A versão do gerador foi incrementada para 19. As versões 17 e 18 preservam seus caminhos anteriores, inclusive a fórmula antiga da contagem de placas e seus pesos.

## Verificação

Build sem avisos ou erros. Smoke completo aprovado, com matriz de 18 configurações para v18 e mais 18 para v19 (três seeds, três percentuais, wrap ligado/desligado), verificando repetibilidade, quota de terra, salvamento, caches de rios, locks e limites da regeneração regional. Contagens de placas validadas separadamente até as dimensões máximas, sem gerar terreno completo no tamanho máximo.

Pranchas e mundos para 30%, 40% e 50% em `results/natural-v19`. Layer tectônica conferida visualmente. Logs em `.godot/v19-smoke.log` e `.godot/v19-preview.log`.

As medições de preview são de uma execução e não são um benchmark de desempenho. A maior quantidade de contatos aumenta o custo da geração, particularmente no ranking das ilhas. Não há simulação tectônica durante a navegação ou pintura.
