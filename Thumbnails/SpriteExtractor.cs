using System;
using UnityEngine;

namespace AQWMod.InventoryThumbs.Thumbnails
{
    // ──────────────────────────────────────────────────────────────────────────
    // Acha a arte de um item dentro do asset bundle e copia para uma textura
    // pequena e própria do mod.
    //
    // Como o painel de preview do jogo (PreviewSpriteImage) faz:
    //   - Arma  → bundle.getGameObject(PrefabName).GetComponent<WeaponSlots>().weapon
    //   - Capa  → bundle.getGameObject("CapeGO")  (GameObject com SpriteRenderers)
    //   - Resto → bundle.getGameObject(PrefabName) mostrado como GameObject
    //   - Armadura/classe → clone do player (não dá pra reaproveitar aqui)
    //
    // A ordem de busca abaixo cobre os mesmos casos e cai para "maior sprite
    // encontrado" quando não há componente específico.
    //
    // Por que copiar: o AssetBundleManager descarrega o bundle com
    // Unload(false) quando o refCount zera — o Sprite original continuaria
    // vivo, mas preso à textura inteira (às vezes um atlas grande). A cópia
    // pequena economiza memória e pode ir para o cache em disco.
    // ──────────────────────────────────────────────────────────────────────────

    internal static class SpriteExtractor
    {
        internal static Sprite FindSprite(AssetBundleLoader loader, Item item, out string source)
        {
            source = null;
            foreach (var name in PrefabCandidates(item))
            {
                GameObject go = null;
                try { go = loader.getGameObject(name); } catch { }
                if (go == null) continue;

                var s = FromGameObject(go, out var how);
                if (s != null) { source = $"{name}:{how}"; return s; }
            }

            // último recurso: maior Sprite solto no bundle
            try
            {
                var bundle = loader.Bundle;
                if (bundle != null)
                {
                    var s = Largest(bundle.LoadAllAssets<Sprite>());
                    if (s != null) { source = "bundle:Sprite"; return s; }
                }
            }
            catch { }
            return null;
        }

        private static string[] PrefabCandidates(Item item)
        {
            if (item.EquipSpot == EquipSpots.Back)
                return string.IsNullOrEmpty(item.PrefabName)
                    ? new[] { "CapeGO" }
                    : new[] { "CapeGO", item.PrefabName };
            return string.IsNullOrEmpty(item.PrefabName) ? new string[0] : new[] { item.PrefabName };
        }

        private static Sprite FromGameObject(GameObject go, out string how)
        {
            how = null;

            var weapon = go.GetComponentInChildren<WeaponSlots>(true);
            if (weapon != null)
            {
                var s = weapon.weapon != null ? weapon.weapon : weapon.backweapon;
                if (s != null) { how = "WeaponSlots"; return s; }
            }

            var cape = go.GetComponentInChildren<CapeSlots>(true);
            if (cape != null && cape.cape != null) { how = "CapeSlots"; return cape.cape; }

            // HeadSlots e armaduras guardam as peças em ItemSlots.slots
            Sprite best = null;
            foreach (var slots in go.GetComponentsInChildren<ItemSlots>(true))
            {
                if (slots.slots == null) continue;
                foreach (var ss in slots.slots)
                    if (ss != null) best = Bigger(best, ss.sprite);
            }
            if (best != null) { how = "ItemSlots"; return best; }

            foreach (var sr in go.GetComponentsInChildren<SpriteRenderer>(true))
                best = Bigger(best, sr.sprite);
            if (best != null) { how = "SpriteRenderer"; return best; }

            foreach (var img in go.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                best = Bigger(best, img.sprite);
            if (best != null) { how = "UI.Image"; return best; }

            return null;
        }

        private static Sprite Largest(Sprite[] sprites)
        {
            Sprite best = null;
            if (sprites == null) return null;
            foreach (var s in sprites) best = Bigger(best, s);
            return best;
        }

        private static Sprite Bigger(Sprite a, Sprite b)
        {
            if (b == null) return a;
            if (a == null) return b;
            return b.rect.width * b.rect.height > a.rect.width * a.rect.height ? b : a;
        }

        // Copia a região do sprite para uma Texture2D legível, reduzida para
        // caber em maxSize. Usa Blit (GPU), então funciona mesmo com texturas
        // não-legíveis. Retorna null se o sprite estiver rotacionado no atlas.
        internal static Texture2D CopyToTexture(Sprite sprite, int maxSize)
        {
            if (sprite == null || sprite.texture == null) return null;
            if (sprite.packed && sprite.packingRotation != SpritePackingRotation.None) return null;

            Rect r;
            try   { r = sprite.textureRect; }
            catch { r = sprite.rect; }   // atlas "tight" não expõe textureRect

            var src = sprite.texture;
            float scale = Mathf.Min(1f, maxSize / Mathf.Max(r.width, r.height));
            int w = Mathf.Max(1, Mathf.RoundToInt(r.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(r.height * scale));

            var rt   = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            var prev = RenderTexture.active;
            try
            {
                var blitScale  = new Vector2(r.width / src.width, r.height / src.height);
                var blitOffset = new Vector2(r.x / src.width, r.y / src.height);

                RenderTexture.active = rt;
                GL.Clear(true, true, Color.clear);
                Graphics.Blit(src, rt, blitScale, blitOffset);

                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply(false, false);
                tex.wrapMode   = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                return tex;
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        internal static Sprite MakeSprite(Texture2D tex)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.name = "__InvThumb__";
            return s;
        }
    }
}
