# Teste visual: montanhas com metade do tamanho do vulcão

Data: 24/11/2026. Ajuste local para avaliação visual do usuário, sem publicação de EXE. O gerador geográfico é considerado completo pelo usuário; este teste muda somente a apresentação das artes existentes.

## Comportamento

Montanhas, picos e picos nevados usam a mesma curva de zoom do `VolcanoOverlay`: transição suave de 12 a 22, com metade de suas dimensões em cada ponto. No shader, as dimensões são convertidas para as unidades UV do hex.

| Escala | Vulcão, unidades do mapa | Montanha, unidades do mapa |
|---|---|---|
| Zoom até 12 | 6 × √3 por 9 | 3 × √3 por 4,5 |
| Zoom a partir de 22 | 1,35 × √3 por 2,48 | 0,675 × √3 por 1,24 |

Posições, variantes, sombreamento do bioma e cores das artes existentes foram preservados. Morros, escarpas e vulcões mantêm suas escalas anteriores. A máscara de árvores permanece ativa. A nova escala não altera classificação de relevo, elevação, rios, clima ou formato de save.

O alcance de desenho das montanhas maiores foi ampliado para 37 hexes em ordem de profundidade. Os hexes adicionais desenham somente montanhas; o alcance normal de 11 hexes continua nos zooms próximos. Isso evita cortar os símbolos grandes nos limites dos hexes.

`mountain_zoom_scale = 0` reproduz o desenho anterior; `1` ativa o teste, atualmente padrão local. Esse uniform permite capturar antes/depois sem regenerar ou mudar o enquadramento.

## Capturas

Mapa Região 512×256, gerador v73, seed 1234. O enquadramento foi escolhido pela concentração de símbolos de montanha, centrado no hex (383, 214). Capturas reais do aplicativo, com e sem o ajuste, nos zooms 4, 12, 17, 22 e 44.

- [Antes, zoom 12](mountain-zoom-half-volcano/mountains-zoom-12-before.png)
- [Depois, zoom 12](mountain-zoom-half-volcano/mountains-zoom-12-after.png)
- [Antes, zoom máximo](mountain-zoom-half-volcano/mountains-zoom-44-before.png)
- [Depois, zoom máximo](mountain-zoom-half-volcano/mountains-zoom-44-after.png)

As capturas mostram faixas de montanhas mais preenchidas à distância e símbolos menores no zoom próximo. Este mapa não gerou vulcões; a relação de metade do tamanho foi aplicada usando as dimensões do renderer, sem inserir um vulcão artificial na geografia.

## Desempenho do teste

Godot 4.7.2 .NET, Debug, OpenGL Compatibility, RTX 5070, janela 1280×800, sem VSync. Uma passagem por variante/enquadramento, 8 frames de aquecimento e 120 frames de movimento lateral curto. São FPS médios locais, sem garantia de FPS mínimo nem validação de todos os tamanhos.

| Zoom | Antes | Novo visual |
|---|---:|---:|
| 4 | 249,4 FPS | 128,4 FPS |
| 12 | 188,8 FPS | 135,6 FPS |
| 17 | 209,5 FPS | 130,4 FPS |
| 22 | 187,4 FPS | 240,6 FPS |
| 44 | 274,3 FPS | 189,4 FPS |

A região ampliada de amostragem tem custo perceptível nos zooms afastados, embora todas as amostras do novo visual tenham ficado acima de 60 FPS. Os resultados por variante podem variar com carga e agendamento do sistema. Não substituem o benchmark de referência do M4.

Métricas em `results/mountain-zoom-half-volcano/metrics.json`; log em `.godot/mountain-zoom-review.log`. O processo terminou com código 0. Build sem erros, com o aviso NU1900 de consulta ao NuGet indisponível. Godot registrou as falhas ambientais de certificados e gravação de cache já observadas anteriormente, sem falha de compilação do shader.

## Conferência da entrega

- PASS, execução: build e teste renderizado concluíram; dez capturas antes/depois geradas.
- PASS, propósito: mesma curva do vulcão com dimensões pela metade; terreno mais legível ao aproximar.
- PASS, identidade: mesmas imagens e cores, sem novos assets; variantes e posicionamento preservados.
- PASS, verificação visual: capturas de zoom 12 e 44 inspecionadas; ampliação distante e redução próxima confirmadas.

## Reprodução

```powershell
dotnet build Mapmaker.csproj --no-restore
& '.\Godot_v4.7.2-stable_mono_win64_console.exe' --path . --log-file .godot/mountain-zoom-review.log -- --mountain-zoom-review
```
