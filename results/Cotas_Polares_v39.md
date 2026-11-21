# Contagem e porcentagem sem os polos — v39

Nos novos Mundos, Ártica e Antártica continuam como duas placas extras obrigatórias. Não consomem vagas de placas comuns e não entram no total usado para calcular a deformação das fronteiras, o espaçamento tectônico e a escala do relevo. Seus contatos continuam participando fisicamente da geração para manter o oceano polar.

A base da porcentagem de terra e mar agora exclui todos os hexes úteis pertencentes a essas duas placas. Por exemplo, o alvo de 38% significa 38% de terra e 62% de água na área não polar. O oceano polar é adicional. Ele permanece no mapa, no armazenamento, no tamanho informado e nos cálculos de clima; apenas fica fora dessa porcentagem.

O limite de ruído que define a placa também define os hexes excluídos. O ajuste do nível do mar e a correção exata da quantidade de terra usam a mesma base. A separação continental preserva essa quantidade sem repor terra nos polos.

Região mantém a porcentagem sobre o retângulo inteiro. Saves v38 mantêm a regra anterior ao regenerar, para preservar sua geografia. O formulário explica a nova regra na dica da porcentagem.

## Validação

- Build sem erros ou avisos.
- Matriz polar com três seeds, com/sem wrap e 10/38/65% de terra, tanto na v38 quanto na v39: contagem independente das placas comuns, área não polar contada diretamente, quantidade exata de terra e oceano polar permanente.
- Verificações de determinismo, regeneração de água/elevação/biomas e salvar/abrir.
- Suíte completa do gerador e smoke test da aplicação.

Comandos: `dotnet run --project tests/GeneratorChecks/GeneratorChecks.csproj --no-restore`; Godot com `--headless --path . -- --smoke-test --current-generator-only`.
