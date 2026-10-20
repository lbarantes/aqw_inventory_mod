using System;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Debug;
using AQWMod.InventoryThumbs.Patches;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs
{
    // Mostra a arte de cada item direto na lista do inventário (e banco/loja),
    // sem precisar clicar para abrir o preview.
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInProcess("AdventureQuest Worlds Infinity.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        internal static BepInEx.Logging.ManualLogSource Log { get; private set; }

        private Harmony _harmony;

        private void Awake()
        {
            try
            {
                Log = Logger;
                InvConfig.Initialize(Config);

                var host = new GameObject("__AQWInventoryThumbs__");
                DontDestroyOnLoad(host);
                host.AddComponent<InventoryDumper>().Setup(Logger);

                if (!InvConfig.Enabled.Value)
                {
                    Logger.LogInfo("[InvThumbs] Miniaturas desativadas via config (dump continua em F9).");
                    return;
                }

                host.AddComponent<ThumbnailService>().Setup(Logger);
                host.AddComponent<Studio>().Setup(Logger);

                InventoryListItemPatch.Log = Logger;
                ItemListPatch.Log = Logger;
                _harmony = new Harmony(PluginInfo.GUID);

                Patch(typeof(inventoryListItem), nameof(inventoryListItem.setItemData),
                      postfix: new HarmonyMethod(typeof(InventoryListItemPatch), nameof(InventoryListItemPatch.Postfix)));

                // modo Card: lista mista com altura por item (ver MixedList)
                var attach = new HarmonyMethod(typeof(ItemListPatch), nameof(ItemListPatch.Prefix));
                Patch(typeof(ItemList), "Start", prefix: attach);
                Patch(typeof(ItemList), nameof(ItemList.AddListItemPrefabs), prefix: attach);
                Patch(typeof(ItemList), nameof(ItemList.ScaleTheList), prefix: attach,
                      postfix: new HarmonyMethod(typeof(ItemListPatch), nameof(ItemListPatch.PostfixScale)));
                Patch(typeof(inventoryListItem), "Update",
                      prefix: new HarmonyMethod(typeof(ItemListPatch), nameof(ItemListPatch.PrefixRowUpdate)));

                // checkbox "Full Body Preview" no rodapé do inventário
                Patch(typeof(UIInventory), nameof(UIInventory.OnEnable),
                      postfix: new HarmonyMethod(typeof(ItemListPatch), nameof(ItemListPatch.PostfixInventoryEnable)));

                Logger.LogInfo($"[InvThumbs] v{PluginInfo.Version} ({PluginInfo.Label}) inicializado. " +
                               $"Layout={InvConfig.Mode.Value} Dump={InvConfig.DumpKey.Value}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[InvThumbs] Falha na inicialização: {ex}");
            }
        }

        // cada patch isolado: um método que sumiu numa atualização do jogo não
        // impede os outros de aplicar
        private void Patch(Type type, string method, HarmonyMethod prefix = null, HarmonyMethod postfix = null)
        {
            try
            {
                var target = AccessTools.Method(type, method);
                if (target == null)
                {
                    Logger.LogError($"[InvThumbs] {type.Name}.{method} não encontrado — jogo atualizou?");
                    return;
                }
                _harmony.Patch(target, prefix: prefix, postfix: postfix);
            }
            catch (Exception ex)
            {
                Logger.LogError($"[InvThumbs] Falha ao aplicar patch em {type.Name}.{method}: {ex}");
            }
        }
    }

    internal static class PluginInfo
    {
        public const string GUID    = "com.aqwmod.inventorythumbs";
        public const string Name    = "AQW Inventory Thumbnails";
        public const string Version = "0.4.0";   // BepInEx exige x.y.z; a pré-versão vai no Label
        public const string Label   = "pre1-fullbody-grade";
    }
}
