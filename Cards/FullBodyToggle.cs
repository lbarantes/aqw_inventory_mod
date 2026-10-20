using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Cards
{
    // ──────────────────────────────────────────────────────────────────────────
    // Checkbox "Full Set Preview" no rodapé do inventário, no canto oposto ao
    // "(+) 57 / 150 Items" (UIInventory.textItemCap).
    //
    // Criado por código (postfix em UIInventory.OnEnable), uma vez por janela:
    // mesmo pai do texto de capacidade, alinhado na altura dele e encostado na
    // borda direita da lista. Fonte/cor copiadas do texto do jogo. Marcar ou
    // desmarcar grava em [FullBody] Enabled no .cfg e remonta as listas abertas.
    // ──────────────────────────────────────────────────────────────────────────

    internal static class FullBodyToggle
    {
        private const string Name  = "__FullBodyToggle__";
        private const string Label = "Full Set Preview";

        private static readonly Color Gold = new Color(0.86f, 0.68f, 0.30f, 1f);

        internal static void Ensure(UIInventory inv)
        {
            if (inv == null || !InvConfig.Enabled.Value || !InvConfig.CardMode) return;
            var cap = inv.textItemCap;
            if (cap == null || inv.il == null) return;

            var parent = cap.rectTransform.parent as RectTransform;
            if (parent == null || parent.Find(Name) != null) return;

            var root = NewChild(Name, parent);
            root.pivot = new Vector2(1f, 0.5f);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f);
            // se o rodapé for um LayoutGroup, ele não reposiciona o checkbox
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

            // área clicável (caixa + texto): Image transparente recebe o raycast
            var hit = root.gameObject.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);

            float box = Mathf.Clamp(cap.fontSize * 1.1f, 16f, 28f);

            var boxRt = NewChild("Box", root);
            boxRt.anchorMin = boxRt.anchorMax = boxRt.pivot = new Vector2(0f, 0.5f);
            boxRt.sizeDelta = new Vector2(box, box);
            boxRt.anchoredPosition = Vector2.zero;
            var boxImg = boxRt.gameObject.AddComponent<Image>();
            boxImg.color = new Color(0f, 0f, 0f, 0.6f);
            foreach (var (aMin, aMax, size) in new[]
            {
                (new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, 2)),
                (new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 2)),
                (new Vector2(0, 0), new Vector2(0, 1), new Vector2(2, 0)),
                (new Vector2(1, 0), new Vector2(1, 1), new Vector2(2, 0)),
            })
            {
                var e = NewChild("Edge", boxRt);
                e.anchorMin = aMin; e.anchorMax = aMax; e.sizeDelta = size;
                e.pivot = new Vector2(aMin.x == aMax.x ? aMin.x : 0.5f, aMin.y == aMax.y ? aMin.y : 0.5f);
                e.anchoredPosition = Vector2.zero;
                var img = e.gameObject.AddComponent<Image>();
                img.color = Gold;
                img.raycastTarget = false;
            }

            var check = NewChild("Check", boxRt);
            check.anchorMin = Vector2.zero; check.anchorMax = Vector2.one;
            check.offsetMin = new Vector2(4f, 4f); check.offsetMax = new Vector2(-4f, -4f);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.color = Gold;
            checkImg.raycastTarget = false;

            var labelRt = NewChild("Label", root);
            labelRt.anchorMin = labelRt.anchorMax = labelRt.pivot = new Vector2(0f, 0.5f);
            var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = cap.font;
            label.fontSharedMaterial = cap.fontSharedMaterial;
            label.fontSize = cap.fontSize;
            label.color = cap.color;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            label.raycastTarget = false;
            label.text = Label;

            var pref = label.GetPreferredValues(Label);
            float gap = 8f;
            labelRt.sizeDelta = new Vector2(pref.x + 2f, Mathf.Max(box, pref.y));
            labelRt.anchoredPosition = new Vector2(box + gap, 0f);
            root.sizeDelta = new Vector2(box + gap + pref.x + 2f, Mathf.Max(box, pref.y));

            // canto direito da lista, na altura do texto "57 / 150 Items";
            // recalculado todo frame (a janela abre com animação de escala e o
            // layout do rodapé pode assentar depois do OnEnable)
            var anchor = root.gameObject.AddComponent<FooterAnchor>();
            anchor.Cap  = cap.rectTransform;
            anchor.List = (RectTransform)inv.il.transform;
            anchor.Place();

            var toggle = root.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = boxImg;
            toggle.graphic = checkImg;
            toggle.isOn = InvConfig.FullBody.Value;
            toggle.onValueChanged.AddListener(OnChanged);
        }

        // depois de reler o .cfg (Ctrl+F9): checkboxes abertos mostram o valor novo
        internal static void SyncAll()
        {
            foreach (var t in UnityEngine.Object.FindObjectsByType<Toggle>(FindObjectsSortMode.None))
                if (t != null && t.name == Name) t.SetIsOnWithoutNotify(InvConfig.FullBody.Value);
        }

        private static void OnChanged(bool on)
        {
            try
            {
                InvConfig.FullBody.Value = on;   // BepInEx salva o .cfg ao alterar
                CardLayout.RefreshAll();
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError($"[InvThumbs] Erro ao trocar Full Set Preview: {ex}");
            }
        }

        private sealed class FooterAnchor : MonoBehaviour
        {
            internal RectTransform Cap, List;
            private readonly Vector3[] _corners = new Vector3[4];

            private void LateUpdate() => Place();

            internal void Place()
            {
                if (Cap == null || List == null) return;
                var rt = (RectTransform)transform;
                List.GetWorldCorners(_corners);
                var capCenter = Cap.TransformPoint(Cap.rect.center);
                rt.position = new Vector3(_corners[2].x, capCenter.y, capCenter.z);
                rt.anchoredPosition += new Vector2(-12f, 0f);
            }
        }

        private static RectTransform NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }
    }
}
