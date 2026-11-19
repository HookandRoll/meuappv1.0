# Cordilheiras intermediárias e correção das listras — v25

Data: 19/11/2026. Alterações locais, sem publicação de release.

## Causa e correção

A redução da v24 deixou as cristas estreitas demais. A largura do núcleo tectônico passa de 0,035 para 0,075 da escala média das placas. O perfil de altitude também recebe uma transição intermediária: encostas continuam suaves, e a elevação alta permanece concentrada nas colisões. O resultado não volta aos platôs largos da v23.

As listras tinham uma causa separada. A antiga chuva orográfica amostrava o relevo em passos de `max(1, height/128)` — 16 hexes no mapa máximo. Uma cordilheira estreita podia ser amostrada ou ignorada conforme a fase desse salto, produzindo um padrão periódico de umidade e biomas a sotavento. Afinar as cordilheiras tornou o defeito mais evidente.

A v25 transporta umidade por todos os hexes de cada linha, com condensação nas subidas e recuperação contínua da umidade após as barreiras. A passagem tem custo linear; usa buffers de linha reutilizados e um campo temporário de 16 bits por célula da área gerada. Nenhum estado climático passa a ser simulado durante navegação ou edição.

Com wrap horizontal, a condição periódica da umidade é resolvida pela composição da recorrência linear da linha, sem uma costura no início da varredura. Na geração regional, a leitura considera o relevo atual fora da seleção, e a escrita continua limitada à região e aos campos autorizados. O cálculo atmosférico utiliza a superfície da água como referência, não a profundidade do fundo oceânico.

A correção dos términos de rios da v24 permanece ativa. As receitas anteriores continuam preservadas pela versão registrada no mundo.

## Comparação medida

1024×512, seed 1234, 38% de terra e três continentes:

| Versão | Células alpinas | Células Peak | Términos de rio desconhecidos sem pai |
|---|---:|---:|---:|
| v23 | 7.694 | 8.479 | 7 |
| v24 | 2.572 | 2.739 | 0 |
| v25 | 5.830 | 5.895 | 0 |

Capturas e mundos em `results/ridges-climate-v25`. A área alpina é uma medida do caso de teste, não uma largura ou contagem garantida para toda seed.

## Validação

- Build sem erros ou avisos.
- Teste de barreira estreita: redução de umidade ao atravessar a montanha, seguida de recuperação monotônica, sem ciclos periódicos de faixas.
- Teste de wrap: rotacionar a linha de relevo e água desloca a umidade junto, com diferença máxima de uma unidade em 16 bits, nos dois sentidos do vento.
- Matriz de regressão ampliada para v18–v25, com três seeds, três percentuais de terra e wrap ligado/desligado.
- Determinismo, quota, caminhos de rios sem ciclos, destinos válidos, persistência dos cinco níveis, locks, exterior da seleção e histórico do editor.
- Execução renderizada 4096×2048: dados gerados em 95,2 s, imagem pronta em 103,71 s; 18.965 trechos fluviais e zero términos desconhecidos sem pai. Captura do mapa máximo conferida, sem o padrão de listras do comparativo anterior.

Logs: `.godot/v25-final-smoke.log`, `.godot/v25-ridge-final.log` e `.godot/v25-maximum.log`. O aviso preexistente de certificados não impediu os testes. Os tempos são medições de uma execução local.

Captura máxima: `results/generator-v25/maximum-map.png`.

Para experimentar, criar um mundo v25. Arquivos anteriores mantêm seus campos salvos e sua versão de regeneração; abrir/salvar não substitui silenciosamente o clima ou as edições.
