# Fechamento do M3 — 18/11/2026

Validado antes das alterações da nova geração (gerador v17).

- `dotnet build --no-restore`: zero erros e avisos.
- Smoke headless: aprovado, 524288 hexes; save/load 0,185 s.
- Smoke renderizado, 1100 × 700: aprovado; save/load 0,184 s.
- Seleção e pinceladas cruzando quatro chunks; uma operação por pincelada.
- Pincéis de bioma, relevo, água e ferramentas de elevação; formas e força.
- Locks por campo e hex inteiro, três modos de regeneração e campo único.
- Nenhuma mutação fora da seleção; undo/redo integral.
- Draft, cancelar, salvar, salvar como e reabrir preservando dados e histórico esperado.
- Camada de locks verificada por pixels no teste renderizado.

Logs: `.godot/m3-closure.log` e `.godot/m3-closure-rendered.log`.
Capturas: `.godot/editor-page-*-1100x700.png`.

Conclusão: gate do M3 aprovado no ambiente atual. Isso não equivale a benchmark no tamanho máximo nem a publicação de release.
