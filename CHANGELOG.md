# Versões

Cada versão tem um nome descritivo, uma tag git (`v<versão>-<nome>`) e uma
cópia da DLL em `versions/`. Para trocar a instalada (com o jogo fechado):

```powershell
.\tools\use-version.ps1 v0.1.0
```

## v0.1.0 — miniatura-no-icone

- Troca o ícone genérico do tipo (espada, armadura...) pela arte real do item,
  no mesmo espaço do ícone (~50px), 25% maior.
- As linhas mantêm o tamanho original do jogo.
- Cache em disco das miniaturas (64px).
- F9: dump da lista no log. Shift+F9: limpa o cache.
