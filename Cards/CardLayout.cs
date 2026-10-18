using System.Collections.Generic;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Deixa as linhas da lista mais altas para caber um card com arte grande.
    //
    // A ItemList do jogo é virtualizada com altura fixa: fabHeight (serializado
    // no prefab, ~72.9) e listItemCount linhas recicladas. Tudo que posiciona
    // linhas lê fabHeight — AddListItemPrefabs (posição inicial), ScaleTheList
    // (altura do conteúdo), inventoryListItem.Update/JumpMultiplier (reciclagem)
    // e os ScrollToGamepadItem. Então basta trocar fabHeight ANTES do primeiro
    // uso: prefixos em Start, ScaleTheList e AddListItemPrefabs garantem isso
    // seja qual for a ordem em que o jogo chama.
    //
    // listItemCount fica igual: com linhas mais altas as mesmas N linhas cobrem
    // uma área maior que a janela, o que só deixa sobra (nunca buraco).
    //
    // A linha em si é ajustada depois, na primeira vez que recebe um item
    // (CardRow.Apply, chamado pelo ThumbRow).
    // ──────────────────────────────────────────────────────────────────────────

    internal static class CardLayout
    {
        // instanceID da ItemList → fabHeight original (só listas escaladas)
        private static readonly Dictionary<int, float> OriginalHeight = new Dictionary<int, float>();
        private static readonly HashSet<int> Seen = new HashSet<int>();

        internal static void EnsureScaled(ItemList list)
        {
            if (list == null || !InvConfig.Enabled.Value || !InvConfig.CardMode) return;

            int id = list.GetInstanceID();
            if (!Seen.Add(id)) return;
            if (!Eligible(list)) return;

            float target = Mathf.Clamp(InvConfig.CardHeight.Value, 40f, 400f);
            if (target <= list.fabHeight) return;

            OriginalHeight[id] = list.fabHeight;
            list.fabHeight = target;
        }

        // só listas com o prefab de linha conhecido (inventário/banco/loja/loot)
        private static bool Eligible(ItemList list)
        {
            var prefab = list.listItemPrefab;
            return prefab != null
                && prefab.GetComponent<inventoryListItem>() != null
                && prefab.transform.Find(CardRow.ContainerName) != null;
        }

        internal static bool TryGetHeights(inventoryListItem row, out float oldH, out float newH)
        {
            oldH = newH = 0f;
            var list = row != null ? row.il : null;
            if (list == null || !OriginalHeight.TryGetValue(list.GetInstanceID(), out oldH)) return false;
            newH = list.fabHeight;
            return true;
        }
    }
}
