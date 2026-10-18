using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using AQWMod.InventoryThumbs.Core;

namespace AQWMod.InventoryThumbs.Thumbnails
{
    // ──────────────────────────────────────────────────────────────────────────
    // Fila de carregamento das miniaturas.
    //
    // - Uma entrada por arte (chave = bundle versionado + prefab + slot), então
    //   itens repetidos ou iguais em listas diferentes reaproveitam a mesma.
    // - A lista do jogo é virtualizada (ItemList recicla ~N linhas conforme o
    //   scroll), então os pedidos chegam só para as linhas visíveis. A fila é
    //   LIFO: o pedido mais recente (o que está na tela agora) sai primeiro.
    // - Antes de baixar, descarta pedidos cujas linhas já mostram outro item
    //   (scroll rápido não gera downloads inúteis).
    // - Usa o próprio AssetBundleLoader do jogo: mesmo cache de disco do Unity
    //   e mesmo refCount do AssetBundleManager, então não briga com o painel
    //   de preview se ele estiver usando o mesmo bundle.
    // ──────────────────────────────────────────────────────────────────────────

    internal sealed class ThumbnailService : MonoBehaviour
    {
        internal static ThumbnailService Instance { get; private set; }

        private enum State { Idle, Queued, Loading, Ready, Failed }

        private sealed class Entry
        {
            internal string Key;
            internal Item   Item;
            internal State  State;
            internal Sprite Sprite;
            internal bool   Owned;      // textura criada pelo mod (pode destruir)
            internal string Info;       // origem do sprite ou motivo da falha
            internal readonly List<ThumbRow> Waiters = new List<ThumbRow>();
        }

        private const int CacheFormatVersion = 1;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>();
        private readonly List<Entry> _queue = new List<Entry>();
        private int _active;
        private ManualLogSource _log;
        private string _diskDir;

        internal int CountReady  { get; private set; }
        internal int CountFailed { get; private set; }
        internal int CountDisk   { get; private set; }

        internal void Setup(ManualLogSource log)
        {
            Instance = this;
            _log     = log;
            _diskDir = Path.Combine(Paths.CachePath, "inventory_thumbs");
        }

        // ── Chave / elegibilidade ─────────────────────────────────────────────

        internal static string KeyFor(Item item)
        {
            if (item == null || item.Bundle == null || string.IsNullOrEmpty(item.Bundle.Filename))
                return null;
            if (!CategoryEnabled(item.EquipSpot)) return null;
            if (item.EquipSpot != EquipSpots.Back && string.IsNullOrEmpty(item.PrefabName))
                return null;

            string file;
            try   { file = item.Bundle.VersionedFileName; }
            catch { file = item.Bundle.Filename; }
            return $"{file}|{item.PrefabName}|{(int)item.EquipSpot}";
        }

        private static bool CategoryEnabled(EquipSpots spot)
        {
            switch (spot)
            {
                case EquipSpots.Weapon:    return InvConfig.ShowWeapons.Value;
                case EquipSpots.Back:      return InvConfig.ShowCapes.Value;
                case EquipSpots.Head:      return InvConfig.ShowHelms.Value;
                case EquipSpots.Armor:
                case EquipSpots.Class:     return InvConfig.ShowArmors.Value;
                case EquipSpots.Pet:       return InvConfig.ShowPets.Value;
                case EquipSpots.House:
                case EquipSpots.HouseItem:
                case EquipSpots.GuildItem: return InvConfig.ShowHouse.Value;
                default:                   return InvConfig.ShowOthers.Value;
            }
        }

        internal string DescribeKey(string key)
        {
            if (key == null) return "sem arte/ignorado";
            return _entries.TryGetValue(key, out var e) ? $"{e.State} ({e.Info})" : "não pedido";
        }

        // ── Pedido ────────────────────────────────────────────────────────────

        internal void Request(ThumbRow row, Item item, string key)
        {
            if (!_entries.TryGetValue(key, out var e))
            {
                e = new Entry { Key = key, State = State.Idle };
                _entries[key] = e;
            }
            e.Item = item;

            switch (e.State)
            {
                case State.Ready:
                    row.Show(e.Sprite);
                    return;

                case State.Failed:
                    return;

                case State.Idle:
                    if (TryLoadFromDisk(e)) { row.Show(e.Sprite); return; }
                    AddWaiter(e, row);
                    e.State = State.Queued;
                    _queue.Add(e);
                    return;

                case State.Queued:
                    AddWaiter(e, row);
                    _queue.Remove(e);   // sobe para o topo da pilha
                    _queue.Add(e);
                    return;

                case State.Loading:
                    AddWaiter(e, row);
                    return;
            }
        }

        private static void AddWaiter(Entry e, ThumbRow row)
        {
            if (!e.Waiters.Contains(row)) e.Waiters.Add(row);
        }

        private static void PruneWaiters(Entry e)
        {
            e.Waiters.RemoveAll(r => r == null || r.CurrentKey != e.Key);
        }

        // ── Loop ──────────────────────────────────────────────────────────────

        private void Update()
        {
            int max = Mathf.Max(1, InvConfig.MaxConcurrent.Value);
            while (_active < max && _queue.Count > 0)
            {
                var e = _queue[_queue.Count - 1];
                _queue.RemoveAt(_queue.Count - 1);

                PruneWaiters(e);
                if (e.Waiters.Count == 0) { e.State = State.Idle; continue; }

                StartCoroutine(Load(e));
            }
        }

