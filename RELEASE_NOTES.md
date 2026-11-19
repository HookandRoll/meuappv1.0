# Jota Fantasy Map Maker — Alpha 1.1.29

Executável portátil para **Windows x64**. Baixe `Jota-Fantasy-Map-Maker-Alpha-1.1.29.exe` e execute. O pacote inclui o runtime .NET; não é necessário instalar Godot nem o SDK para usar o aplicativo.

Esta versão reúne as mudanças desde a **Alpha 1.0.15**, incluindo o editor e a evolução dos geradores v16–v29. O gerador de mapas novos é o **v29**.

## Editor de mapas

- Painel de edição com páginas para pincéis, inspeção de hexes, seleção e geração regional.
- Pintura de biomas, relevo e água; ferramentas de elevação, com forma, raio, valor e força do pincel.
- Edição dos campos do hex selecionado e prévia das operações.
- Seleção regional, locks por campo ou por hex e regeneração com preservação de água/elevação, conforme o modo escolhido.
- Regeneração de campos específicos, incluindo rios, respeitando locks e o exterior da seleção.
- Desfazer/refazer com restauração de dados, entidades e caches; uma pincelada corresponde a uma operação.
- Fluxo de rascunho, cancelar, salvar e salvar como, com progresso de salvamento.
- Painel ajustado a janelas de 1280×800 e 1100×700, com recorte e reposicionamento da área do mapa.

## Continentes, costas e ilhas

- Escultura continental como receita de criação, com sementes e quota de terra determinísticas.
- Separação das massas continentais e reservas de água entre ilhas e continentes.
- Etapa própria de ilhas, com distribuição espaçada e influência dos corredores tectônicos.
- Novas formas localizadas de baías, penínsulas e vales costeiros; redução de faixas de terra excessivamente finas.
- Tratamento de bacias fechadas como lagos e manutenção das conexões costeiras durante a geração.
- Melhor desempenho da avaliação tectônica e da seleção de ilhas em mapas grandes; progresso durante essas etapas.

## Tectônica e cordilheiras

- Placas procedurais com movimentos, contatos convergentes/divergentes e influência sobre crosta, domos, colisões, rifts, fossas e subducção.
- Quantidade de placas proporcional ao tamanho do mapa, de 10 até 96.
- Camada **Tectônica** com fronteiras, movimentos e legenda; referência original preservada ao editar e salvar.
- Recuperação do perfil contínuo e irregular das cordilheiras, corrigindo a redução excessiva das versões intermediárias.
- **v29: encostas mais suaves**, ampliando o alcance do perfil de elevação em 1,65 vez, preservando a altitude das cristas e a distribuição de terra/água.
- Nos três comparativos da v29, o percentil 95 dos desníveis entre vizinhos junto às montanhas diminuiu aproximadamente 31–36%. São medidas dos casos testados, sem impor uma proporção a todos os mapas.

## Clima e biomas

- Chuva orográfica baseada no relevo final, com condensação nas subidas, sombra de chuva e recuperação de umidade após barreiras.
- Transporte contínuo por todos os hexes, corrigindo listras periódicas atrás de cordilheiras estreitas.
- Continuidade do transporte de umidade na costura horizontal de mapas com wrap.
- Acumulação e degelo climatológicos usados no escoamento anual, sem criar água sem precipitação.
- Fertilidade junto a rios e margens baixas de lagos: áreas quentes de deserto/estepe podem se tornar campos, respeitando locks de bioma.

## Rios e lagos

- Rede de drenagem determinística com preenchimento auxiliar de depressões, sem sobrescrever a elevação física.
- Lagos com sangradouro e balanço anual de entrada, evaporação e excedente para os rios de saída.
- Rios persistentes com caminhos, vazões, ligação de afluentes e cinco níveis: nascente, riacho, rio pequeno, rio e grande rio.
- Convergência de bacias e união de cabeceiras admissíveis, com verificação de altitude e ausência de ciclos.
- Curvas nos caminhos lógicos e no desenho, com âncoras compartilhadas nas confluências, nas arestas e nas emendas dos chunks.
- Largura contínua pela vazão, nascentes afinadas, tons de azul por porte e shader de correnteza.
- Foz alinhada ao contorno da água, removendo pontas flutuantes no oceano e traços inteiramente submersos nos lagos.
- Cursos selecionados permanecem conectados até oceano, lago, área úmida, outro curso ou borda física; uma transição para clima árido não corta um rio no meio da terra.
- Supressão de nascentes locais visíveis em desertos quentes e secos, preservando rios sustentados por água vinda de outras regiões.
- Inspeção dos cinco níveis e suporte a locks, regeneração regional, save/load e undo/redo das redes.

## Arquivos e compatibilidade

- Persistência da referência tectônica e das entidades fluviais no `.world`, com reconstrução dos caches ao abrir.
- Ajuste dos limites de entidades esparsas para mapas grandes e validação antes de substituir o arquivo salvo.
- Saves antigos preservam os dados e a versão do gerador. Abrir/salvar não regenera silenciosamente terreno, clima ou rios.
- Para obter a geografia e as encostas da v29, crie um mapa novo. Os algoritmos de geração antigos continuam disponíveis para reproduzir arquivos existentes.

## Verificação

- Build .NET sem erros ou avisos.
- Smoke completo: 524.288 hexes, salvar/reabrir, costuras regionais, locks, edição e histórico.
- Matriz v18–v29 com três seeds, três percentuais de terra e wrap ligado/desligado, verificando determinismo, quota, rios e persistência.
- Comparação renderizada v28/v29 em 512×256 (40% e 50% de terra) e 1024×512 (38%): costas idênticas, nenhuma altitude anterior reduzida e zero términos fluviais desconhecidos sem pai.
- Evidências e detalhes por etapa nos relatórios de implementação em `results/*.md`.
- EXE final testado numa pasta isolada: smoke completo aprovado (incluindo save/load) e inicialização gráfica OpenGL concluída sem erros.

Tectônica e clima são receitas de geração estáticas. Simulação temporal de placas, erosão dinâmica, desenho manual de redes fluviais e estradas não fazem parte desta entrega.
