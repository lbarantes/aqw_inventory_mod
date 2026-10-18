using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Thumbnails
{
    // ──────────────────────────────────────────────────────────────────────────
    // "Estúdio" fora da tela: fotografa objetos montados (vários sprites) numa
    // RenderTexture e devolve uma Texture2D.
    //
    // Necessário porque o painel de preview do jogo não mostra uma imagem única
    // para todos os tipos (PreviewSpriteImage.DetermineDisplayedObject):
    //   - Pet / item de casa → instancia o prefab do bundle (várias peças)
    //   - Capacete / armadura / classe → ShowPlayerClone: cria um Player clone
    //     do personagem vestindo o item (new Player(main, pai, equips) +
    //     createAvatar) e renderiza com uma câmera dedicada
    //
    // O estúdio faz o mesmo, só que longe de tudo (posição 5000,5000), na
    // layer "RenderTextures" (a mesma que o jogo usa pro preview) e com uma
    // câmera própria que só enxerga essa layer e só liga durante a foto.
    // Uma foto por vez (Busy).
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class Studio : MonoBehaviour
    {
        internal static Studio Instance { get; private set; }

        private static readonly Vector3 Origin = new Vector3(5000f, 5000f, 0f);
        private const float Margin = 1.06f;

        private ManualLogSource _log;
        private Transform _stage;
        private Camera _cam;
        private int _layer;

        internal bool Busy { get; private set; }
        internal Transform Stage => _stage;

        internal void Setup(ManualLogSource log)
        {
            Instance = this;
            _log = log;

            _layer = LayerMask.NameToLayer("RenderTextures");
            if (_layer < 0) _layer = 31;

            var root = new GameObject("__InvThumbsStudio__");
            root.transform.SetParent(transform, false);
            root.transform.position = Origin;
            _stage = root.transform;

            var camGo = new GameObject("__InvThumbsCam__");
            camGo.transform.SetParent(_stage, false);
            camGo.transform.localPosition = new Vector3(0f, 0f, -50f);
            _cam = camGo.AddComponent<Camera>();
            _cam.orthographic    = true;
            _cam.clearFlags      = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask     = 1 << _layer;
            _cam.nearClipPlane   = 0.01f;
            _cam.farClipPlane    = 200f;
            _cam.allowHDR        = false;
            _cam.allowMSAA       = false;
            _cam.enabled         = false;
        }

        internal void Acquire() => Busy = true;
        internal void Release() => Busy = false;

        // Fotografa "target". focus (opcional) devolve a área de interesse
        // em coordenadas de mundo — ex.: só o capacete dentro do clone.
        // Chame entre Acquire/Release.
        internal IEnumerator Shoot(GameObject target, Func<Bounds?> focus, Action<Texture2D, string> done)
        {
            Texture2D result = null;
            string error = null;
            RenderTexture rt = null;

            if (target == null) { done(null, "alvo nulo"); yield break; }

            SetLayerRecursive(target, _layer);
            Bounds? b = null;
            try { b = focus != null ? focus() : null; } catch { }
            if (b == null) b = RendererBounds(target);
            if (b == null) { done(null, "nenhum renderer visível"); yield break; }

            var bounds = b.Value;
            int max = Mathf.Clamp(InvConfig.TextureSize.Value, 16, 1024);
            float aspect = Mathf.Clamp(bounds.size.x / Mathf.Max(0.0001f, bounds.size.y), 0.2f, 5f);
            int w = aspect >= 1f ? max : Mathf.Max(8, Mathf.RoundToInt(max * aspect));
            int h = aspect >= 1f ? Mathf.Max(8, Mathf.RoundToInt(max / aspect)) : max;

            try
            {
                rt = RenderTexture.GetTemporary(w, h, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
                _cam.targetTexture    = rt;
                _cam.aspect           = (float)w / h;
                _cam.orthographicSize = bounds.extents.y * Margin;
                var c = bounds.center;
                _cam.transform.position = new Vector3(c.x, c.y, c.z - 50f);
                _cam.enabled = true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            if (error == null)
            {
                // câmera ligada renderiza neste frame; lê no fim dele
                yield return new WaitForEndOfFrame();
                try
                {
                    // partes que carregaram depois do SetLayer também entram
                    var prev = RenderTexture.active;
                    RenderTexture.active = rt;
                    result = new Texture2D(w, h, TextureFormat.RGBA32, false);
                    result.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    result.Apply(false, false);
                    result.wrapMode = TextureWrapMode.Clamp;
                    RenderTexture.active = prev;

                    if (IsEmpty(result))
                    {
                        Destroy(result);
                        result = null;
                        error = "foto vazia";
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            _cam.enabled = false;
            _cam.targetTexture = null;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            done(result, error);
        }

        internal static Bounds? RendererBounds(GameObject go)
        {
            Bounds? acc = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || IsEffect(r)) continue;
                if (r is SpriteRenderer sr && (sr.sprite == null || sr.color.a <= 0.01f)) continue;
                if (r.bounds.size.sqrMagnitude <= 0f) continue;
                if (acc == null) acc = r.bounds;
                else { var a = acc.Value; a.Encapsulate(r.bounds); acc = a; }
            }
            return acc;
        }

        // partículas/rastros inflariam a área da foto (tipos em outros módulos,
        // comparados pelo nome para não precisar referenciá-los)
        private static bool IsEffect(Renderer r)
        {
            var n = r.GetType().Name;
            return n == "ParticleSystemRenderer" || n == "TrailRenderer" || n == "LineRenderer";
        }

        internal void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform) SetLayerRecursive(t.gameObject, layer);
        }

        private static bool IsEmpty(Texture2D tex)
        {
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i += 7)
                if (px[i].a > 8) return false;
            return true;
        }
    }
}
