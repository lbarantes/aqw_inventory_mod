using System.Collections.Generic;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Thumbnails;
using Kind = AQWMod.InventoryThumbs.Thumbnails.ThumbnailService.Kind;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Lista mista: cada item tem o seu tamanho e posição.
    //   Plain → linha original (fabHeight), largura inteira
    //   Wide  → card horizontal (CardHeight), largura inteira
    //   Tall  → card vertical (Full Set), em grade de FullBodyColumns por
    //           fileira; um item Plain/Wide no meio fecha a fileira
    //
    // A ItemList do jogo só sabe altura fixa: inventoryListItem.Update move
    // cada linha reciclada em saltos de fabHeight e calcula o índice do item
    // por localPosition.y / fabHeight. Aqui esse Update é desligado (prefixo em
    // inventoryListItem.Update) e este componente faz o mesmo trabalho:
    //   - Rebuild calcula, para cada índice, topo (y), centro x, largura e
    //     altura, e a altura total do conteúdo do ScrollRect;
    //   - a cada LateUpdate, descobre que índices cruzam o viewport e
    //     distribui as linhas recicladas (listItems) entre eles; linha que
    //     muda de índice recebe Refresh() → setItemData, como no jogo;
    //   - se a grade mostra mais itens do que há linhas (o jogo cria ~11),
    //     cria mais do mesmo jeito que ItemList.AddListItemPrefabs — elas
    //     entram em listItems e o jogo as destrói junto com as dele;
    //   - replica o resto do Update original: esconder linha sem item e
    //     ligar/desligar o selectedGraphic.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class MixedList : MonoBehaviour
    {
        private const int MaxRows = 64;
        private const float Slack = 40f;   // px além do viewport em cima e embaixo

        private ItemList _list;
        private float _plainH, _rowW;

        private List<Item> _builtFor;
        private int _builtCount = -1;
        private Kind[]  _kind = new Kind[0];
        private float[] _top  = new float[0];   // distância do topo do conteúdo
        private float[] _h    = new float[0];
        private float[] _x    = new float[0];   // centro x (local do Content)

        private List<GameObject> _rowsFor;
        private int _rowsCount = -1;
        private inventoryListItem[] _rows = new inventoryListItem[0];

        private readonly List<int> _needed = new List<int>();
        private readonly HashSet<int> _neededSet = new HashSet<int>();
        private readonly List<inventoryListItem> _free = new List<inventoryListItem>();
        private readonly Vector3[] _corners = new Vector3[4];

        internal ItemList List => _list;
        internal float PlainHeight => _plainH;
        internal float TallWidth   => _rowW / Columns;
        internal static float CardHeight => Mathf.Clamp(InvConfig.CardHeight.Value, 40f, 400f);
        internal static float TallHeight => Mathf.Clamp(InvConfig.FullBodyHeight.Value, 120f, 600f);
        private static int Columns => Mathf.Clamp(InvConfig.FullBodyColumns.Value, 1, 4);

        internal void Setup(ItemList list)
        {
            _list   = list;
            _plainH = list.fabHeight;

            // largura da linha original (prefab com âncora central: sizeDelta.x)
            var prt = list.listItemPrefab != null ? list.listItemPrefab.transform as RectTransform : null;
            _rowW = prt != null && prt.rect.width > 1f ? prt.rect.width : 540f;
        }

        // ── Layout ────────────────────────────────────────────────────────────

        internal void Rebuild()
        {
            var items = _list.itemList;
            int n = items?.Count ?? 0;
            _builtFor   = items;
            _builtCount = n;

            if (_kind.Length != n)
            {
                _kind = new Kind[n]; _top = new float[n]; _h = new float[n]; _x = new float[n];
            }

            int cols = Columns;
            float colW = _rowW / cols, tallH = TallHeight, wideH = CardHeight;
            float y = 0f, lineTop = 0f;
            int col = 0;

            for (int i = 0; i < n; i++)
            {
                var k = ThumbnailService.KindFor(items[i]);
                _kind[i] = k;

                if (k == Kind.Tall)
                {
                    if (col == 0) lineTop = y;
                    _top[i] = lineTop;
                    _h[i]   = tallH;
                    _x[i]   = -_rowW / 2f + colW * (col + 0.5f);
                    if (++col == cols) { col = 0; y = lineTop + tallH; }
                }
                else
                {
                    if (col > 0) { col = 0; y = lineTop + tallH; }   // fecha a fileira
                    _top[i] = y;
                    _h[i]   = k == Kind.Wide ? wideH : _plainH;
                    _x[i]   = 0f;
                    y += _h[i];
                }
            }
            if (col > 0) y = lineTop + tallH;

            var content = _list.ContentRect;
            if (content != null) content.sizeDelta = new Vector2(content.sizeDelta.x, y);
        }

        internal Kind KindAt(int i) => i >= 0 && i < _kind.Length ? _kind[i] : Kind.Plain;

        // ── Posicionamento ────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_list == null || _list.itemList == null) return;
            if (_list.itemList != _builtFor || _list.itemList.Count != _builtCount) Rebuild();

            int n = _builtCount;
            if (!VisibleRange(out var top, out var bottom)) return;
            top -= Slack; bottom += Slack;

            // primeiro índice cuja base passa do topo visível (topos não decrescem)
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_top[mid] + _h[mid] <= top) lo = mid + 1; else hi = mid;
            }
            _needed.Clear();
            for (int i = lo; i < n && _top[i] < bottom && _needed.Count < MaxRows; i++) _needed.Add(i);

            CollectRows(_needed.Count);
            if (_rows.Length == 0) return;

            // linha que já mostra um índice necessário fica; as outras são livres
            _neededSet.Clear();
            foreach (var i in _needed) _neededSet.Add(i);
            _free.Clear();
            foreach (var r in _rows)
            {
                if (r == null) continue;
                if (_neededSet.Contains(r.selectionIndex) && r.itemIndexCheck == r.selectionIndex)
                    _neededSet.Remove(r.selectionIndex);
                else
                    _free.Add(r);
            }

            int f = 0;
            foreach (var idx in _needed)
            {
                if (!_neededSet.Contains(idx)) continue;
                if (f >= _free.Count) break;
                var r = _free[f++];
                r.selectionIndex = idx;
                r.itemIndexCheck = idx;
                r.Refresh();                                   // setItemData → ThumbRow.Bind
            }
            // linhas que sobraram: sem item
            for (; f < _free.Count; f++)
            {
                var r = _free[f];
                r.selectionIndex = -1;
                r.itemIndexCheck = -1;
                if (r.container != null && r.container.activeSelf) r.container.SetActive(false);
            }

            foreach (var r in _rows)
            {
                if (r == null) continue;
                int i = r.selectionIndex;
                if (i < 0 || i >= n)
                {
                    if (r.container != null && r.container.activeSelf) r.container.SetActive(false);
                    continue;
                }

                var t = r.transform;
                var p = t.localPosition;
                var target = new Vector3(_x[i], -(_top[i] + _h[i] / 2f), p.z);
                if (p != target) t.localPosition = target;

                if (r.selectedGraphic != null)
                {
                    bool sel = i == _list.selectedItem;
                    if (r.selectedGraphic.gameObject.activeSelf != sel) r.selectedGraphic.gameObject.SetActive(sel);
                }
            }
        }

        // linhas recicladas do jogo; cria mais se a grade precisar
        private void CollectRows(int wanted)
        {
            var go = _list.listItems;
            if (go == null) { _rows = new inventoryListItem[0]; return; }

            while (go.Count < wanted && go.Count < MaxRows && _list.listItemPrefab != null && _list.Content != null)
                go.Add(CreateRow());

            if (go == _rowsFor && go.Count == _rowsCount) return;
            _rowsFor = go;
            _rowsCount = go.Count;
            var tmp = new List<inventoryListItem>(go.Count);
            foreach (var g in go)
            {
                if (g == null) continue;
                var r = g.GetComponent<inventoryListItem>();
                if (r != null) tmp.Add(r);
            }
            _rows = tmp.ToArray();
        }

        // mesmo que ItemList.AddListItemPrefabs faz para cada linha
        private GameObject CreateRow()
        {
            var go = Instantiate(_list.listItemPrefab, _list.Content);
            go.transform.localPosition = new Vector3(0f, 100000f, 0f);   // fora da vista até ser posicionada
            var r = go.GetComponent<inventoryListItem>();
            r.il = _list;
            r.MainScroll = _list.sr;
            r.inv = _list.inv;
            r.selectionIndex = -1;
            r.itemIndexCheck = -1;
            r.ready = true;
            if (r.container != null) r.container.SetActive(false);
            return go;
        }

        // faixa visível do viewport, em "distância a partir do topo do conteúdo"
        private bool VisibleRange(out float top, out float bottom)
        {
            top = bottom = 0f;
            var sr = _list.sr;
            // as linhas são filhas de ItemList.Content (AddListItemPrefabs)
            var content = _list.Content;
            if (sr == null || content == null) return false;

            var viewport = sr.viewport != null ? sr.viewport : (RectTransform)sr.transform;
            viewport.GetWorldCorners(_corners);
            // as linhas ficam em localPosition.y = -(topo + h/2), origem no pivô do conteúdo
            float yTop    = content.InverseTransformPoint(_corners[1]).y;
            float yBottom = content.InverseTransformPoint(_corners[0]).y;
            top    = -yTop;
            bottom = -yBottom;
            return true;
        }
    }
}
