using System;
using BepInEx.Logging;
using AQWMod.InventoryThumbs.Cards;

namespace AQWMod.InventoryThumbs.Patches
{
    // Prefixo comum para ItemList.Start, ScaleTheList e AddListItemPrefabs:
    // troca fabHeight pela altura do card antes que o jogo use o valor.
    internal static class ItemListPatch
    {
        internal static ManualLogSource Log;
        private static bool _errorLogged;

        internal static void Prefix(ItemList __instance)
        {
            try
            {
                CardLayout.Prepare(__instance);
            }
            catch (Exception ex)
            {
                if (_errorLogged) return;
                _errorLogged = true;
                Log?.LogError($"[InvThumbs] Erro no patch de ItemList: {ex}");
            }
        }
    }
}
