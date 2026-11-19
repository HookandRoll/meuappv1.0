# Restauração das cordilheiras — v28

19/11/2026. Correção local do gerador; sem atualização dos executáveis em `dist`.

A v24 separou um núcleo tectônico muito estreito e comprimiu a altitude das encostas. A v27 também reduziu a intensidade das cristas, suavizou seu campo e diminuiu a elevação adicional. Esses tratamentos apagavam partes das cadeias.

A v28 recupera a receita contínua da v26. O tratamento atenuado da v27 fica restrito aos mundos que registram essa versão. Permanecem a correção dos términos dos rios da v24 e o transporte contínuo de umidade da v25.

O comando `--restored-ridge-review` compara v24, v26, v27 e v28 em três configurações, todas com seed 1234 e três continentes. A v28 precisa reproduzir exatamente os arrays de elevação, relevo, biomas, chuva e água da v26; comparar apenas a quantidade de picos não detectaria cadeias deslocadas ou partidas. O teste também rejeita rios sem pai que terminem em `Custom`.

| Dimensões / terra | Picos v24 | Picos v27 | Picos v28 | Alpino v28 |
|---|---:|---:|---:|---:|
| 512×256 / 40% | 804 | 953 | 1.884 | 1.044 |
| 512×256 / 50% | 840 | 1.381 | 2.494 | 1.023 |
| 1024×512 / 38% | 2.739 | 1.763 | 4.988 | 4.820 |

Todos os comparativos passaram, com zero términos desconhecidos sem pai. Capturas reais das camadas Terreno, Elevação e Tectônica, métricas e mundos v28 estão em `results/restored-ridges-v28`. As capturas de terreno e elevação de 1024×512 foram inspecionadas visualmente.

Para usar a correção, criar um mapa com o projeto recompilado. Os mundos salvos continuam com seus dados e sua receita de geração; abrir um arquivo antigo não recupera automaticamente as cordilheiras. Há uma amostra pronta em `results/restored-ridges-v28/1024-land-38-v28.world`.

Build: `dotnet build Mapmaker.csproj --no-restore`, zero avisos e erros. Logs da verificação: `.godot/v28-restored-review.log`, `.godot/v28-smoke.log`, `.godot/v28-maximum.log`.

Smoke completo aprovado: 524.288 hexes, save/load, emendas regionais, edição e matriz v18–v28 com três seeds, três percentuais de terra e wrap ligado/desligado. O mapa máximo 4096×2048 concluiu geração e renderização em 94,73 s, com 111.257 células alpinas, 18.221 trechos fluviais e zero términos desconhecidos sem pai. Captura conferida: `results/generator-v28/maximum-map.png`.
