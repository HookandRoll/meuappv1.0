# World Simulation RPG

Protótipo de mapa para RPG. O marco M0 testa a renderização de 524.288 hexes com Godot 4 .NET. O mapa de demonstração é determinístico e serve para exercitar o renderer.

## Abrir o protótipo

1. Instale o Godot **4.7.2 .NET** e um SDK .NET compatível com o alvo `net8.0`.
2. Abra `project.godot` no editor ou importe a pasta pelo gerenciador de projetos.
3. Pressione **F5**. Use a roda do mouse para aproximar, arraste com o botão direito ou do meio para mover e clique para inspecionar um hex.

O VS Code pode abrir a pasta inteira. Também é possível compilar com `dotnet build Mapmaker.csproj`.

## Verificar o M0

Após compilar, use o executável de console do Godot .NET. Substitua `godot` pelo caminho do executável se ele estiver fora do PATH.

```text
godot --headless --path . -- --smoke-test
godot --path . -- --benchmark
```

O primeiro comando verifica a conversão e a seleção pelos centros de todos os hexes. O segundo abre uma janela, captura `.godot/benchmark-m0.png` e mede pan/zoom, troca de layer por uniform e atualização integral das duas texturas de dados, totalizando 4 MiB no preset maior. A latência engloba o tempo até o sinal de conclusão do frame do Godot. O benchmark retorna código 2 quando a média de FPS fica abaixo de 30 ou a troca de layer chega a 100 ms.

O núcleo em `src/Core` usa C# puro. `src/GodotApp` cuida das texturas, do shader, dos lotes de relevo e da interface. A especificação na raiz lista os marcos seguintes.
