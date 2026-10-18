using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Thumbnails
{
    // ──────────────────────────────────────────────────────────────────────────
    // Componente colado em cada linha (inventoryListItem) da lista.
    //
    // Cria uma Image "__InvThumb__" como filha do ícone genérico da linha,
    // esticada sobre ele. Quando a miniatura chega, desliga só o componente
    // Image do ícone (a filha continua renderizando) e liga a miniatura.
    // Nada do prefab do jogo é alterado além do enabled do ícone, que volta
    // a true sempre que a linha é reciclada para outro item.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class ThumbRow : MonoBehaviour
    {
        private static readonly HashSet<ThumbRow> All = new HashSet<ThumbRow>();

        private inventoryListItem _row;
        private Image _thumb;

        internal string CurrentKey { get; private set; }

        private void OnDestroy() => All.Remove(this);

        // chamado no postfix de inventoryListItem.setItemData
        internal void Bind(inventoryListItem row)
        {
            _row = row;
            All.Add(this);
            Hide();

            var key = ThumbnailService.KeyFor(row.item);
            CurrentKey = key;
            if (key == null || ThumbnailService.Instance == null) return;
            if (!EnsureThumb()) return;

            ThumbnailService.Instance.Request(this, row.item, key);
        }

        internal void Show(Sprite sprite)
        {
            if (sprite == null || _row == null || _row.icon == null || !EnsureThumb()) return;

            _thumb.sprite  = sprite;
            _thumb.color   = IsLocked() ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            _thumb.enabled = true;
            _row.icon.enabled = false;
        }

        internal void Hide()
        {
            if (_thumb != null) _thumb.enabled = false;
            if (_row != null && _row.icon != null) _row.icon.enabled = true;
        }

        internal string Describe()
        {
            var shown = _thumb != null && _thumb.enabled;
            return $"thumb={(shown ? "visível" : "oculta")} key={CurrentKey ?? "-"}";
        }

        private bool IsLocked() => _row.item is ShopItem si && si.Locked;

        private bool EnsureThumb()
        {
            if (_thumb != null) return true;
            if (_row == null || _row.icon == null) return false;

            var go = new GameObject("__InvThumb__", typeof(RectTransform));
            go.transform.SetParent(_row.icon.transform, false);

            var rt = (RectTransform)go.transform;
            rt.anchorMin  = Vector2.zero;
            rt.anchorMax  = Vector2.one;
            rt.pivot      = new Vector2(0.5f, 0.5f);
            rt.offsetMin  = Vector2.zero;
            rt.offsetMax  = Vector2.zero;
            rt.localScale = Vector3.one * Mathf.Clamp(InvConfig.IconScale.Value, 0.25f, 4f);

            _thumb = go.AddComponent<Image>();
            _thumb.preserveAspect = true;
            _thumb.raycastTarget  = false;
            _thumb.enabled        = false;
            return true;
        }

        // ── Operações em todas as linhas vivas ────────────────────────────────

        internal static void HideAll()
        {
            foreach (var r in All) if (r != null) r.Hide();
        }

        internal static void RebindAll()
        {
            foreach (var r in new List<ThumbRow>(All))
                if (r != null && r._row != null && r._row.item != null) r.Bind(r._row);
        }
    }
}
