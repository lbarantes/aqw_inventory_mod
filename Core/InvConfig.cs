using BepInEx.Configuration;

namespace AQWMod.InventoryThumbs.Core
{
    internal static class InvConfig
    {
        internal static ConfigEntry<bool>   Enabled        { get; private set; }
        internal static ConfigEntry<int>    MaxConcurrent  { get; private set; }
        internal static ConfigEntry<int>    ThumbnailSize  { get; private set; }
        internal static ConfigEntry<float>  IconScale      { get; private set; }
        internal static ConfigEntry<bool>   DiskCache      { get; private set; }
        internal static ConfigEntry<float>  LoadTimeout    { get; private set; }

        internal static ConfigEntry<bool>   ShowWeapons    { get; private set; }
        internal static ConfigEntry<bool>   ShowCapes      { get; private set; }
        internal static ConfigEntry<bool>   ShowHelms      { get; private set; }
        internal static ConfigEntry<bool>   ShowArmors     { get; private set; }
        internal static ConfigEntry<bool>   ShowPets       { get; private set; }
        internal static ConfigEntry<bool>   ShowHouse      { get; private set; }
        internal static ConfigEntry<bool>   ShowOthers     { get; private set; }

        internal static ConfigEntry<string> DumpKey        { get; private set; }
        internal static ConfigEntry<bool>   VerboseLog     { get; private set; }

        internal static void Initialize(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Liga/desliga as miniaturas na lista do inventário.");
            MaxConcurrent = cfg.Bind("General", "MaxConcurrentLoads", 3,
                "Quantos asset bundles podem ser baixados ao mesmo tempo.");
            ThumbnailSize = cfg.Bind("General", "ThumbnailSize", 64,
                "Lado máximo (px) da miniatura guardada em memória/disco.");
            IconScale = cfg.Bind("General", "IconScale", 1.25f,
                "Tamanho da miniatura em relação ao ícone original da linha.");
            DiskCache = cfg.Bind("General", "DiskCache", true,
                "Salva as miniaturas em BepInEx/cache/inventory_thumbs para abrir instantâneo depois.");
            LoadTimeout = cfg.Bind("General", "LoadTimeoutSeconds", 30f,
                "Desiste de um bundle que não terminou de carregar nesse tempo.");

            ShowWeapons = cfg.Bind("Types", "Weapons", true, "Armas.");
            ShowCapes   = cfg.Bind("Types", "Capes",   true, "Capas.");
            ShowHelms   = cfg.Bind("Types", "Helms",   true, "Capacetes.");
            ShowArmors  = cfg.Bind("Types", "Armors",  true,
                "Armaduras/classes (experimental: usa a maior peça do bundle, não o personagem montado).");
            ShowPets    = cfg.Bind("Types", "Pets",    true, "Pets.");
            ShowHouse   = cfg.Bind("Types", "House",   true, "Casas e itens de casa.");
            ShowOthers  = cfg.Bind("Types", "Others",  true, "Demais itens que tenham arte.");

            DumpKey = cfg.Bind("Debug", "DumpKey", "F9",
                "Dump da hierarquia do inventário no log. Shift+tecla limpa o cache de miniaturas.");
            VerboseLog = cfg.Bind("Debug", "VerboseLog", false,
                "Loga cada miniatura carregada/falhada.");
        }
    }
}
