using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.InventoryThumbs.Cards;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Thumbnails
{
    // ──────────────────────────────────────────────────────────────────────────
    // Componente colado em cada linha (inventoryListItem) da lista. Liga a
    // linha ao ThumbnailService e decide onde a arte aparece:
    //
    //   Modo Card → lista mista (MixedList): a linha vira card (CardRow) se o
    //               item tem arte, e volta a ser a linha original se não tem.
    //   Modo Icon → uma Image "__InvThumb__" filha do ícone genérico, esticada
    //               sobre ele; o Image do ícone é desligado enquanto a arte
    //               aparece e religado quando a linha é reciclada.
    //
    // Se a lista não foi escalada (lista não reconhecida ou modo Icon), cai
    // no modo Icon.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class ThumbRow : MonoBehaviour
    {
        private static readonly HashSet<ThumbRow> All = new HashSet<ThumbRow>();

        private inventoryListItem _row;
        private CardRow _card;
        private Image _thumb;

        internal string CurrentKey { get; private set; }

        private void OnDestroy() => All.Remove(this);

        // chamado no postfix de inventoryListItem.setItemData
        internal void Bind(inventoryListItem row)
        {
            _row = row;
            All.Add(this);

            var key   = ThumbnailService.KeyFor(row.item);
            var mixed = CardLayout.Get(row.il);

            if (mixed != null)
            {
                // lista mista: o layout sai do mesmo critério que o MixedList
                // usou para o tamanho desta posição (ThumbnailService.KindFor)
                EnsureCard();
                var kind = ThumbnailService.KindFor(row.item);
                _card.SetKind(kind, mixed.PlainHeight, MixedList.CardHeight, mixed.TallWidth, MixedList.TallHeight);
                if (kind == ThumbnailService.Kind.Plain) key = null;
                Hide();
                CurrentKey = key;
                if (key != null && ThumbnailService.Instance != null)
                    ThumbnailService.Instance.Request(this, row.item, key);
                return;
            }

            Hide();
            CurrentKey = null;

            // modo Card, mas lista não reconhecida: não mexe
            if (InvConfig.CardMode) return;

            // modo Icon (v0.1): arte no lugar do ícone pequeno
            CurrentKey = key;
            if (key == null || ThumbnailService.Instance == null || !EnsureThumb()) return;
            ThumbnailService.Instance.Request(this, row.item, key);
        }

        private void EnsureCard()
        {
            if (_card != null) return;
            _card = GetComponent<CardRow>();
            if (_card == null) _card = gameObject.AddComponent<CardRow>();
            _card.Build(_row);
        }

        internal void Show(Sprite sprite)
        {
            if (sprite == null || _row == null) return;

            if (_card != null && _card.IsCard)
            {
                _card.ShowArt(sprite, IsLocked());
                return;
            }

            if (_row.icon == null || !EnsureThumb()) return;
            _thumb.sprite  = sprite;
            _thumb.color   = IsLocked() ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            _thumb.enabled = true;
            _row.icon.enabled = false;
        }

        internal void Hide()
        {
            if (_card != null && _row != null) _card.ResetFor(_row);
            if (_thumb != null) _thumb.enabled = false;
            if (_row != null && _row.icon != null) _row.icon.enabled = true;
        }

        internal string Describe()
        {
            bool shown = _card != null ? _card.ArtVisible : _thumb != null && _thumb.enabled;
            return $"{(_card != null ? "card" : "ícone")} arte={(shown ? "visível" : "oculta")} key={CurrentKey ?? "-"}";
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
