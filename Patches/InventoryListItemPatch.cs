using System;
using BepInEx.Logging;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs.Patches
{
    // Postfix em inventoryListItem.setItemData(Item, ItemList).
    //
    // setItemData roda sempre que uma linha recebe um item: ao abrir a lista,
    // ao trocar de aba/filtro e a cada reciclagem no scroll (Update → Refresh
    // → setItemData). Vale para inventário, banco, loja e loot, que usam o
    // mesmo prefab de linha.
    internal static class InventoryListItemPatch
    {
        internal static ManualLogSource Log;
        private static bool _errorLogged;

        internal static void Postfix(inventoryListItem __instance)
        {
            try
            {
                // sem "??": o operador ignora o null "falso" dos objetos Unity
                var row = __instance.GetComponent<ThumbRow>();
                if (row == null) row = __instance.gameObject.AddComponent<ThumbRow>();
                row.Bind(__instance);
            }
            catch (Exception ex)
            {
                // nunca quebrar a lista do jogo; loga só a primeira vez
                if (_errorLogged) return;
                _errorLogged = true;
                Log?.LogError($"[InvThumbs] Erro no patch de setItemData: {ex}");
            }
        }
    }
}