        private IEnumerator Load(Entry e)
        {
            _active++;
            e.State = State.Loading;

            AssetBundleLoader loader = null;
            bool   done  = false;
            string error = null;

            try
            {
                loader = new AssetBundleLoader(e.Item.Bundle);
                loader.onComplete += _ => done = true;
                loader.onError    += msg => { error = msg ?? "erro"; done = true; };
                loader.Load();   // pode completar na hora se o bundle já estiver carregado
            }
            catch (Exception ex)
            {
                error = ex.Message;
                done  = true;
            }

            float waited = 0f, timeout = InvConfig.LoadTimeout.Value;
            while (!done && waited < timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            try
            {
                if (!done) error = "timeout";
                if (error == null) Extract(e, loader);
                else Fail(e, error);
            }
            catch (Exception ex)
            {
                Fail(e, ex.Message);
            }
            finally
            {
                // sempre devolver a referência: sem Dispose o finalizer do jogo
                // loga warning e manda evento de erro para o analytics
                try { loader?.Dispose(); } catch { }
                _active--;
            }

            Deliver(e);
        }

        private void Extract(Entry e, AssetBundleLoader loader)
        {
            var sprite = SpriteExtractor.FindSprite(loader, e.Item, out var source);
            if (sprite == null) { Fail(e, "nenhum sprite no bundle"); return; }

            Texture2D tex = null;
            try { tex = SpriteExtractor.CopyToTexture(sprite, Mathf.Max(16, InvConfig.ThumbnailSize.Value)); }
            catch (Exception ex) { Verbose($"cópia falhou ({ex.Message}), usando sprite original"); }

            if (tex != null)
            {
                e.Sprite = SpriteExtractor.MakeSprite(tex);
                e.Owned  = true;
                SaveToDisk(e, tex);
            }
            else
            {
                e.Sprite = sprite;   // continua válido após Unload(false)
                e.Owned  = false;
            }

            e.State = State.Ready;
            e.Info  = source;
            CountReady++;
            Verbose($"OK  '{e.Item?.Name}' ← {source}");
        }

        private void Fail(Entry e, string reason)
        {
            e.State = State.Failed;
            e.Info  = reason;
            CountFailed++;
            Verbose($"FALHA '{e.Item?.Name}' [{e.Key}]: {reason}");
        }

        private static void Deliver(Entry e)
        {
            PruneWaiters(e);
            if (e.State == State.Ready)
                foreach (var row in e.Waiters) row.Show(e.Sprite);
            e.Waiters.Clear();
        }

        // ── Cache em disco ────────────────────────────────────────────────────

        private string DiskPath(string key)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"v{CacheFormatVersion}|{key}"));
                var sb = new StringBuilder(32);
                foreach (var b in hash) sb.Append(b.ToString("x2"));
                return Path.Combine(_diskDir, sb + ".png");
            }
        }

        private bool TryLoadFromDisk(Entry e)
        {
            if (!InvConfig.DiskCache.Value) return false;
            try
            {
                var path = DiskPath(e.Key);
                if (!File.Exists(path)) return false;

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!LoadImage(tex, File.ReadAllBytes(path)))
                {
                    Destroy(tex);
                    return false;
                }
                tex.wrapMode = TextureWrapMode.Clamp;

                e.Sprite = SpriteExtractor.MakeSprite(tex);
                e.Owned  = true;
                e.State  = State.Ready;
                e.Info   = "disco";
                CountDisk++;
                return true;
            }
            catch (Exception ex)
            {
                Verbose($"cache em disco ilegível: {ex.Message}");
                return false;
            }
        }

        // ImageConversion.LoadImage tem uma sobrecarga com ReadOnlySpan<byte>
        // que o compilador não resolve contra o mscorlib do net48; chamada
        // via reflection na sobrecarga (Texture2D, byte[], bool).
        private static MethodInfo _loadImage;

        private static bool LoadImage(Texture2D tex, byte[] data)
        {
            if (_loadImage == null)
                _loadImage = typeof(ImageConversion).GetMethod("LoadImage",
                    new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) });
            return (bool)_loadImage.Invoke(null, new object[] { tex, data, true });
        }

        private void SaveToDisk(Entry e, Texture2D tex)
        {
            if (!InvConfig.DiskCache.Value) return;
            try
            {
                Directory.CreateDirectory(_diskDir);
                File.WriteAllBytes(DiskPath(e.Key), tex.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Verbose($"não salvou no disco: {ex.Message}");
            }
        }

        // ── Limpeza ───────────────────────────────────────────────────────────

        internal void ClearAll()
        {
            ThumbRow.HideAll();

            foreach (var e in _entries.Values)
            {
                if (e.State != State.Ready || !e.Owned || e.Sprite == null) continue;
                var tex = e.Sprite.texture;
                Destroy(e.Sprite);
                if (tex != null) Destroy(tex);
            }
            _entries.Clear();
            _queue.Clear();
            CountReady = CountFailed = CountDisk = 0;

            try
            {
                if (Directory.Exists(_diskDir))
                    foreach (var f in Directory.GetFiles(_diskDir, "*.png")) File.Delete(f);
            }
            catch (Exception ex)
            {
                _log.LogWarning($"[InvThumbs] Não consegui limpar {_diskDir}: {ex.Message}");
            }

            _log.LogInfo("[InvThumbs] Cache limpo — recarregando linhas visíveis.");
            ThumbRow.RebindAll();
        }

        private void Verbose(string msg)
        {
            if (InvConfig.VerboseLog.Value) _log.LogInfo("[InvThumbs] " + msg);
        }
    }
}
