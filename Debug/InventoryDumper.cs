using System;
using System.Text;
using BepInEx.Logging;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs.Debug
{
    // ──────────────────────────────────────────────────────────────────────────
    // F9        → dump no log da lista aberta: ItemList (altura das linhas,
    //             quantidade de linhas recicladas), hierarquia completa de UMA
    //             linha com tamanhos de RectTransform e componentes, e o estado
    //             da miniatura de cada linha visível.
    // Shift+F9  → limpa o cache de miniaturas (memória + disco) e recarrega.
    //
    // Serve para calibrar o layout (ex.: aumentar a altura da linha) com base
    // nos tamanhos reais do prefab, e para entender por que um item não ganhou
    // miniatura. Pesquise [InvDump] em BepInEx/LogOutput.log.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class InventoryDumper : MonoBehaviour
    {
        private ManualLogSource _log;
        private KeyCode _key;

        internal void Setup(ManualLogSource log)
        {
            _log = log;
            var name = InvConfig.DumpKey.Value;
            if (!Enum.TryParse(name, true, out _key))
            {
                _key = KeyCode.F9;
                _log.LogWarning($"[InvDump] Tecla '{name}' inválida — usando F9.");
            }
        }

        private void Update()
        {
            if (!Input.GetKeyDown(_key)) return;
            try
            {
                if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                    ThumbnailService.Instance?.ClearAll();
                else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                {
                    // vale para as próximas listas criadas: feche e reabra o inventário
                    InvConfig.File.Reload();
                    _log.LogInfo($"[InvDump] Config relida: Layout={InvConfig.Mode.Value} " +
                                 $"CardHeight={InvConfig.CardHeight.Value} ArtWidth={InvConfig.ArtWidth.Value} " +
                                 "— feche e reabra o inventário.");
                }
                else
                    Dump();
            }
            catch (Exception ex)
            {
                _log.LogError($"[InvDump] Erro: {ex}");
            }
        }

        private void Dump()
        {
            var sb = new StringBuilder();
            sb.AppendLine("[InvDump] ════════════════ DUMP DO INVENTÁRIO ════════════════");

            var svc = ThumbnailService.Instance;
            if (svc != null)
                sb.AppendLine($"[InvDump] Miniaturas: prontas={svc.CountReady} disco={svc.CountDisk} falhas={svc.CountFailed}");

            var rows = FindObjectsByType<inventoryListItem>(FindObjectsSortMode.None);
            sb.AppendLine($"[InvDump] Linhas (inventoryListItem) ativas na cena: {rows.Length}");
            if (rows.Length == 0)
            {
                sb.AppendLine("[InvDump] Abra o inventário (ou banco/loja) e aperte de novo.");
                _log.LogInfo(sb.ToString());
                return;
            }

            // agrupar por ItemList (inventário, banco e loja podem estar abertos juntos)
            foreach (var list in FindObjectsByType<ItemList>(FindObjectsSortMode.None))
            {
                sb.AppendLine($"[InvDump] ItemList '{Path(list.transform)}' fabHeight={list.fabHeight} " +
                              $"listItemCount={list.listItemCount} linhasPrefab={list.listItems?.Count ?? 0} " +
                              $"itens={list.itemList?.Count ?? 0}");
            }

            sb.AppendLine("[InvDump] ── Hierarquia da primeira linha ──");
            DumpHierarchy(rows[0].transform, sb, 0);

            sb.AppendLine("[InvDump] ── Linhas visíveis ──");
            foreach (var r in rows)
            {
                var it = r.item;
                if (it == null || !r.gameObject.activeInHierarchy) continue;
                var tr  = r.GetComponent<ThumbRow>();
                var key = ThumbnailService.KeyFor(it);
                sb.AppendLine($"[InvDump]  '{it.Name}' type={it.ItemType} spot={it.EquipSpot} " +
                              $"bundle={(it.Bundle != null ? it.Bundle.Filename : "-")} prefab={it.PrefabName ?? "-"} " +
                              $"→ {svc?.DescribeKey(key)} | {tr?.Describe() ?? "sem ThumbRow"}");
            }

            sb.AppendLine("[InvDump] ════════════════ FIM ════════════════");
            _log.LogInfo(sb.ToString());
        }

        private static void DumpHierarchy(Transform t, StringBuilder sb, int depth)
        {
            if (depth > 8) return;
            var indent = new string(' ', depth * 2);
            var rt = t as RectTransform;
            var geo = rt != null
                ? $"size={rt.rect.width:0.#}x{rt.rect.height:0.#} pos={rt.anchoredPosition} anchors={rt.anchorMin}-{rt.anchorMax} scale={rt.localScale}"
                : "";

            var comps = new StringBuilder();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                if (comps.Length > 0) comps.Append(", ");
                comps.Append(c.GetType().Name);
                if (c is UnityEngine.UI.Image img && img.sprite != null) comps.Append($"[{img.sprite.name}]");
            }

            sb.AppendLine($"[InvDump] {indent}{t.name}{(t.gameObject.activeSelf ? "" : " (off)")} {geo} {{{comps}}}");
            for (int i = 0; i < t.childCount; i++) DumpHierarchy(t.GetChild(i), sb, depth + 1);
        }

        private static string Path(Transform t)
        {
            var s = t.name;
            for (var p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
            return s;
        }
    }
}
