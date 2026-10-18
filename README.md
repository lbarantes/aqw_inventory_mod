# AQW Inventory Thumbnails

Plugin BepInEx que mostra a arte de cada item direto na lista do inventário
(e banco/loja/loot), no lugar do ícone genérico do tipo. Não é preciso clicar
no item para ver o preview.

## Instalação

`dotnet build -c Release` compila e copia `AQWInventoryThumbs.dll` para
`BepInEx/plugins/`. Também fica uma cópia em `dist/`.

## Como funciona

- Um patch Harmony (postfix) em `inventoryListItem.setItemData` roda sempre
  que uma linha recebe um item. A lista do jogo é virtualizada e recicla as
  linhas no scroll, então só as linhas visíveis pedem miniatura.
- A arte é carregada com o próprio `AssetBundleLoader` do jogo, que usa o
  mesmo cache e o mesmo refCount do painel de preview. O sprite é procurado
  na ordem: `WeaponSlots.weapon`, `CapeSlots.cape`, a maior peça de
  `ItemSlots`, o maior `SpriteRenderer` e, por último, o maior `Sprite` do
  bundle.
- O sprite é copiado para uma textura pequena (`ThumbnailSize`) e salvo em
  `BepInEx/cache/inventory_thumbs/`. Da segunda vez, abre na hora.
- Itens sem arte, ou cuja arte falhou, mantêm o ícone genérico.

## Teclas

- **F9**: grava no log a hierarquia da linha, os tamanhos e o estado da
  miniatura de cada item visível. Para achar, pesquise `[InvDump]` em
  `BepInEx/LogOutput.log`.
- **Shift+F9**: limpa o cache de miniaturas (memória e disco) e recarrega.
- **Ctrl+F9**: relê o `.cfg`. Feche e reabra o inventário para aplicar.

## Layout Card

No modo `Card` (padrão desde a v0.2), um prefixo em `ItemList.Start`,
`ScaleTheList` e `AddListItemPrefabs` aumenta `ItemList.fabHeight`, a altura
fixa que a lista virtualizada usa para posicionar e reciclar as linhas.
Depois, cada linha é remontada como card (`Cards/CardRow.cs`): quadro de arte
à esquerda, ícone do jogo como selo e nome à direita.

## Versões

O histórico está em `CHANGELOG.md`, e cada versão tem uma tag git. Para trocar
a DLL instalada, com o jogo fechado:

```powershell
.\tools\use-version.ps1            # lista
.\tools\use-version.ps1 v0.1.0     # instala
```

## Configuração

`BepInEx/config/com.aqwmod.inventorythumbs.cfg` é criado na primeira
execução. Ali dá para ajustar o tamanho (`IconScale`), o número de downloads
simultâneos, ligar e desligar por tipo de item e ativar `VerboseLog`.

## Limitações conhecidas

- O jogo mostra armaduras e classes montadas no personagem. Aqui aparece só a
  maior peça do bundle (experimental).
- A altura da linha é a do jogo, então a miniatura fica no espaço do ícone. O
  dump da F9 traz os tamanhos para, se for o caso, aumentar a linha
  (`ItemList.fabHeight`).
