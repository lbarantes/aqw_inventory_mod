using System.Collections.Generic;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Liga o modo Card nas listas reconhecidas: cola um MixedList na ItemList,
    // que passa a posicionar as linhas com altura por item (card para quem
    // tem arte, linha original para o resto). Ver MixedList.
    //
    // Chamado por prefixos em ItemList.Start, ScaleTheList e
    // AddListItemPrefabs — o que vier primeiro — e por um postfix em
    // ScaleTheList, que recalcula as alturas depois que o jogo monta a lista.
    // ──────────────────────────────────────────────────────────────────────────

    internal static class CardLayout
    {
        private static readonly Dictionary<int, MixedList> Managed = new Dictionary<int, MixedList>();
        private static readonly HashSet<int> Rejected = new HashSet<int>();

        internal static MixedList Prepare(ItemList list)
        {
            if (list == null || !InvConfig.Enabled.Value || !InvConfig.CardMode) return null;

            int id = list.GetInstanceID();
            if (Managed.TryGetValue(id, out var mixed) && mixed != null) return mixed;
            if (Rejected.Contains(id)) return null;
            if (!Eligible(list)) { Rejected.Add(id); return null; }

            mixed = list.gameObject.AddComponent<MixedList>();
            mixed.Setup(list);
            Managed[id] = mixed;
            return mixed;
        }

        // só listas com o prefab de linha conhecido (inventário/banco/loja/loot)
        private static bool Eligible(ItemList list)
        {
            var prefab = list.listItemPrefab;
            return prefab != null
                && prefab.GetComponent<inventoryListItem>() != null
                && prefab.transform.Find(CardRow.ContainerName) != null;
        }

        internal static MixedList Get(ItemList list) =>
            list != null && Managed.TryGetValue(list.GetInstanceID(), out var m) && m != null ? m : null;
    }
}
