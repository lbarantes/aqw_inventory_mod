using BepInEx.Configuration;

namespace AQWMod.InventoryThumbs.Core
{
    internal enum LayoutMode
    {
        Card,   // linha mais alta, arte grande à esquerda (v0.2+)
        Icon,   // arte no lugar do ícone pequeno, linha original (v0.1)
    }

    internal static class InvConfig
    {
        internal static ConfigFile File { get; private set; }

        internal static ConfigEntry<bool>   Enabled        { get; private set; }
        internal static ConfigEntry<int>    MaxConcurrent  { get; private set; }
        internal static ConfigEntry<int>    TextureSize    { get; private set; }
        internal static ConfigEntry<bool>   DiskCache      { get; private set; }
        internal static ConfigEntry<float>  LoadTimeout    { get; private set; }

        internal static ConfigEntry<LayoutMode> Mode       { get; private set; }
        internal static ConfigEntry<float>  CardHeight     { get; private set; }
        internal static ConfigEntry<float>  ArtWidth       { get; private set; }
        internal static ConfigEntry<float>  BadgeScale     { get; private set; }
        internal static ConfigEntry<float>  IconScale      { get; private set; }
        internal static ConfigEntry<float>  HelmFrame      { get; private set; }
        internal static ConfigEntry<float>  HelmOffsetY    { get; private set; }
        internal static ConfigEntry<bool>   FullBody       { get; private set; }
        internal static ConfigEntry<int>    FullBodyColumns { get; private set; }
        internal static ConfigEntry<float>  FullBodyHeight { get; private set; }
        internal static ConfigEntry<float>  FullBodyZoom   { get; private set; }

        internal static ConfigEntry<bool>   ShowWeapons    { get; private set; }
        internal static ConfigEntry<bool>   ShowCapes      { get; private set; }
        internal static ConfigEntry<bool>   ShowHelms      { get; private set; }
        internal static ConfigEntry<bool>   ShowArmors     { get; private set; }
        internal static ConfigEntry<bool>   ShowPets       { get; private set; }
        internal static ConfigEntry<bool>   HouseInCards   { get; private set; }
        internal static ConfigEntry<bool>   ShowOthers     { get; private set; }

        internal static ConfigEntry<string> DumpKey        { get; private set; }
        internal static ConfigEntry<bool>   VerboseLog     { get; private set; }

        internal static bool CardMode => Mode.Value == LayoutMode.Card;
        internal static bool FullBodyMode => CardMode && FullBody.Value;

        internal static void Initialize(ConfigFile cfg)
        {
            File = cfg;

            Enabled = cfg.Bind("General", "Enabled", true,
                "Liga/desliga a arte dos itens na lista do inventário.");
            MaxConcurrent = cfg.Bind("General", "MaxConcurrentLoads", 3,
                "Quantos asset bundles podem ser baixados ao mesmo tempo.");
            TextureSize = cfg.Bind("General", "TextureSize", 256,
                "Lado máximo (px) da arte guardada em memória/disco. Maior = mais nítido e mais memória.");
            DiskCache = cfg.Bind("General", "DiskCache", true,
                "Salva a arte em BepInEx/cache/inventory_thumbs para abrir instantâneo depois.");
            LoadTimeout = cfg.Bind("General", "LoadTimeoutSeconds", 30f,
                "Desiste de um bundle que não terminou de carregar nesse tempo.");

            Mode = cfg.Bind("Layout", "Mode", LayoutMode.Card,
                "Card = item com arte vira card (linha alta, arte grande) e o resto fica como linha original; Icon = arte no lugar do ícone pequeno.");
            CardHeight = cfg.Bind("Layout", "CardHeight", 150f,
                "Altura das linhas com arte no modo Card (a original do jogo é ~73). Itens sem arte mantêm a altura original.");
            ArtWidth = cfg.Bind("Layout", "ArtWidth", 200f,
                "Largura do quadro da arte no modo Card (limitada a 45% da linha).");
            BadgeScale = cfg.Bind("Layout", "BadgeScale", 0.7f,
                "Escala do ícone do jogo (tipo/raridade/equipado) que vira selo no canto do card.");
            HelmFrame = cfg.Bind("Layout", "HelmFrame", 0.55f,
                "Capacetes: altura do quadro em fração da altura do corpo. Igual para todos (mesma escala). Maior = mais afastado.");
            FullBody = cfg.Bind("FullBody", "Enabled", false,
                "Full Body Preview (checkbox no rodapé do inventário): armas, capacetes, armaduras, classes, " +
                "capas e pets viram cards verticais com o seu conjunto atual vestindo o item.");
            FullBodyColumns = cfg.Bind("FullBody", "Columns", 3,
                new ConfigDescription("Cards por fileira no Full Body Preview.", new AcceptableValueRange<int>(1, 4)));
            FullBodyHeight = cfg.Bind("FullBody", "CardHeight", 280f,
                "Altura de cada card vertical do Full Body Preview.");
            FullBodyZoom = cfg.Bind("FullBody", "Frame", 1.35f,
                "Altura do quadro em relação à altura do personagem (pés à cabeça). Igual para todos os cards: " +
                "maior = personagem menor, com mais espaço para armas grandes (que podem sair cortadas).");
            HelmOffsetY = cfg.Bind("Layout", "HelmOffsetY", 0.1f,
                "Capacetes: desloca o centro do quadro para cima (fração do quadro) a partir da cabeça.");
            IconScale = cfg.Bind("Layout", "IconScale", 1.25f,
                "Modo Icon: tamanho da arte em relação ao ícone original.");

            ShowWeapons = cfg.Bind("Types", "Weapons", true, "Armas.");
            ShowCapes   = cfg.Bind("Types", "Capes",   true, "Capas.");
            ShowHelms   = cfg.Bind("Types", "Helms",   true, "Capacetes.");
            ShowArmors  = cfg.Bind("Types", "Armors",  true,
                "Armaduras/classes (experimental: usa a maior peça do bundle, não o personagem montado).");
            ShowPets    = cfg.Bind("Types", "Pets",    true, "Pets.");
            HouseInCards = cfg.Bind("Types", "HouseInCards", false,
                "Casas e itens de casa como card. Desligado: a aba House fica na lista original.");
            ShowOthers  = cfg.Bind("Types", "Others",  true, "Demais itens que tenham arte.");

            DumpKey = cfg.Bind("Debug", "DumpKey", "F9",
                "F9 = dump no log; Shift+F9 = limpa o cache; Ctrl+F9 = relê este arquivo (reabra o inventário).");
            VerboseLog = cfg.Bind("Debug", "VerboseLog", false,
                "Loga cada arte carregada/falhada.");
        }
    }
}
