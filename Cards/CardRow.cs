using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Transforma uma linha (inventoryListItem) em card.
    //
    // Estrutura do prefab (do dump F9, linha original 540x73):
    //   InventoryListItem            ← raiz, altura = fabHeight
    //     ListItemContainer 100x100  ← âncora central (não estica): BG, Selected,
    //                                  Equipped, Content, Highlight... esticam
    //                                  dentro dele
    //       Content/ItemName         ← nome (estica em x)
    //       Right/Level              ← nível, à direita
    //       ItemIcon                 ← ícone de tipo + raridade + check equipado
    //
    // O card:
    //   - cresce a raiz e o ListItemContainer pela diferença de altura (os
    //     fundos/brilhos esticados acompanham sozinhos);
    //   - cria "__InvCard__" à esquerda: fundo escuro, borda na cor da
    //     raridade e a arte grande dentro;
    //   - move o ItemIcon do jogo para o canto do card como selo (mantém
    //     equipado/cadeado/gema funcionando);
    //   - empurra o nome para a direita do card e deixa quebrar em linhas.
    //
    // Posições são calculadas em coordenadas locais da raiz, então não
    // dependem de âncoras/pivôs de cada filho.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class CardRow : MonoBehaviour
    {
        internal const string ContainerName = "ListItemContainer";

        private const float Pad       = 8f;
        private const float Border    = 2f;
        private const float ArtInset  = 6f;
        private const float NameGap   = 14f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);

        private Image   _art;
        private Image   _placeholder;   // ícone de tipo enquanto não há arte
        private Image[] _borders;

        private RectTransform _panel;
        private Geometry _plain, _card;

        internal bool Built  { get; private set; }
        internal bool IsCard { get; private set; }

        // ── Geometria salva (para alternar card ⇄ linha original) ─────────────

        // Tudo que a montagem do card altera na linha. A mesma linha é
        // reciclada para itens com e sem arte (lista mista), então os dois
        // estados ficam guardados e SetCard só reaplica um ou outro.
        private sealed class Geometry
        {
            internal Vector2 RowSize;
            internal Vector2[] FixedChildSizes;      // filhos diretos com âncora vertical fixa
            internal Vector3 IconScale, IconPos;
            internal Vector2 NameMin, NameMax;
            internal TextWrappingModes NameWrap;
            internal TextOverflowModes NameOverflow;
        }

        private RectTransform[] _fixedChildren;
        private RectTransform _icon;
        private inventoryListItem _row;

        private Geometry Capture()
        {
            var g = new Geometry
            {
                RowSize = ((RectTransform)_row.transform).sizeDelta,
                FixedChildSizes = new Vector2[_fixedChildren.Length],
            };
            for (int i = 0; i < _fixedChildren.Length; i++) g.FixedChildSizes[i] = _fixedChildren[i].sizeDelta;
            if (_icon != null) { g.IconScale = _icon.localScale; g.IconPos = _icon.localPosition; }
            if (_row.itemName != null)
            {
                var n = _row.itemName.rectTransform;
                g.NameMin = n.offsetMin; g.NameMax = n.offsetMax;
                g.NameWrap = _row.itemName.textWrappingMode;
                g.NameOverflow = _row.itemName.overflowMode;
            }
            return g;
        }

        private void Restore(Geometry g)
        {
            ((RectTransform)_row.transform).sizeDelta = g.RowSize;
            for (int i = 0; i < _fixedChildren.Length; i++) _fixedChildren[i].sizeDelta = g.FixedChildSizes[i];
            if (_icon != null) { _icon.localScale = g.IconScale; _icon.localPosition = g.IconPos; }
            if (_row.itemName != null)
            {
                var n = _row.itemName.rectTransform;
                n.offsetMin = g.NameMin; n.offsetMax = g.NameMax;
                _row.itemName.textWrappingMode = g.NameWrap;
                _row.itemName.overflowMode = g.NameOverflow;
            }
        }

        // ── Montagem (uma vez por linha) ──────────────────────────────────────

        internal void Build(inventoryListItem row, float plainH, float cardH)
        {
            if (Built) return;
            Built = true;
            _row  = row;

            var container = row.transform.Find(ContainerName) as RectTransform;
            if (container == null) return;
            _icon = container.Find("ItemIcon") as RectTransform;

            var fixedList = new System.Collections.Generic.List<RectTransform>();
            for (int i = 0; i < row.transform.childCount; i++)
                if (row.transform.GetChild(i) is RectTransform c && Mathf.Approximately(c.anchorMin.y, c.anchorMax.y))
                    fixedList.Add(c);   // o ListItemContainer
            _fixedChildren = fixedList.ToArray();

            _plain = Capture();
            ApplyCardGeometry(row, container, plainH, cardH);
            _card = Capture();

            Restore(_plain);
            _panel.gameObject.SetActive(false);
            IsCard = false;
        }

        internal void SetCard(bool on)
        {
            if (!Built || _panel == null || on == IsCard) return;
            IsCard = on;
            Restore(on ? _card : _plain);
            _panel.gameObject.SetActive(on);
        }

        private void ApplyCardGeometry(inventoryListItem row, RectTransform container, float oldH, float newH)
        {
            var rowRt = (RectTransform)row.transform;

            float dy = newH - oldH;
            rowRt.sizeDelta += new Vector2(0f, dy);
            foreach (var c in _fixedChildren) c.sizeDelta += new Vector2(0f, dy);

            var r     = rowRt.rect;
            float artH = newH - 2f * Pad;
            float artW = Mathf.Min(InvConfig.ArtWidth.Value, r.width * 0.45f);
            float left = r.xMin + Pad;
            var center = new Vector2(left + artW / 2f, r.center.y);

            _panel = BuildPanel(container, artW, artH);
            _panel.position = rowRt.TransformPoint(center);

            MoveBadge(rowRt, container, left, r.center.y + artH / 2f);
            MoveName(row, rowRt, left + artW + NameGap, newH);
        }

        private RectTransform BuildPanel(RectTransform container, float w, float h)
        {
            var panel = NewChild("__InvCard__", container);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(w, h);

            // logo depois do Content: acima do fundo, abaixo de Highlight/Selected
            var content = container.Find("Content");
            panel.SetSiblingIndex(content != null ? content.GetSiblingIndex() + 1 : 0);

            var bg = panel.gameObject.AddComponent<Image>();
            bg.color = PanelColor;
            bg.raycastTarget = false;

            _borders = new[]
            {
                Edge(panel, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, Border)),   // topo
                Edge(panel, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, Border)),   // base
                Edge(panel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(Border, 0)),   // esquerda
                Edge(panel, new Vector2(1, 0), new Vector2(1, 1), new Vector2(Border, 0)),   // direita
            };

            var art = NewChild("__InvArt__", panel);
            art.anchorMin = Vector2.zero;
            art.anchorMax = Vector2.one;
            art.offsetMin = new Vector2(ArtInset, ArtInset);
            art.offsetMax = new Vector2(-ArtInset, -ArtInset);
            _art = art.gameObject.AddComponent<Image>();
            _art.preserveAspect = true;
            _art.raycastTarget  = false;
            _art.enabled        = false;

            var ph = NewChild("__InvPlaceholder__", panel);
            ph.anchorMin = ph.anchorMax = ph.pivot = new Vector2(0.5f, 0.5f);
            ph.sizeDelta = new Vector2(Mathf.Min(56f, w - 12f), Mathf.Min(56f, h - 12f));
            _placeholder = ph.gameObject.AddComponent<Image>();
            _placeholder.preserveAspect = true;
            _placeholder.raycastTarget  = false;
            _placeholder.color          = new Color(1f, 1f, 1f, 0.35f);
            _placeholder.enabled        = false;
            return panel;
        }

        private static Image Edge(RectTransform parent, Vector2 aMin, Vector2 aMax, Vector2 size)
        {
            var rt = NewChild("__Edge__", parent);
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot     = new Vector2(aMin.x == aMax.x ? aMin.x : 0.5f, aMin.y == aMax.y ? aMin.y : 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            var img = rt.gameObject.AddComponent<Image>();
            img.raycastTarget = false;
            return img;
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        // ícone do jogo (tipo + raridade + equipado) vira selo no canto superior esquerdo
        private static void MoveBadge(RectTransform rowRt, RectTransform container, float panelLeft, float panelTop)
        {
            var icon = container.Find("ItemIcon") as RectTransform;
            if (icon == null) return;

            float s = Mathf.Clamp(InvConfig.BadgeScale.Value, 0.2f, 2f);
            icon.localScale = Vector3.Scale(icon.localScale, new Vector3(s, s, 1f));

            // centro visual = o ItemIcon interno (o externo tem margem à direita)
            var visual = icon.childCount > 0 ? (RectTransform)icon.GetChild(0) : icon;
            var size   = visual.rect.size * s;
            var target = new Vector2(panelLeft + 2f + size.x / 2f, panelTop - 2f - size.y / 2f);

            var current = rowRt.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
            icon.position += rowRt.TransformVector((Vector3)(target - (Vector2)current));
        }

        private static void MoveName(inventoryListItem row, RectTransform rowRt, float leftEdge, float rowH)
        {
            if (row.itemName == null) return;
            var rt = row.itemName.rectTransform;

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float curLeft  = rowRt.InverseTransformPoint(corners[0]).x;
            float curRight = rowRt.InverseTransformPoint(corners[2]).x;

            // garante pelo menos 90px de largura para o nome
            float newLeft = Mathf.Min(leftEdge, curRight - 90f);
            rt.offsetMin += new Vector2(newLeft - curLeft, 0f);

            if (Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rowH - 2f * Pad);

            row.itemName.textWrappingMode = TextWrappingModes.Normal;
            row.itemName.overflowMode     = TextOverflowModes.Ellipsis;
        }

        // ── Estado por item (a cada reciclagem) ───────────────────────────────

        internal void ResetFor(inventoryListItem row)
        {
            if (_art != null) _art.enabled = false;
            if (_placeholder != null)
            {
                _placeholder.sprite  = row.icon != null ? row.icon.sprite : null;
                _placeholder.enabled = _placeholder.sprite != null;
            }

            var c = row.rarity != null ? row.rarity.color : Color.gray;
            c.a = 1f;
            if (_borders != null)
                foreach (var b in _borders) if (b != null) b.color = c;
        }

        internal void ShowArt(Sprite sprite, bool locked)
        {
            if (_art == null || sprite == null) return;
            _art.sprite  = sprite;
            _art.color   = locked ? new Color(0.5f, 0.5f, 0.5f, 1f) : Color.white;
            _art.enabled = true;
            if (_placeholder != null) _placeholder.enabled = false;
        }

        internal bool ArtVisible => _art != null && _art.enabled;
    }
}
