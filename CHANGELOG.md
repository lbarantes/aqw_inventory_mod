# Versões

Cada versão tem um nome descritivo, uma tag git (`v<versão>-<nome>`) e uma
cópia da DLL em `versions/`. Para trocar a instalada (com o jogo fechado):

```powershell
.\tools\use-version.ps1 v0.1.0
```

## v0.3.1 — clone-nao-trava

- **Corrige a aba de classes (e capacete/armadura) que às vezes não mostrava
  as artes até trocar de aba e voltar.** Às vezes o jogo falha ao montar o
  avatar do clone de alguma classe (`CustomizableCharacter.setupBack` com
  prefab nulo). Quando isso acontecia, o aviso de "clone pronto" nunca
  chegava, e o mod esperava 30s com o estúdio ocupado e as outras fotos
  paradas na fila. Agora:
  - um clone que já tem peças visíveis e estáveis é fotografado mesmo sem o
    aviso, e o limite de espera de um clone caiu para 15s;
  - o estúdio (câmera) só fica ocupado durante a foto em si; até 2 clones
    carregam em paralelo, cada um na sua vaga, longe do outro;
  - os clones têm fila própria e não ocupam mais as vagas de download das
    armas, capas e pets.
- Itens que ficam sem arte agora aparecem no `LogOutput.log` (uma linha
  `[InvThumbs] Sem arte para ...`), para facilitar o diagnóstico.

## v0.2.4 — elmos-clone-nao-trava (branch `v0.2.x`)

- A mesma correção da v0.3.1, aplicada sobre a v0.2.3 (cards por aba, sem a
  lista mista). Feita para comparar as duas linhas.

## v0.3.0 — lista-mista

- **Card por item, não por aba.** Na mesma lista, os itens com arte viram card
  (~150px) e os sem arte (house, itens de casa, misc, gems, pets sem arte
  convertida) ficam na linha original do jogo (~73px). Isso aproveita melhor o
  espaço no inventário principal ("Todas").
- **Aba de pets:** antes a aba inteira ficava em lista, porque a maioria dos
  pets não tem arte e a regra de "metade dos itens" (`CardMinRatio`, removida)
  não era atingida. Agora cada pet com arte vira card.
- Como funciona: a lista do jogo só suporta altura fixa (`fabHeight`). O
  `MixedList` (`Cards/MixedList.cs`) substitui a reciclagem de linhas do jogo
  (`inventoryListItem.Update`, desligado nas listas gerenciadas) por uma com
  altura por item. Cada linha reciclada alterna entre card e linha original
  (`CardRow.SetCard`) conforme o item que recebe.
- Limitação: ao navegar com gamepad, a rolagem automática até o item
  selecionado ainda supõe altura fixa e pode parar fora do lugar.

## v0.2.3 — elmos-mesma-escala

- **Capacetes com a mesma escala.** Antes a câmera se ajustava ao tamanho de
  cada capacete, então os grandes ficavam "longe" e os pequenos "perto". Agora
  o quadro tem tamanho fixo: uma fração da altura do corpo sem o capacete (o
  corpo é o mesmo para todos), centrada na cabeça (`GetHelmPlace`).
- Ajustável em `[Layout]`: `HelmFrame` (0.55, maior = mais afastado) e
  `HelmOffsetY` (0.1, sobe o centro). Esses valores entram na chave do cache,
  então mudar refaz as fotos.

## v0.2.2 — clone-sem-fantasma

- **Corrige o "preview fantasma".** Enquanto carregavam armaduras, classes e
  capacetes, um clone completo do personagem piscava no lado esquerdo da tela,
  um para cada item. A causa: o construtor do clone do jogo mantém a posição de
  mundo ao prender o objeto, então o clone nascia na origem do mapa, à vista
  da câmera. Agora ele vai para o estúdio fora da tela, na layer de
  renderização, desde o primeiro frame, e é mantido lá a cada frame enquanto
  carrega.
- **O capacete acompanha a armadura atual.** A foto do capacete é tirada
  vestindo a armadura (ou classe) equipada, como no preview do jogo. Agora a
  armadura faz parte da chave do cache, e o mod percebe a troca (verifica a
  cada 0,5s) e recarrega as linhas visíveis.

## v0.2.1 — cards-pets-elmos-armaduras

Correções na v0.2.0, sem mudar o visual dos cards.

- **House, Misc e Gems voltam a ser a lista original.** A decisão entre card e
  lista é feita pelo conteúdo de cada aba: só vira card se pelo menos metade
  dos itens tiver arte (`CardMinRatio`). Nas abas em lista, o mod não altera
  nada. Itens de casa ficam fora dos cards (`HouseInCards = false`).
- **Capacete, armadura e classe:** antes não aparecia nada. Agora seguem o
  mesmo caminho do preview do jogo: um clone do seu personagem vestindo o
  item é fotografado por uma câmera fora da tela. O capacete é enquadrado na
  cabeça.
- **Pets:** antes aparecia só uma peça solta. Agora o pet inteiro (todas as
  peças) é fotografado.
- Itens sem arte dentro de uma aba em card mostram o ícone do tipo, apagado,
  no centro do quadro.
- O cache em disco agora separa as entradas pela forma de extrair a arte. As
  miniaturas antigas e erradas dos pets deixam de ser usadas.

## v0.2.0 — cards-arte-grande

- Novo layout **Card** (padrão): cada linha fica com ~150px de altura (a
  original tem ~73) e ganha um quadro de 200px à esquerda com a arte grande,
  fundo escuro e borda na cor da raridade.
- O ícone do jogo (tipo, raridade, check de equipado, cadeado, gema) vira um
  selo no canto superior esquerdo do card.
- O nome vai para a direita do card e pode quebrar em várias linhas.
- Arte guardada em 256px (antes era 64px). O cache em disco é separado por
  tamanho.
- Layout ajustável em `[Layout]` no `.cfg` (`CardHeight`, `ArtWidth`,
  `BadgeScale`). `Mode = Icon` volta ao visual da v0.1.
- Ctrl+F9 relê o `.cfg` sem reiniciar o jogo. Vale ao reabrir o inventário.

## v0.1.0 — miniatura-no-icone

- Troca o ícone genérico do tipo (espada, armadura...) pela arte real do item,
  no mesmo espaço do ícone (~50px), 25% maior.
- As linhas mantêm o tamanho original do jogo.
- Cache em disco das miniaturas (64px).
- F9: dump da lista no log. Shift+F9: limpa o cache.
