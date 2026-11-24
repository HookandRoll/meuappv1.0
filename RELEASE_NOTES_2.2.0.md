# Alpha 2.2 - World Geography

Jota Fantasy Map Maker para Windows x64, em EXE portátil com runtime .NET e recursos incorporados. Esta versão encerra o M5 no escopo geográfico ajustado.

- Árvores mais legíveis no zoom máximo: aproximadamente metade dos grupos fica oculta, com transição gradual e seleção estável por posição e seed. A cobertura distante permanece preservada.
- Montanhas, picos e picos nevados usam metade das dimensões dos vulcões, com desenho maior à distância e redução gradual ao aproximar.
- Aba **Editar mapa → Redes** para desenhar e apagar trechos de rios, com larguras, seleção, locks, undo/redo, salvar e cancelar rascunho. A ferramenta provisória de estradas já implementada também está disponível; gerar conexões entre pontos fica para uma etapa futura.
- Geografia, clima acoplado, biomas e rios mantêm a receita v73. As artes continuam independentes dos dados do mundo.
- Nome do método de geração simplificado para **Escultura continental** e identificação da versão atualizada na build.

## Compatibilidade e escopo

O formato dos saves e a receita de geração permanecem os mesmos. Arquivos anteriores preservam sua geografia; as mudanças de escala e visibilidade são visuais e não exigem regenerar o mapa.

O fechamento do M5 não inclui as ferramentas de campanha do M6 nem a Definition of Done completa do protótipo. Na regeneração regional sem rota válida, o comportamento atual preserva o trecho fluvial original, conforme registrado no relatório de fechamento.

## Downloads

- [EXE portátil Windows x64](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.2.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.2-World-Geography.exe)
- [Relatórios e capturas de validação](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.2.0-alpha/Jota-Fantasy-Map-Maker-Alpha-2.2-Validation.zip)
- [Checksums SHA-256](https://github.com/HookandRoll/meuappv1.0/releases/download/v2.2.0-alpha/SHA256SUMS.txt)

[Fechamento do M5](https://github.com/HookandRoll/meuappv1.0/blob/v2.2.0-alpha/results/M5_Fechamento.md). [Validação da versão](https://github.com/HookandRoll/meuappv1.0/blob/v2.2.0-alpha/results/Validacao_Alpha_2.2.md).
