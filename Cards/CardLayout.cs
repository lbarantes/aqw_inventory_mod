using System.Collections.Generic;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Decide, a cada vez que a lista é montada, se as linhas viram card.
    //
    // A ItemList do jogo é virtualizada com altura fixa: fabHeight (serializado
    // no prefab, ~72.9) e listItemCount linhas recicladas. Tudo que posiciona
    // linhas lê fabHeight — AddListItemPrefabs (posição inicial), ScaleTheList
    // (altura do conteúdo), inventoryListItem.Update/JumpMultiplier (reciclagem)
    // e os ScrollToGamepadItem. Então basta trocar fabHeight ANTES do primeiro
    // uso: prefixos em Start, ScaleTheList e AddListItemPrefabs garantem isso
    // seja qual for a ordem em que o jogo chama.
    //
    // Trocar de aba (Todas/Itens/Gems/Classe/House/Misc ou as abas verticais)
    // passa sempre por ScaleTheList, que destrói e recria as linhas — então a
    // decisão vale por aba. As abas de cima (Misc/House/Gems) só trocam o
    // conteúdo da lista (UIInventory.Button_Show*), sem mudar a TabType da
    // ItemList; por isso a decisão é pelo CONTEÚDO: se pelo menos CardMinRatio
    // dos itens tem arte, card; senão fica a lista original do jogo.
    //
    // listItemCount fica igual: com linhas mais altas as mesmas N linhas cobrem
    // uma área maior que a janela, o que só deixa sobra (nunca buraco).
    //
    // A linha em si é ajustada depois, na primeira vez que recebe um item
    // (CardRow.Apply, chamado pelo ThumbRow).
    // ──────────────────────────────────────────────────────────────────────────

    internal static class CardLayout
    {
        // instanceID da ItemList → fabHeight original (listas reconhecidas)
        private static readonly Dictionary<int, float> OriginalHeight = new Dictionary<int, float>();
        private static readonly HashSet<int> Rejected = new HashSet<int>();

        internal static void Prepare(ItemList list)
        {
            if (list == null || !InvConfig.Enabled.Value || !InvConfig.CardMode) return;

            int id = list.GetInstanceID();
            if (!OriginalHeight.TryGetValue(id, out var orig))
            {
                if (Rejected.Contains(id)) return;
                if (!Eligible(list)) { Rejected.Add(id); return; }
                orig = list.fabHeight;
                OriginalHeight[id] = orig;
            }

            float card = Mathf.Clamp(InvConfig.CardHeight.Value, 40f, 400f);
            list.fabHeight = WantsCards(list) ? Mathf.Max(orig, card) : orig;
        }

        // só listas com o prefab de linha conhecido (inventário/banco/loja/loot)
        private static bool Eligible(ItemList list)
        {
            var prefab = list.listItemPrefab;
            return prefab != null
                && prefab.GetComponent<inventoryListItem>() != null
                && prefab.transform.Find(CardRow.ContainerName) != null;
        }

        private static bool WantsCards(ItemList list)
        {
            var items = list.itemList;
            if (items == null || items.Count == 0) return false;

            int withArt = 0;
            foreach (var it in items)
                if (ThumbnailService.KeyFor(it) != null) withArt++;
            return withArt >= items.Count * Mathf.Clamp01(InvConfig.CardMinRatio.Value);
        }

        // lista gerenciada pelo modo Card (em modo card OU lista original)
        internal static bool IsManaged(ItemList list) =>
            list != null && OriginalHeight.ContainsKey(list.GetInstanceID());

        // true só quando a lista está, agora, em modo card
        internal static bool TryGetHeights(inventoryListItem row, out float oldH, out float newH)
        {
            oldH = newH = 0f;
            var list = row != null ? row.il : null;
            if (list == null || !OriginalHeight.TryGetValue(list.GetInstanceID(), out oldH)) return false;
            newH = list.fabHeight;
            return newH > oldH + 0.5f;
        }
    }
}
