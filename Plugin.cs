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
        private Harmony _harmony;

        private void Awake()
        {
            try
            {
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

                InventoryListItemPatch.Log = Logger;
                _harmony = new Harmony(PluginInfo.GUID);
                var target = AccessTools.Method(typeof(inventoryListItem), nameof(inventoryListItem.setItemData));
                if (target == null)
                {
                    Logger.LogError("[InvThumbs] inventoryListItem.setItemData não encontrado — jogo atualizou?");
                    return;
                }
                _harmony.Patch(target, postfix: new HarmonyMethod(typeof(InventoryListItemPatch),
                                                                   nameof(InventoryListItemPatch.Postfix)));

                Logger.LogInfo($"[InvThumbs] v{PluginInfo.Version} inicializado. Dump={InvConfig.DumpKey.Value}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"[InvThumbs] Falha na inicialização: {ex}");
            }
        }
    }

    internal static class PluginInfo
    {
        public const string GUID    = "com.aqwmod.inventorythumbs";
        public const string Name    = "AQW Inventory Thumbnails";
        public const string Version = "0.1.0";
    }
}
