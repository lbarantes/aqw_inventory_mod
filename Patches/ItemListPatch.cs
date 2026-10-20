using System;
using BepInEx.Logging;
using AQWMod.InventoryThumbs.Cards;

namespace AQWMod.InventoryThumbs.Patches
{
    // Modo Card (lista mista, ver MixedList):
    //   - prefixo em ItemList.Start / ScaleTheList / AddListItemPrefabs: cola o
    //     MixedList na lista antes de ela ser montada;
    //   - postfix em ScaleTheList: recalcula alturas e altura do conteúdo
    //     (o jogo acabou de somar fabHeight por item);
    //   - prefixo em inventoryListItem.Update: desliga a reciclagem de altura
    //     fixa do jogo nas listas gerenciadas (o MixedList faz no lugar).
    internal static class ItemListPatch
    {
        internal static ManualLogSource Log;
        private static bool _errorLogged;

        internal static void Prefix(ItemList __instance)
        {
            try { CardLayout.Prepare(__instance); }
            catch (Exception ex) { LogOnce(ex); }
        }

        internal static void PostfixScale(ItemList __instance)
        {
            try { CardLayout.Prepare(__instance)?.Rebuild(); }
            catch (Exception ex) { LogOnce(ex); }
        }

        internal static bool PrefixRowUpdate(inventoryListItem __instance)
        {
            try { return CardLayout.Get(__instance.il) == null; }
            catch { return true; }
        }

        internal static void PostfixInventoryEnable(UIInventory __instance)
        {
            try { FullBodyToggle.Ensure(__instance); }
            catch (Exception ex) { LogOnce(ex); }
        }

        private static void LogOnce(Exception ex)
        {
            if (_errorLogged) return;
            _errorLogged = true;
            Log?.LogError($"[InvThumbs] Erro no patch de ItemList: {ex}");
        }
    }
}
