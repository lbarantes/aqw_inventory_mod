# AQW Inventory Thumbnails

Plugin BepInEx que mostra a arte de cada item direto na lista do inventário
(e banco/loja/loot), no lugar do ícone genérico do tipo. Não é preciso clicar
no item para ver o preview.

## Instalação

Baixe o **AQWInventoryThumbs-Setup.exe** do [último release](https://github.com/lbarantes/aqw_inventory_mod/releases/latest)
e execute. Ele acha a pasta do jogo pela Steam, instala o BepInEx 5.4.23.5 se
faltar (se você já usa outro mod com BepInEx, nada muda nele) e coloca o mod em
`BepInEx\plugins`. Para atualizar, rode o instalador da versão nova.

Instalação manual: extraia o **AQWInventoryThumbs.zip** do release na pasta do
jogo, a que tem o `AdventureQuest Worlds Infinity.exe`.

## Desenvolvimento

`dotnet build -c Release` (no PC com o jogo, ao lado das pastas do jogo)
compila e copia `AQWInventoryThumbs.dll` para `BepInEx/plugins/`. Também ficam
cópias em `dist/` e em `versions/v<Version>-<VersionLabel>/`.

## Releases

Cada merge na `main` vira um release no GitHub (`.github/workflows/release.yml`):
tag `v<Version>-<VersionLabel>`, com instalador, .zip e DLL, e com as notas
tiradas da seção da versão no `CHANGELOG.md`. Pré-versões (`-preN`) saem
marcadas como pre-release.

O GitHub não compila o mod, porque o código depende das DLLs do jogo, que não
podem ir para o repositório. O release usa a DLL que está em `versions/`. Para
lançar uma versão:

1. Suba `Version` e `VersionLabel` no `inventory_mod.csproj`,
   `PluginInfo.Version`/`Label` no `Plugin.cs` e escreva a seção no
   `CHANGELOG.md`.
2. Rode `dotnet build -c Release` e inclua no commit a pasta nova em
   `versions/`.
3. Abra a PR. O `pr-check.yml` monta o instalador e avisa se a versão já tem
   release. Ao fazer o merge, o release é publicado.

Para gerar o mesmo pacote localmente, em `dist/release`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\build-release.ps1
```

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

No modo `Card` (padrão), a lista é mista: um item com arte vira card (quadro
de arte à esquerda, ícone do jogo como selo, nome à direita) e um item sem
arte fica na linha original do jogo. A lista do jogo só sabe trabalhar com
altura fixa, então o `Cards/MixedList.cs` substitui a reciclagem de linhas
(`inventoryListItem.Update`) por uma com altura por item. `Cards/CardRow.cs`
guarda as geometrias de cada linha e alterna entre elas.

## Full Set Preview

O checkbox **Full Set Preview** fica no rodapé do inventário. Com ele
marcado, armas, capacetes, armaduras, classes, capas e pets viram cards
verticais em grade (2 por fileira, `[FullBody] Columns`). Cada card é uma foto
de um clone do seu personagem com o conjunto equipado, só com o slot do item
trocado. No card de uma classe, a armadura é tirada, para a classe aparecer.

O quadro tem escala fixa pela altura do personagem (`[FullBody] Frame`), então
todos os cards ficam do mesmo tamanho. O pet do clone é mantido ligado e
parado no `PetAttach`. Cada combinação de conjunto fica no cache em disco, e
trocar uma peça equipada refaz os cards visíveis.

## Versões

O histórico está em `CHANGELOG.md`, e cada versão tem uma tag git. Para trocar
a DLL instalada, com o jogo fechado:

```powershell
powershell -ExecutionPolicy Bypass -File tools\use-version.ps1          # lista
powershell -ExecutionPolicy Bypass -File tools\use-version.ps1 v0.1.0   # instala
```

## Configuração

`BepInEx/config/com.aqwmod.inventorythumbs.cfg` é criado na primeira
execução. Ali dá para ajustar o tamanho (`IconScale`), o número de downloads
simultâneos, ligar e desligar por tipo de item e ativar `VerboseLog`.

## Limitações conhecidas

- Capacetes, armaduras e classes são fotografados vestidos num clone do seu
  personagem (rosto, cabelo e gênero são os seus), igual ao preview do jogo.
  Pets e itens de casa são fotografados montados (`Thumbnails/Studio.cs`).
- Gamepad: a rolagem automática até o item selecionado supõe altura fixa e
  pode parar fora do lugar nas listas mistas.
