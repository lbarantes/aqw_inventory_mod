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

        private static int TextureSize => Mathf.Clamp(InvConfig.TextureSize.Value, 16, 1024);

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

        // Como a arte é obtida — espelha PreviewSpriteImage.DetermineDisplayedObject
        internal enum Strategy
        {
            Sprite,   // arma/capa: sprite único do prefab (WeaponSlots/CapeSlots)
            Object,   // pet/item de casa: prefab de várias peças fotografado no Studio
            Clone,    // capacete/armadura/classe: clone do personagem vestindo o item
        }

        internal static Strategy StrategyFor(Item item)
        {
            switch (item.EquipSpot)
            {
                case EquipSpots.Head:
                case EquipSpots.Armor:
                case EquipSpots.Class:     return Strategy.Clone;
                case EquipSpots.Pet:
                case EquipSpots.House:
                case EquipSpots.HouseItem:
                case EquipSpots.GuildItem: return Strategy.Object;
                default:                   return Strategy.Sprite;
            }
        }

        // tipos que ficam sempre como lista (aba Misc / Gems): sem arte própria
        private static bool IsMiscType(iType t)
        {
            switch (t)
            {
                case iType.None: case iType.Item: case iType.Note: case iType.QuestItem:
                case iType.Resource: case iType.Enhancement: case iType.ServerUse:
                case iType.ClientUse: case iType.Misc: case iType.Elixir:
                case iType.Pattern: case iType.Spellstone: case iType.Trait:
                    return true;
                default:
                    return false;
            }
        }

        internal static string KeyFor(Item item)
        {
            if (item == null || item.Bundle == null || string.IsNullOrEmpty(item.Bundle.Filename))
                return null;
            if (IsMiscType(item.ItemType)) return null;
            if (!CategoryEnabled(item.EquipSpot)) return null;

            var strategy = StrategyFor(item);
            if (strategy != Strategy.Clone && item.EquipSpot != EquipSpots.Back
                && string.IsNullOrEmpty(item.PrefabName))
                return null;

            string file;
            try   { file = item.Bundle.VersionedFileName; }
            catch { file = item.Bundle.Filename; }
            // estratégia na chave: mudar a forma de extrair invalida o cache antigo
            var key = $"{strategy}{StrategyVersion(strategy)}|{file}|{item.PrefabName}|{(int)item.EquipSpot}";

            // capacete é fotografado vestido com a armadura/classe atual (como o
            // preview do jogo): trocar de armadura gera outra foto
            if (item.EquipSpot == EquipSpots.Head) key += "|body" + BodySignature();
            return key;
        }

        // armadura (ou, sem ela, classe) que o clone do capacete veste
        private static string BodySignature()
        {
            var main = Entity.mainPlayer;
            if (main == null) return "-";
            var body = main.Armor ?? main.Class;
            return body != null ? $"{body.EquipSpot}:{body.ID}" : "-";
        }

        private static int StrategyVersion(Strategy s) => s == Strategy.Sprite ? 1 : 2;

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
                case EquipSpots.GuildItem: return InvConfig.HouseInCards.Value;
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

        private string _lastBody;
        private float  _bodyCheckAt;

        private void Update()
        {
            // trocou de armadura/classe → as linhas visíveis de capacete pedem
            // a foto nova (o refreshEquips do jogo não recarrega as linhas)
            if (Time.unscaledTime >= _bodyCheckAt)
            {
                _bodyCheckAt = Time.unscaledTime + 0.5f;
                var body = BodySignature();
                if (_lastBody != null && body != _lastBody)
                {
                    Verbose($"armadura mudou ({_lastBody} → {body}), recarregando linhas");
                    ThumbRow.RebindAll();
                }
                _lastBody = body;
            }

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

            IEnumerator job;
            switch (StrategyFor(e.Item))
            {
                case Strategy.Clone:  job = LoadClone(e);  break;
                case Strategy.Object: job = LoadObject(e); break;
                default:              job = LoadSprite(e); break;
            }

            // roda o job sem deixar uma exceção travar _active
            while (true)
            {
                object current;
                try
                {
                    if (!job.MoveNext()) break;
                    current = job.Current;
                }
                catch (Exception ex)
                {
                    if (e.State == State.Loading) Fail(e, ex.Message);
                    break;
                }
                yield return current;
            }
            if (e.State == State.Loading) Fail(e, "sem resultado");

            _active--;
            Deliver(e);
        }

        // ── Bundle ────────────────────────────────────────────────────────────

        // Carrega o bundle com o AssetBundleLoader do jogo. Quem recebe o
        // loader é responsável pelo Dispose (sem ele, o finalizer do jogo loga
        // warning e manda evento de erro pro analytics).
        private static IEnumerator LoadBundle(Item item, Action<AssetBundleLoader, string> done)
        {
            AssetBundleLoader loader = null;
            bool   finished = false;
            string error    = null;

            try
            {
                loader = new AssetBundleLoader(item.Bundle);
                loader.onComplete += _ => finished = true;
                loader.onError    += msg => { error = msg ?? "erro"; finished = true; };
                loader.Load();   // pode completar na hora se o bundle já estiver carregado
            }
            catch (Exception ex)
            {
                error    = ex.Message;
                finished = true;
            }

            float waited = 0f, timeout = InvConfig.LoadTimeout.Value;
            while (!finished && waited < timeout)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!finished) error = "timeout";
            if (error != null)
            {
                try { loader?.Dispose(); } catch { }
                loader = null;
            }
            done(loader, error);
        }

        private static IEnumerator WaitForStudio()
        {
            while (Studio.Instance != null && Studio.Instance.Busy) yield return null;
            Studio.Instance?.Acquire();
        }

        // ── Estratégia Sprite (armas, capas) ──────────────────────────────────

        private IEnumerator LoadSprite(Entry e)
        {
            AssetBundleLoader loader = null;
            string error = null;
            yield return LoadBundle(e.Item, (l, err) => { loader = l; error = err; });
            if (error != null) { Fail(e, error); yield break; }

            try     { Extract(e, loader); }
            finally { try { loader.Dispose(); } catch { } }
        }

        // ── Estratégia Object (pets, itens de casa) ───────────────────────────

        private IEnumerator LoadObject(Entry e)
        {
            AssetBundleLoader loader = null;
            string error = null;
            yield return LoadBundle(e.Item, (l, err) => { loader = l; error = err; });
            if (error != null) { Fail(e, error); yield break; }

            yield return WaitForStudio();
            GameObject inst = null;
            try
            {
                var prefab = loader.getGameObject(e.Item.PrefabName);
                if (prefab == null) { Fail(e, $"prefab '{e.Item.PrefabName}' não está no bundle"); yield break; }

                inst = Instantiate(prefab, Studio.Instance.Stage, false);
                inst.transform.localPosition = Vector3.zero;
                Studio.Instance.SetLayerRecursive(inst, Studio.Instance.Layer);

                // deixa Awake/Start/Animator posicionarem as peças
                yield return null;
                yield return null;

                Texture2D tex = null;
                yield return Studio.Instance.Shoot(inst, null, (t, err) => { tex = t; error = err; });
                if (tex != null) Finish(e, tex, "studio:objeto");
                else Fail(e, error ?? "foto falhou");
            }
            finally
            {
                if (inst != null) Destroy(inst);
                Studio.Instance?.Release();
                try { loader.Dispose(); } catch { }
            }
        }

        // ── Estratégia Clone (capacete, armadura, classe) ─────────────────────

        // Mesmo caminho do PreviewSpriteImage.ShowPlayerClone: um Player clone
        // do personagem com só o item (e, para capacete, a armadura/classe
        // atual, como o jogo faz). O clone carrega os próprios bundles.
        private IEnumerator LoadClone(Entry e)
        {
            var main = Entity.mainPlayer;
            if (main == null) { Fail(e, "personagem não carregado"); yield break; }

            yield return WaitForStudio();
            Player clone = null;
            try
            {
                var equips = new Dictionary<EquipSpots, EquipItem> { { e.Item.EquipSpot, e.Item } };
                if (e.Item.EquipSpot == EquipSpots.Head)
                {
                    if (main.Armor != null)      equips[EquipSpots.Armor] = main.Armor;
                    else if (main.Class != null) equips[EquipSpots.Class] = main.Class;
                }

                GameObject loaded = null;
                clone = new Player(main, Studio.Instance.Stage, equips);
                KeepInStudio(clone);   // antes de qualquer peça ser criada
                clone.showHelm = true;
                clone.OnGameObjectLoaded += (id, go) => loaded = go;
                clone.createAvatar();
                KeepInStudio(clone);

                float waited = 0f, timeout = InvConfig.LoadTimeout.Value;
                while (loaded == null && waited < timeout)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                    KeepInStudio(clone);
                }
                if (loaded == null) { Fail(e, "clone não carregou (timeout)"); yield break; }

                // peças do item podem terminar de chegar logo depois
                float settle = 0f;
                while (settle < 0.35f)
                {
                    settle += Time.unscaledDeltaTime;
                    yield return null;
                    KeepInStudio(clone);
                }

                var root = clone.getGameObject() ?? loaded;
                Func<Bounds?> focus = null;
                if (e.Item.EquipSpot == EquipSpots.Head)
                    focus = () => HelmBounds(clone, root);

                Texture2D tex = null;
                string error = null;
                yield return Studio.Instance.Shoot(root, focus, (t, err) => { tex = t; error = err; });
                if (tex != null) Finish(e, tex, "studio:clone");
                else Fail(e, error ?? "foto falhou");
            }
            finally
            {
                try { clone?.Dispose(); } catch { }   // Dispose destrói o GameObject do clone
                Studio.Instance?.Release();
            }
        }

        // O construtor Player(main, pai, equips) faz eGO.transform.parent = pai,
        // que MANTÉM a posição de mundo: o clone nasceria na origem do mapa, à
        // vista da câmera do jogo (o "preview fantasma" piscando na tela). O
        // preview do jogo corrige com SetParent(spriteParent, false) quando o
        // clone termina de carregar; aqui corrigimos desde o primeiro frame —
        // no estúdio (5000,5000) e na layer que só a câmera do estúdio vê.
        // createAvatar pode reposicionar o eGO, então repete a cada frame.
        private static void KeepInStudio(Player clone)
        {
            var go = clone?.getGameObject();
            if (go == null || Studio.Instance == null) return;
            var t = go.transform;
            if (t.parent != Studio.Instance.Stage) t.SetParent(Studio.Instance.Stage, false);
            t.localPosition = Vector3.zero;
            Studio.Instance.SetLayerRecursive(go, Studio.Instance.Layer);
        }

        // Área do capacete: renderers sob CC.GetHelmPlace(); se não houver,
        // o terço de cima do personagem (cabeça).
        private static Bounds? HelmBounds(Player clone, GameObject root)
        {
            var cc = (clone.GetAvatar() as HumanoidAvatar)?.CC;
            var place = cc != null ? cc.GetHelmPlace() : null;
            var b = place != null ? Studio.RendererBounds(place) : null;
            if (b != null)
            {
                var hb = b.Value;
                hb.Expand(new Vector3(hb.size.x * 0.15f, hb.size.y * 0.15f, 0f));
                return hb;
            }

            var full = Studio.RendererBounds(root);
            if (full == null) return null;
            var f = full.Value;
            float h = f.size.y * 0.4f;
            return new Bounds(new Vector3(f.center.x, f.max.y - h / 2f, f.center.z), new Vector3(f.size.x, h, f.size.z));
        }

        private void Finish(Entry e, Texture2D tex, string source)
        {
            e.Sprite = SpriteExtractor.MakeSprite(tex);
            e.Owned  = true;
            e.State  = State.Ready;
            e.Info   = source;
            SaveToDisk(e, tex);
            CountReady++;
            Verbose($"OK  '{e.Item?.Name}' ← {source} ({tex.width}x{tex.height})");
        }

        private void Extract(Entry e, AssetBundleLoader loader)
        {
            var sprite = SpriteExtractor.FindSprite(loader, e.Item, out var source);
            if (sprite == null) { Fail(e, "nenhum sprite no bundle"); return; }

            Texture2D tex = null;
            try { tex = SpriteExtractor.CopyToTexture(sprite, TextureSize); }
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
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes($"v{CacheFormatVersion}|{TextureSize}|{key}"));
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
