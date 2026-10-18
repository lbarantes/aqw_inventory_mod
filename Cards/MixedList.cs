using System.Collections.Generic;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;
using AQWMod.InventoryThumbs.Thumbnails;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Lista mista: cada item tem a sua altura — card (CardHeight) para quem tem
    // arte, linha original (fabHeight) para o resto (Misc, House, Gems...).
    //
    // A ItemList do jogo só sabe altura fixa: inventoryListItem.Update move
    // cada linha reciclada em saltos de fabHeight e calcula o índice do item
    // por localPosition.y / fabHeight. Aqui esse Update é desligado (prefixo em
    // inventoryListItem.Update) e este componente faz o mesmo trabalho com
    // alturas variáveis:
    //   - offsets[i] = soma das alturas dos itens antes de i
    //   - a cada LateUpdate, descobre que faixa de índices está visível no
    //     viewport e distribui as linhas recicladas (listItems) por ela;
    //     linha que muda de índice recebe Refresh() → setItemData, exatamente
    //     como no jogo;
    //   - replica o resto do Update original: esconder linha além do fim da
    //     lista e ligar/desligar o selectedGraphic.
    // A altura do conteúdo do ScrollRect é recalculada depois de cada
    // ScaleTheList (postfix).
    //
    // fabHeight NÃO é alterado: as listItemCount linhas do jogo cobrem a
    // janela com altura original, então cobrem com folga quando parte delas
    // é card (mais alto).
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class MixedList : MonoBehaviour
    {
        private ItemList _list;
        private float _plainH;

        private List<Item> _builtFor;
        private int _builtCount = -1;
        private float[] _offsets = new float[1];   // tamanho count+1
        private bool[] _isCard = new bool[0];

        private List<GameObject> _rowsFor;
        private inventoryListItem[] _rows = new inventoryListItem[0];

        private readonly HashSet<int> _needed = new HashSet<int>();
        private readonly List<inventoryListItem> _free = new List<inventoryListItem>();
        private readonly Vector3[] _corners = new Vector3[4];

        internal float PlainHeight => _plainH;
        internal static float CardHeight => Mathf.Clamp(InvConfig.CardHeight.Value, 40f, 400f);

        internal void Setup(ItemList list)
        {
            _list   = list;
            _plainH = list.fabHeight;
        }

        internal static bool WantsCard(Item item) => ThumbnailService.KeyFor(item) != null;

        // ── Alturas ───────────────────────────────────────────────────────────

        internal void Rebuild()
        {
            var items = _list.itemList;
            int n = items?.Count ?? 0;
            _builtFor   = items;
            _builtCount = n;

            if (_offsets.Length != n + 1) _offsets = new float[n + 1];
            if (_isCard.Length != n) _isCard = new bool[n];

            float card = CardHeight, y = 0f;
            for (int i = 0; i < n; i++)
            {
                _offsets[i] = y;
                _isCard[i]  = WantsCard(items[i]);
                y += _isCard[i] ? card : _plainH;
            }
            _offsets[n] = y;

            var content = _list.ContentRect;
            if (content != null) content.sizeDelta = new Vector2(content.sizeDelta.x, y);
        }

        internal bool IsCardIndex(int i) => i >= 0 && i < _isCard.Length && _isCard[i];

        // ── Posicionamento ────────────────────────────────────────────────────

        private void LateUpdate()
        {
            if (_list == null || _list.itemList == null) return;
            if (_list.itemList != _builtFor || _list.itemList.Count != _builtCount) Rebuild();
            CollectRows();
            if (_rows.Length == 0) return;

            int n = _builtCount;
            float top, bottom;
            if (!VisibleRange(out top, out bottom)) return;

            // primeiro índice visível (busca binária em offsets)
            int lo = 0, hi = n;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_offsets[mid + 1] <= top) lo = mid + 1; else hi = mid;
            }
            int first = Mathf.Max(0, lo - 1);   // uma linha de folga acima

            // índices que as linhas devem cobrir agora
            _needed.Clear();
            for (int k = 0; k < _rows.Length; k++) _needed.Add(first + k);

            // linha que já mostra um índice necessário fica; as outras são livres
            _free.Clear();
            foreach (var r in _rows)
            {
                if (r == null) continue;
                if (_needed.Contains(r.selectionIndex) && r.itemIndexCheck == r.selectionIndex)
                    _needed.Remove(r.selectionIndex);
                else
                    _free.Add(r);
            }

            int f = 0;
            foreach (var idx in _needed)
            {
                if (f >= _free.Count) break;
                var r = _free[f++];
                r.selectionIndex = idx;
                r.itemIndexCheck = idx;
                if (idx < n) r.Refresh();                       // setItemData → ThumbRow.Bind
                else if (r.container != null) r.container.SetActive(false);
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

                float h = _isCard[i] ? CardHeight : _plainH;
                var t = r.transform;
                var p = t.localPosition;
                float y = -(_offsets[i] + h / 2f);
                if (!Mathf.Approximately(p.y, y)) t.localPosition = new Vector3(p.x, y, p.z);

                if (r.selectedGraphic != null)
                {
                    bool sel = i == _list.selectedItem;
                    if (r.selectedGraphic.gameObject.activeSelf != sel) r.selectedGraphic.gameObject.SetActive(sel);
                }
            }
        }

        private void CollectRows()
        {
            var go = _list.listItems;
            if (go == _rowsFor && go != null && go.Count == _rows.Length) return;
            _rowsFor = go;
            var tmp = new List<inventoryListItem>();
            if (go != null)
                foreach (var g in go)
                {
                    if (g == null) continue;
                    var r = g.GetComponent<inventoryListItem>();
                    if (r != null) tmp.Add(r);
                }
            _rows = tmp.ToArray();
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
            // as linhas ficam em localPosition.y = -(offset + h/2), origem no pivô do conteúdo
            float yTop    = content.InverseTransformPoint(_corners[1]).y;
            float yBottom = content.InverseTransformPoint(_corners[0]).y;
            top    = -yTop;
            bottom = -yBottom;
            return true;
        }
    }
}
