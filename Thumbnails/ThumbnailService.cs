using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using HarmonyLib;
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
        private int _activeClones;
        private const int MaxClones = 2;
        private readonly bool[] _cloneSlots = new bool[MaxClones];
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
            Body,     // Full Set Preview: clone com o conjunto atual + o item no slot dele
        }

        // Como a linha aparece na lista mista
        internal enum Kind
        {
            Plain,    // linha original do jogo (sem arte)
            Wide,     // card horizontal: arte à esquerda, nome à direita
            Tall,     // card vertical (Full Set): figura em cima, nome embaixo
        }

        internal static Kind KindFor(Item item)
        {
            if (KeyFor(item) == null) return Kind.Plain;
            return EffectiveStrategy(item) == Strategy.Body ? Kind.Tall : Kind.Wide;
        }

        // slots que aparecem no corpo do personagem
        internal static bool IsBodySpot(EquipSpots spot)
        {
            switch (spot)
            {
                case EquipSpots.Weapon: case EquipSpots.Head: case EquipSpots.Back:
                case EquipSpots.Armor:  case EquipSpots.Class: case EquipSpots.Pet:
                    return true;
                default:
                    return false;
            }
        }

        internal static Strategy EffectiveStrategy(Item item)
        {
            if (InvConfig.FullBodyMode && IsBodySpot(item.EquipSpot)) return Strategy.Body;
            return StrategyFor(item);
        }

        private static bool UsesClone(Item item)
        {
            var s = EffectiveStrategy(item);
            return s == Strategy.Clone || s == Strategy.Body;
        }

        // Conjunto que o clone do Full Set veste: o equipado agora, com o
        // item da lista no slot dele.
        //   - Classe: a armadura é removida — com armadura vestida, a classe
        //     não aparece (a armadura cobre o corpo), então o card da classe
        //     mostra a classe de verdade.
        //   - Armadura: entra no lugar da atual (a classe fica, mas a armadura
        //     é que aparece, igual no jogo).
        //   - Demais (arma, capacete, capa, pet): só o slot do item muda.
        internal static Dictionary<EquipSpots, EquipItem> BuildOutfit(Item item)
        {
            var outfit = new Dictionary<EquipSpots, EquipItem>();
            var main = Entity.mainPlayer;
            if (main?.equips != null)
                foreach (var kv in main.equips)
                    if (kv.Value != null && IsBodySpot(kv.Key)) outfit[kv.Key] = kv.Value;

            if (item != null)
            {
                if (item.EquipSpot == EquipSpots.Class) outfit.Remove(EquipSpots.Armor);
                outfit[item.EquipSpot] = item;
            }
            return outfit;
        }

        // assinatura do conjunto (para a chave do cache e para perceber troca de equipamento)
        internal static string OutfitSignature(Item item)
        {
            var outfit = BuildOutfit(item);
            var sb = new StringBuilder();
            foreach (var spot in new[] { EquipSpots.Weapon, EquipSpots.Head, EquipSpots.Back,
                                         EquipSpots.Armor, EquipSpots.Class, EquipSpots.Pet })
                sb.Append(outfit.TryGetValue(spot, out var it) ? it.ID.ToString() : "-").Append(',');

            var main = Entity.mainPlayer;
            if (main != null)
                sb.Append(main.showHelm ? 'h' : '_').Append(main.showCloak ? 'c' : '_').Append(main.showPet ? 'p' : '_');
            return sb.ToString();
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

            var strategy = EffectiveStrategy(item);
            if (strategy != Strategy.Clone && strategy != Strategy.Body && item.EquipSpot != EquipSpots.Back
                && string.IsNullOrEmpty(item.PrefabName))
                return null;

            string file;
            try   { file = item.Bundle.VersionedFileName; }
            catch { file = item.Bundle.Filename; }
            // estratégia na chave: mudar a forma de extrair invalida o cache antigo
            var key = $"{strategy}{StrategyVersion(strategy)}|{file}|{item.PrefabName}|{(int)item.EquipSpot}";

            // capacete é fotografado vestido com a armadura/classe atual (como o
            // preview do jogo): trocar de armadura gera outra foto
            // o enquadramento também entra na chave: mudar HelmFrame refaz as fotos
            if (strategy == Strategy.Body)
                key += "|outfit" + OutfitSignature(item) + $"|fr{InvConfig.FullBodyZoom.Value:0.00}/{BodyFrameAspect():0.00}";
            else if (item.EquipSpot == EquipSpots.Head)
                key += $"|body{BodySignature()}|f{InvConfig.HelmFrame.Value:0.00}/{InvConfig.HelmOffsetY.Value:0.00}";
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

        private static int StrategyVersion(Strategy s) => s == Strategy.Sprite ? 1 : s == Strategy.Body ? 2 : 2;

        // proporção (largura/altura) do quadro da figura no card vertical, para
        // a foto preencher o quadro sem sobras (CardRow.ApplyTall: largura da
        // coluna - margens, altura do card - faixa do nome - margens)
        internal static float BodyFrameAspect()
        {
            int cols = Mathf.Clamp(InvConfig.FullBodyColumns.Value, 1, 4);
            // CardRow.ApplyTall: card com inset 3, quadro com margem 3 e faixa
            // do nome de 46px, arte com inset 6 dentro do quadro
            float w = 540f / cols - 24f;
            float h = Mathf.Clamp(InvConfig.FullBodyHeight.Value, 120f, 600f) - 67f;
            return Mathf.Clamp(w / Mathf.Max(1f, h), 0.3f, 3f);
        }

        // o que, se mudar, exige refazer fotos que dependem do equipamento
        private static string EquipSignature() =>
            InvConfig.FullBodyMode ? OutfitSignature(null) : BodySignature();

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
                var body = EquipSignature();
                if (_lastBody != null && body != _lastBody)
                {
                    Verbose($"armadura mudou ({_lastBody} → {body}), recarregando linhas");
                    ThumbRow.RebindAll();
                }
                _lastBody = body;
            }

            // clones (capacete/armadura/classe) têm limite próprio: são lentos e
            // um clone travado não pode segurar as armas/capas da fila
            int max = Mathf.Max(1, InvConfig.MaxConcurrent.Value);
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                if (i >= _queue.Count) continue;
                var e = _queue[i];
                bool clone = UsesClone(e.Item);
                if (clone ? _activeClones >= MaxClones : _active >= max) continue;

                _queue.RemoveAt(i);
                PruneWaiters(e);
                if (e.Waiters.Count == 0) { e.State = State.Idle; continue; }

                StartCoroutine(Load(e));
                if (_active >= max && _activeClones >= MaxClones) break;
            }
        }

        private IEnumerator Load(Entry e)
        {
            bool isClone = UsesClone(e.Item);
            if (isClone) _activeClones++; else _active++;
            e.State = State.Loading;

            IEnumerator job;
            switch (EffectiveStrategy(e.Item))
            {
                case Strategy.Body:
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

            if (isClone) _activeClones--; else _active--;
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
                inst.transform.localPosition = Studio.SlotPosition(-1);
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

            int slot = Array.IndexOf(_cloneSlots, false);
            if (slot < 0) slot = 0;
            _cloneSlots[slot] = true;
            var slotPos = Studio.SlotPosition(slot);

            Player clone = null;
            try
            {
                bool body = EffectiveStrategy(e.Item) == Strategy.Body;
                Dictionary<EquipSpots, EquipItem> equips;
                if (body)
                {
                    equips = BuildOutfit(e.Item);
                }
                else
                {
                    equips = new Dictionary<EquipSpots, EquipItem> { { e.Item.EquipSpot, e.Item } };
                    if (e.Item.EquipSpot == EquipSpots.Head)
                    {
                        if (main.Armor != null)      equips[EquipSpots.Armor] = main.Armor;
                        else if (main.Class != null) equips[EquipSpots.Class] = main.Class;
                    }
                }

                GameObject loaded = null;
                clone = new Player(main, Studio.Instance.Stage, equips);
                KeepInStudio(clone, slotPos, body);   // antes de qualquer peça ser criada
                // o slot do próprio item sempre aparece; o resto segue as
                // preferências do jogador (esconder capacete/capa/pet)
                clone.showHelm  = !body || main.showHelm  || e.Item.EquipSpot == EquipSpots.Head;
                clone.showCloak = !body || main.showCloak || e.Item.EquipSpot == EquipSpots.Back;
                clone.showPet   = !body || main.showPet   || e.Item.EquipSpot == EquipSpots.Pet;
                clone.OnGameObjectLoaded += (id, go) => loaded = go;
                clone.createAvatar();
                KeepInStudio(clone, slotPos, body);

                // Pronto = OnGameObjectLoaded do jogo. Mas às vezes a montagem do
                // avatar quebra no meio (ex.: CustomizableCharacter.setupBack com
                // prefab nulo) e esse evento nunca chega; antes isso segurava o
                // estúdio por LoadTimeout (30s) e travava todas as outras fotos.
                // Agora, se o avatar já tem peças visíveis e elas pararam de mudar
                // por StableTime, fotografa assim mesmo.
                float waited = 0f, timeout = Mathf.Min(InvConfig.LoadTimeout.Value, CloneTimeout);
                float stableFor = 0f;
                int lastCount = -1;
                while (loaded == null && waited < timeout)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                    KeepInStudio(clone, slotPos, body);

                    int count = VisibleRendererCount(clone.getGameObject());
                    if (count > 0 && count == lastCount) stableFor += Time.unscaledDeltaTime;
                    else stableFor = 0f;
                    lastCount = count;
                    if (waited >= MinCloneWait && stableFor >= StableTime) break;
                }
                if (loaded == null && lastCount <= 0) { Fail(e, "clone não carregou (timeout)"); yield break; }
                if (loaded == null) Verbose($"'{e.Item?.Name}': clone sem OnGameObjectLoaded, fotografando como está");

                // peças do item podem terminar de chegar logo depois
                float settle = 0f;
                while (settle < 0.35f)
                {
                    settle += Time.unscaledDeltaTime;
                    yield return null;
                    KeepInStudio(clone, slotPos, body);
                }

                var root = clone.getGameObject() ?? loaded;
                Func<Bounds?> focus = null;
                if (body)
                    focus = () => BodyBounds(clone, root);
                else if (e.Item.EquipSpot == EquipSpots.Head)
                    focus = () => HelmBounds(clone, root);

                // o estúdio (câmera) só é ocupado durante a foto em si
                yield return WaitForStudio();
                try
                {
                    Texture2D tex = null;
                    string error = null;
                    yield return Studio.Instance.Shoot(root, focus, (t, err) => { tex = t; error = err; });
                    if (tex != null) Finish(e, tex, body ? "studio:corpo" : "studio:clone");
                    else Fail(e, error ?? "foto falhou");
                }
                finally
                {
                    Studio.Instance?.Release();
                }
            }
            finally
            {
                // segurança: nunca deixar o Dispose do clone destruir o pet do
                // personagem (o jogo só reaproveita pet existente para o
                // mainPlayer, mas se um dia mudar, o pet real some)
                try
                {
                    if (clone != null && clone.petGO != null && main.petGO == clone.petGO) clone.petGO = null;
                }
                catch { }
                try { clone?.Dispose(); } catch { }   // Dispose destrói o GameObject do clone (e o pet dele)
                _cloneSlots[slot] = false;
            }
        }

        private const float CloneTimeout = 15f;
        private const float MinCloneWait = 2.5f;
        private const float StableTime   = 0.75f;

        private static int VisibleRendererCount(GameObject go)
        {
            if (go == null) return 0;
            int n = 0;
            foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                if (r.enabled && (!(r is SpriteRenderer sr) || sr.sprite != null)) n++;
            return n;
        }

        // O construtor Player(main, pai, equips) faz eGO.transform.parent = pai,
        // que MANTÉM a posição de mundo: o clone nasceria na origem do mapa, à
        // vista da câmera do jogo (o "preview fantasma" piscando na tela). O
        // preview do jogo corrige com SetParent(spriteParent, false) quando o
        // clone termina de carregar; aqui corrigimos desde o primeiro frame —
        // no estúdio (5000,5000) e na layer que só a câmera do estúdio vê.
        // createAvatar pode reposicionar o eGO, então repete a cada frame.
        // Cada clone fica na sua vaga (Studio.SlotPosition), longe dos outros:
        // a câmera enquadra só o alvo, então clones vizinhos não entram na foto.
        private static void KeepInStudio(Player clone, Vector3 slotPos, bool controlPet = false)
        {
            var go = clone?.getGameObject();
            if (go == null || Studio.Instance == null) return;
            var t = go.transform;
            if (t.parent != Studio.Instance.Stage) t.SetParent(Studio.Instance.Stage, false);
            t.localPosition = slotPos;
            Studio.Instance.SetLayerRecursive(go, Studio.Instance.Layer);

            // o pet não é filho do clone: o jogo cria solto e o faz seguir o
            // PetAttach (FollowerGameObject). Nasce perto do clone (já no
            // estúdio); aqui só garante a layer da câmera do estúdio.
            if (clone.petGO != null)
            {
                Studio.Instance.SetLayerRecursive(clone.petGO, Studio.Instance.Layer);
                if (controlPet) PinPet(clone);
            }
        }

        // O pet do clone, deixado com o jogo, sai errado na foto:
        //   - o clone não é o mainPlayer, então ApplyPetVisibilitySetting o trata
        //     como pet de OUTRO jogador e o desliga se "mostrar pets dos outros"
        //     estiver desligado;
        //   - FollowerGameObject/Walk o fazem andar atrás do PetAttach e o
        //     PetMaterialFader esmaece o pet ao "teleportar" (longe do dono).
        // Aqui: pet sempre ligado, sem seguir/andar/esmaecer, parado no PetAttach.
        private static void PinPet(Player clone)
        {
            var pet = clone.petGO;
            if (!pet.activeSelf) pet.SetActive(true);

            foreach (var mb in pet.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || !mb.enabled) continue;
                if (mb is FollowerGameObject || mb is Walk) mb.enabled = false;
                else if (mb is PetMaterialFader fader)
                {
                    try { AccessTools.Method(typeof(PetMaterialFader), "StopFade")?.Invoke(fader, null); } catch { }
                    try { AccessTools.Method(typeof(PetMaterialFader), "ResetMaterials")?.Invoke(fader, null); } catch { }
                    fader.enabled = false;
                }
            }

            var cc = (clone.GetAvatar() as HumanoidAvatar)?.CC;
            var attach = cc != null ? cc.PetAttach : null;
            if (attach != null)
            {
                var p = attach.transform.position;
                pet.transform.position = new Vector3(p.x, p.y, pet.transform.position.z);
            }
        }

        // Enquadramento do capacete com ESCALA FIXA: o quadro não depende do
        // tamanho do capacete (senão capacete grande sai "longe" e pequeno
        // "perto"). Tamanho = fração da altura do corpo sem o capacete — o corpo
        // é o mesmo para todos (mesma armadura/classe) — e centro = posição do
        // CC.GetHelmPlace(), que segue a cabeça. Capacetes muito grandes podem
        // passar da borda; ajuste em [Layout] HelmFrame / HelmOffsetY.
        private static Bounds? HelmBounds(Player clone, GameObject root)
        {
            var cc = (clone.GetAvatar() as HumanoidAvatar)?.CC;
            var place = cc != null ? cc.GetHelmPlace() : null;
            var body = BoundsExcluding(root, place != null ? place.transform : null);

            if (place != null && body != null)
            {
                float h = body.Value.size.y * Mathf.Clamp(InvConfig.HelmFrame.Value, 0.1f, 1.5f);
                float w = h * 1.25f;
                var c = place.transform.position + new Vector3(0f, h * InvConfig.HelmOffsetY.Value, 0f);
                return new Bounds(new Vector3(c.x, c.y, body.Value.center.z), new Vector3(w, h, body.Value.size.z));
            }

            // sem âncora da cabeça: terço de cima do personagem
            var full = Studio.RendererBounds(root);
            if (full == null) return null;
            var f = full.Value;
            float fh = f.size.y * 0.4f;
            return new Bounds(new Vector3(f.center.x, f.max.y - fh / 2f, f.center.z), new Vector3(f.size.x, fh, f.size.z));
        }

        // Full Set: corpo inteiro + pet (que não é filho do clone)
        // Enquadramento com ESCALA FIXA (como nos capacetes): o quadro sai da
        // altura do personagem — pés (origem do eGO, no chão) até a cabeça
        // (CC.GetHelmPlace) — vezes FullBodyZoom, na proporção do quadro do
        // card. Arma grande, capa ou pet não mudam o tamanho do personagem;
        // o que passar da borda é cortado. Sem âncora da cabeça, cai no
        // enquadramento antigo (tudo que aparece).
        private static Bounds? BodyBounds(Player clone, GameObject root)
        {
            var cc = (clone.GetAvatar() as HumanoidAvatar)?.CC;
            var head = cc != null ? cc.GetHelmPlace() : null;
            if (head != null)
            {
                float feetY = root.transform.position.y;
                var hp = head.transform.position;
                float span = hp.y - feetY;
                if (span > 0.5f)
                {
                    float h = span * Mathf.Clamp(InvConfig.FullBodyZoom.Value, 1f, 4f);
                    float w = h * BodyFrameAspect();
                    float bottom = feetY - h * 0.08f;
                    return new Bounds(new Vector3(hp.x, bottom + h / 2f, hp.z), new Vector3(w, h, 10f));
                }
            }
            return LooseBodyBounds(clone, root);
        }

        private static Bounds? LooseBodyBounds(Player clone, GameObject root)
        {
            var b = Studio.RendererBounds(root);
            var pet = clone.petGO != null ? Studio.RendererBounds(clone.petGO) : null;
            if (b == null) return pet;
            if (pet == null) return b;
            var u = b.Value;
            // pet que se afastou demais (andando) não estica a foto inteira
            if (Vector3.Distance(pet.Value.center, u.center) < u.size.magnitude * 1.5f) u.Encapsulate(pet.Value);
            return u;
        }

        // como Studio.RendererBounds, ignorando o que estiver sob "skip"
        private static Bounds? BoundsExcluding(GameObject root, Transform skip)
        {
            Bounds? acc = null;
            foreach (var r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled || (skip != null && r.transform.IsChildOf(skip))) continue;
                var n = r.GetType().Name;
                if (n == "ParticleSystemRenderer" || n == "TrailRenderer" || n == "LineRenderer") continue;
                if (r is SpriteRenderer sr && (sr.sprite == null || sr.color.a <= 0.01f)) continue;
                if (r.bounds.size.sqrMagnitude <= 0f) continue;
                if (acc == null) acc = r.bounds;
                else { var a = acc.Value; a.Encapsulate(r.bounds); acc = a; }
            }
            return acc;
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
            _log.LogWarning($"[InvThumbs] Sem arte para '{e.Item?.Name}' ({e.Item?.EquipSpot}): {reason}");
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
