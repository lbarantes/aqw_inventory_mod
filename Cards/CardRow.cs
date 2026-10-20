using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Monta uma linha (inventoryListItem) em um de três layouts:
    //
    //   Plain → a linha original do jogo, intocada
    //   Wide  → card horizontal: quadro de arte à esquerda, nome à direita
    //   Tall  → card vertical (Full Body Preview): figura em cima, nome
    //           embaixo, nível no canto; vários por fileira (grade)
    //
    // Estrutura do prefab (do dump F9, linha original 540x73):
    //   InventoryListItem            ← raiz
    //     ListItemContainer 100x100  ← âncora central (não estica): BG, Selected,
    //                                  Equipped, Content, Highlight... esticam
    //                                  dentro dele, com sobras laterais grandes
    //                                  (o BG tem 712px numa linha de 540)
    //       Content/ItemName         ← nome
    //       Right/Level              ← nível
    //       ACTaggedItem             ← selo AC
    //       ItemIcon                 ← ícone de tipo + raridade + check equipado
    //
    // A mesma linha é reciclada para itens de layouts diferentes (lista
    // mista), então cada layout é calculado uma vez a partir do Plain,
    // fotografado (Capture: âncoras, pivô, tamanho, posição e escala de tudo
    // que é mexido, mais as opções de texto do nome) e reaplicado por SetKind.
    // Posições são calculadas em coordenadas locais da raiz, então não
    // dependem das âncoras/pivôs de cada filho.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class CardRow : MonoBehaviour
    {
        internal const string ContainerName = "ListItemContainer";

        private const float Pad      = 8f;
        private const float Border   = 2f;
        private const float ArtInset = 6f;
        private const float NameGap  = 14f;
        private const float TallNameH = 46f;

        private static readonly Color PanelColor = new Color(0f, 0f, 0f, 0.45f);

        private inventoryListItem _row;
        private RectTransform _rowRt, _container, _icon, _right, _ac;
        private RectTransform _panel;
        private Image   _art;
        private Image   _placeholder;   // ícone de tipo enquanto não há arte
        private Image[] _borders;

        private readonly List<RectTransform> _tracked = new List<RectTransform>();
        private readonly Dictionary<string, Geometry> _geometries = new Dictionary<string, Geometry>();
        private Geometry _plain;

        internal bool Built { get; private set; }
        internal ThumbnailService.Kind Kind { get; private set; } = ThumbnailService.Kind.Plain;
        internal bool IsCard => Kind != ThumbnailService.Kind.Plain;

        // ── Geometria salva ───────────────────────────────────────────────────

        private struct RtState
        {
            internal Vector2 AMin, AMax, Pivot, Size, Pos;
            internal Vector3 Scale;
            internal bool Active;
        }

        private sealed class Geometry
        {
            internal RtState[] States;
            internal TextWrappingModes NameWrap;
            internal TextOverflowModes NameOverflow;
            internal TextAlignmentOptions NameAlign;
            internal bool NameAutoSize;
            internal float NameSize, NameSizeMin, NameSizeMax;
        }

        private Geometry Capture()
        {
            var g = new Geometry { States = new RtState[_tracked.Count] };
            for (int i = 0; i < _tracked.Count; i++)
            {
                var t = _tracked[i];
                g.States[i] = new RtState
                {
                    AMin = t.anchorMin, AMax = t.anchorMax, Pivot = t.pivot,
                    Size = t.sizeDelta, Pos = t.anchoredPosition, Scale = t.localScale,
                    Active = t.gameObject.activeSelf,
                };
            }
            var n = _row.itemName;
            if (n != null)
            {
                g.NameWrap = n.textWrappingMode;
                g.NameOverflow = n.overflowMode;
                g.NameAlign = n.alignment;
                g.NameAutoSize = n.enableAutoSizing;
                g.NameSize = n.fontSize;
                g.NameSizeMin = n.fontSizeMin;
                g.NameSizeMax = n.fontSizeMax;
            }
            return g;
        }

        private void Restore(Geometry g)
        {
            for (int i = 0; i < _tracked.Count; i++)
            {
                var t = _tracked[i];
                var s = g.States[i];
                t.anchorMin = s.AMin; t.anchorMax = s.AMax; t.pivot = s.Pivot;
                t.sizeDelta = s.Size; t.anchoredPosition = s.Pos; t.localScale = s.Scale;
            }
            // só o quadro de arte liga/desliga; os objetos do jogo mantêm o
            // estado que o próprio jogo controla (AC, cadeado, equipado...)
            _panel.gameObject.SetActive(g.States[_tracked.IndexOf(_panel)].Active);

            var n = _row.itemName;
            if (n != null)
            {
                n.textWrappingMode = g.NameWrap;
                n.overflowMode = g.NameOverflow;
                n.alignment = g.NameAlign;
                n.enableAutoSizing = g.NameAutoSize;
                n.fontSizeMin = g.NameSizeMin;
                n.fontSizeMax = g.NameSizeMax;
                n.fontSize = g.NameSize;
            }
        }

        // ── Montagem (uma vez por linha) ──────────────────────────────────────

        internal void Build(inventoryListItem row)
        {
            if (Built) return;
            _row   = row;
            _rowRt = (RectTransform)row.transform;
            _container = row.transform.Find(ContainerName) as RectTransform;
            if (_container == null) return;
            Built = true;

            _icon  = _container.Find("ItemIcon") as RectTransform;
            _right = _container.Find("Right") as RectTransform;
            _ac    = _container.Find("ACTaggedItem") as RectTransform;

            _panel = BuildPanel(_container);
            _panel.gameObject.SetActive(false);

            _tracked.Add(_rowRt);
            for (int i = 0; i < row.transform.childCount; i++)
                if (row.transform.GetChild(i) is RectTransform c) _tracked.Add(c);          // ListItemContainer
            for (int i = 0; i < _container.childCount; i++)
                if (_container.GetChild(i) is RectTransform c) _tracked.Add(c);             // BG, Content, Right...
            if (row.itemName != null) _tracked.Add(row.itemName.rectTransform);

            _plain = Capture();
        }

        // wideH: altura do card horizontal; tallW/tallH: tamanho do card vertical
        internal void SetKind(ThumbnailService.Kind kind, float plainH, float wideH, float tallW, float tallH)
        {
            if (!Built) return;
            Kind = kind;
            if (kind == ThumbnailService.Kind.Plain) { Restore(_plain); return; }

            string id = kind == ThumbnailService.Kind.Wide ? $"W{wideH:0.#}" : $"T{tallW:0.#}x{tallH:0.#}";
            if (!_geometries.TryGetValue(id, out var g))
            {
                Restore(_plain);
                if (kind == ThumbnailService.Kind.Wide) ApplyWide(plainH, wideH);
                else ApplyTall(tallW, tallH);
                g = Capture();
                _geometries[id] = g;
            }
            Restore(g);
        }

        // ── Card horizontal (v0.2+) ───────────────────────────────────────────

        private void ApplyWide(float oldH, float newH)
        {
            float dy = newH - oldH;
            _rowRt.sizeDelta += new Vector2(0f, dy);
            _container.sizeDelta += new Vector2(0f, dy);

            var r      = _rowRt.rect;
            float artH = newH - 2f * Pad;
            float artW = Mathf.Min(InvConfig.ArtWidth.Value, r.width * 0.45f);
            float left = r.xMin + Pad;

            SetRect(_panel, new Vector2(left, r.center.y - artH / 2f), new Vector2(left + artW, r.center.y + artH / 2f));
            _panel.gameObject.SetActive(true);

            MoveBadge(left, r.center.y + artH / 2f, InvConfig.BadgeScale.Value);
            MoveWideName(left + artW + NameGap, newH);
        }

        private void MoveWideName(float leftEdge, float rowH)
        {
            if (_row.itemName == null) return;
            var rt = _row.itemName.rectTransform;
            var (curMin, curMax) = RowRect(rt);

            // garante pelo menos 90px de largura para o nome
            float newLeft = Mathf.Min(leftEdge, curMax.x - 90f);
            rt.offsetMin += new Vector2(newLeft - curMin.x, 0f);

            if (Mathf.Approximately(rt.anchorMin.y, rt.anchorMax.y))
                rt.sizeDelta = new Vector2(rt.sizeDelta.x, rowH - 2f * Pad);

            _row.itemName.textWrappingMode = TextWrappingModes.Normal;
            _row.itemName.overflowMode     = TextOverflowModes.Ellipsis;
        }

        // ── Card vertical (Full Body Preview) ─────────────────────────────────

        private void ApplyTall(float w, float h)
        {
            var size0 = _rowRt.rect.size;
            _rowRt.sizeDelta = new Vector2(w, h);
            _container.sizeDelta += new Vector2(w - size0.x, h - size0.y);

            var r = _rowRt.rect;
            const float inset = 3f;
            var cardMin = new Vector2(r.xMin + inset, r.yMin + inset);
            var cardMax = new Vector2(r.xMax - inset, r.yMax - inset);

            // fundos/brilhos/hover do jogo (filhos esticados do container) tinham
            // sobras laterais de centenas de px: numa grade invadiriam o card do
            // lado, então passam a cobrir exatamente o card
            for (int i = 0; i < _container.childCount; i++)
            {
                var c = _container.GetChild(i) as RectTransform;
                if (c == null || c == _panel) continue;
                bool stretchX = !Mathf.Approximately(c.anchorMin.x, c.anchorMax.x);
                bool stretchY = !Mathf.Approximately(c.anchorMin.y, c.anchorMax.y);
                if (stretchX && stretchY) SetRect(c, cardMin, cardMax);
            }

            // figura: tudo acima da faixa do nome
            SetRect(_panel, new Vector2(cardMin.x + 3f, cardMin.y + TallNameH),
                            new Vector2(cardMax.x - 3f, cardMax.y - 3f));
            _panel.gameObject.SetActive(true);

            // nome: faixa de baixo, centralizado, até 2 linhas com auto-ajuste
            var n = _row.itemName;
            if (n != null)
            {
                SetRect(n.rectTransform, new Vector2(cardMin.x + 6f, cardMin.y + 2f),
                                         new Vector2(cardMax.x - 6f, cardMin.y + TallNameH - 2f));
                float baseSize = n.fontSize;
                n.textWrappingMode = TextWrappingModes.Normal;
                n.overflowMode     = TextOverflowModes.Ellipsis;
                n.alignment        = TextAlignmentOptions.Center;
                n.enableAutoSizing = true;
                n.fontSizeMax      = baseSize;
                n.fontSizeMin      = Mathf.Max(8f, baseSize * 0.6f);
            }

            float panelTop = cardMax.y - 3f, panelLeft = cardMin.x + 3f, panelRight = cardMax.x - 3f;
            MoveBadge(panelLeft, panelTop, InvConfig.BadgeScale.Value * 0.85f);

            // nível: canto superior direito do card
            if (_right != null)
            {
                var level = _right.Find("Level") as RectTransform;
                var visual = level != null ? level : _right;
                Scale(_right, 0.85f);
                var vs = visual.rect.size * 0.85f;
                MoveCenter(_right, visual, new Vector2(panelRight - vs.x / 2f, panelTop - vs.y / 2f));
            }

            // selo AC: logo abaixo do nível
            if (_ac != null)
            {
                Scale(_ac, 0.55f);
                var vs = _ac.rect.size * 0.55f;
                MoveCenter(_ac, _ac, new Vector2(panelRight - vs.x / 2f, panelTop - 52f - vs.y / 2f));
            }
        }

        // ── Quadro de arte ────────────────────────────────────────────────────

        private RectTransform BuildPanel(RectTransform container)
        {
            var panel = NewChild("__InvCard__", container);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(100f, 100f);

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
            ph.sizeDelta = new Vector2(56f, 56f);
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
        private void MoveBadge(float panelLeft, float panelTop, float scale)
        {
            if (_icon == null) return;
            float s = Mathf.Clamp(scale, 0.2f, 2f);
            Scale(_icon, s);

            // centro visual = o ItemIcon interno (o externo tem margem à direita)
            var visual = _icon.childCount > 0 ? (RectTransform)_icon.GetChild(0) : _icon;
            var size   = visual.rect.size * s;
            MoveCenter(_icon, visual, new Vector2(panelLeft + 2f + size.x / 2f, panelTop - 2f - size.y / 2f));
        }

        // ── Utilitários de geometria (coordenadas locais da raiz da linha) ────

        private static void Scale(RectTransform rt, float s) =>
            rt.localScale = Vector3.Scale(rt.localScale, new Vector3(s, s, 1f));

        // move "rt" para que o centro de "visual" (rt ou um filho) caia em target
        private void MoveCenter(RectTransform rt, RectTransform visual, Vector2 target)
        {
            var current = (Vector2)_rowRt.InverseTransformPoint(visual.TransformPoint(visual.rect.center));
            rt.position += _rowRt.TransformVector((Vector3)(target - current));
        }

        // retângulo de rt em coordenadas da raiz (min, max)
        private (Vector2, Vector2) RowRect(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = (Vector2)_rowRt.InverseTransformPoint(corners[0]);
            var b = (Vector2)_rowRt.InverseTransformPoint(corners[2]);
            return (Vector2.Min(a, b), Vector2.Max(a, b));
        }

        // posiciona rt para ocupar [min,max] (coordenadas da raiz), sem mudar
        // âncoras/pivô: converte para o espaço do pai e acerta os offsets
        private void SetRect(RectTransform rt, Vector2 min, Vector2 max)
        {
            var parent = rt.parent as RectTransform;
            if (parent == null) return;
            Vector2 pMin = parent.InverseTransformPoint(_rowRt.TransformPoint(min));
            Vector2 pMax = parent.InverseTransformPoint(_rowRt.TransformPoint(max));
            var pr = parent.rect;
            var aMin = pr.min + Vector2.Scale(rt.anchorMin, pr.size);
            var aMax = pr.min + Vector2.Scale(rt.anchorMax, pr.size);
            rt.offsetMin = Vector2.Min(pMin, pMax) - aMin;
            rt.offsetMax = Vector2.Max(pMin, pMax) - aMax;
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
