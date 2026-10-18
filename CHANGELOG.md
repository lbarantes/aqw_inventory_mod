# Versões

Cada versão tem um nome descritivo, uma tag git (`v<versão>-<nome>`) e uma
cópia da DLL em `versions/`. Para trocar a instalada (com o jogo fechado):

```powershell
.\tools\use-version.ps1 v0.1.0
```

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
