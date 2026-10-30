// Client side of the two ItemTweaks4Server cases: the Spec-ops kit case (template f39e1020a6eb538f07653b87, a clone of the
// Dogtag case) and the Spec-ops key case (template c40d4da0b8d5434d00fffb4c, a clone of the Gingy keychain).
//
// Weight: all weight in the game funnels through ContainerExtensions.TotalWeight(IContainer) (sum of the
// children's TotalWeight), called by ContainerCollection.TotalWeight, which Inventory.TotalWeight/equipment
// weight/tooltips all recurse through. Nothing caches it per item; Inventory.TotalWeight is a Deferred that is
// marked dirty on every add/remove/refresh event under the equipment, so a postfix scaling the result for
// containers whose parent is one of the cases stays correct on every move (each case has its own multiplier). The cases' own weight is untouched.
//
// Look: the models are the Dogtag case's and the Gingy keychain's, from their own bundles and pools, so they are tinted per
// renderer with a MaterialPropertyBlock (no shared material is modified) right after ObjectsFactory.CreateItemAsync builds
// them. Spec-ops case: optional flat albedo replacement (light gray). Key case: multiply tint only (a darker keychain).
// Icons are rendered from that same model (ItemIconCreator.RenderModel -> CreateCleanLootPrefabAsync ->
// CreateItemAsync) and cached by template id, so they come out tinted too. Pooled models are cleaned again
// when they are reused for another template.
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Krane.ItemTweaks
{
    [BepInPlugin("krane.itemtweaks", "Krane's ItemTweaks", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string CaseId = "f39e1020a6eb538f07653b87";
        internal const string KeyCaseId = "c40d4da0b8d5434d00fffb4c";

        internal static ManualLogSource Log;
        private static readonly MongoID CaseMongoId = CaseId;
        private static readonly MongoID KeyCaseMongoId = KeyCaseId;

        // Per-case behavior: weight multiplier and look. Color/Flat are read live from the config. Multiply: multiply the material's own
        // color by the tint instead of replacing it.
        private sealed class CaseSpec
        {
            internal float WeightMultiplier = 0.1f;
            internal Func<Color> Color;
            internal Func<bool> Flat;
            internal bool Multiply;
        }

        private static readonly CaseSpec KitCase = new CaseSpec { Color = () => TintColor.Value, Flat = () => FlatTint.Value };
        private static readonly CaseSpec KeyCase = new CaseSpec { Color = () => KeyTintColor.Value, Flat = () => false, Multiply = true };
        private static readonly Dictionary<MongoID, CaseSpec> Cases = new Dictionary<MongoID, CaseSpec>
        {
            { CaseMongoId, KitCase },
            { KeyCaseMongoId, KeyCase },
        };

        internal static ConfigEntry<int> WeightReduction;
        internal static ConfigEntry<Color> TintColor;
        internal static ConfigEntry<bool> FlatTint;
        internal static ConfigEntry<int> KeyWeightReduction;
        internal static ConfigEntry<Color> KeyTintColor;

        private void Awake()
        {
            Log = Logger;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            WeightReduction = Config.Bind("Weight", "Content Weight Reduction", 90,
                new ConfigDescription("Spec-ops kit case: percent by which items stored in it are lighter than normal (90 = they weigh 10%). The case itself is unchanged.",
                    new AcceptableValueRange<int>(0, 95)));
            KitCase.WeightMultiplier = 1f - WeightReduction.Value / 100f;
            WeightReduction.SettingChanged += (_, __) =>
            {
                KitCase.WeightMultiplier = 1f - WeightReduction.Value / 100f;
                MarkWeightDirty();
            };
            KeyWeightReduction = Config.Bind("Weight", "Key Case Content Weight Reduction", 90,
                new ConfigDescription("Spec-ops key case: percent by which items stored in it are lighter than normal (90 = they weigh 10%). The case itself is unchanged.",
                    new AcceptableValueRange<int>(0, 95)));
            KeyCase.WeightMultiplier = 1f - KeyWeightReduction.Value / 100f;
            KeyWeightReduction.SettingChanged += (_, __) =>
            {
                KeyCase.WeightMultiplier = 1f - KeyWeightReduction.Value / 100f;
                MarkWeightDirty();
            };
            TintColor = Config.Bind("Look", "Case Color", new Color(0.93f, 0.93f, 0.93f, 1f),
                "Spec-ops kit case: color of the model and icon.");
            FlatTint = Config.Bind("Look", "Flat Color", true,
                "Spec-ops kit case: replace the albedo texture with a flat color so it comes out light gray. Off: only multiply the existing texture by the color (stays dark). Changes apply to icons generated after a game restart.");
            KeyTintColor = Config.Bind("Look", "Key Case Color", new Color(0.75f, 0.75f, 0.75f, 1f),
                "Spec-ops key case: color the Gingy keychain model and icon are multiplied by (darkens it). Changes apply to icons generated after a game restart.");

            var h = new Harmony("krane.itemtweaks");
            SafePatch(h, typeof(Plugin), AccessTools.Method(typeof(ContainerExtensions), nameof(ContainerExtensions.TotalWeight), new[] { typeof(IContainer) }),
                postfix: nameof(WeightPostfix));
            SafePatch(h, typeof(Plugin), AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateItemAsync)),
                postfix: nameof(CreateItemPostfix));
            try { RaidRepair.Init(Config, h); }
            catch (Exception e) { Log.LogError(e); }
            Log.LogInfo("ItemTweaks loaded");
        }

        // One failing patch target must not stop the other features.
        internal static void SafePatch(Harmony harmony, Type owner, MethodBase target, string prefix = null, string postfix = null, int? priority = null)
        {
            try
            {
                if (target == null) throw new MissingMethodException("patch target not found for " + (prefix ?? postfix));
                HarmonyMethod Make(string name)
                {
                    var m = new HarmonyMethod(owner, name);
                    if (priority.HasValue) m.priority = priority.Value;
                    return m;
                }
                harmony.Patch(target, prefix: prefix == null ? null : Make(prefix), postfix: postfix == null ? null : Make(postfix));
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

        private static void WeightPostfix(IContainer container, ref float __result)
        {
            Item parent = container?.ParentItem;
            if (parent == null) return;
            if (Cases.TryGetValue(parent.TemplateId, out var spec))
                __result *= spec.WeightMultiplier;
        }

        // Inventory.TotalWeight is a cached Deferred that is only dirtied by item events; invalidate it so a changed setting shows at once.
        private static void MarkWeightDirty()
        {
            try
            {
                if (!Singleton<GameWorld>.Instantiated) return;
                var inventory = Singleton<GameWorld>.Instance.MainPlayer?.Profile?.Inventory;
                if (inventory == null) return;
                inventory.TotalWeight.SetDirty();
                inventory.TotalWeightEliteSkill.SetDirty();
                inventory._onWeightUpdated.Invoke();
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
        }

        // Tint/Untint touch Unity objects, so they must run on the main thread. CreateItemAsync's own continuations are
        // resumed by Diz.Jobs.JobScheduler (JobAwaiter.OnCompleted -> AddContinuation), driven from the main thread, but the
        // Task we are handed can be awaited anywhere, so do not rely on that: marshal through a queue drained in Update.
        private static int _mainThread;
        private static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();

        private void Update()
        {
            while (MainThreadQueue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception e) { Log.LogError(e); }
            }
        }

        private static Task RunOnMainThread(Action action)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThread)
            {
                action();
                return Task.CompletedTask;
            }
            var tcs = new TaskCompletionSource<bool>();
            MainThreadQueue.Enqueue(() =>
            {
                try { action(); tcs.SetResult(true); }
                catch (Exception e) { tcs.SetException(e); }
            });
            return tcs.Task;
        }

        // Runs when the async method returns its Task: chain a continuation that edits the finished model.
        private static void CreateItemPostfix(Item item, ref Task<GameObject> __result)
        {
            if (item == null || __result == null) return;
            Cases.TryGetValue(item.TemplateId, out var spec);
            __result = Wrap(__result, spec);
        }

        private static async Task<GameObject> Wrap(Task<GameObject> task, CaseSpec spec)
        {
            GameObject go = await task;
            if (go == null) return go;
            try
            {
                await RunOnMainThread(() =>
                {
                    // Always start from the untinted state: a pooled model may carry the other case's tint.
                    Untint(go);
                    if (spec != null) Tint(go, spec.Color(), spec.Flat(), spec.Multiply);
                });
            }
            catch (Exception e)
            {
                Log.LogError(e);
            }
            return go;
        }

        // Remembers the property blocks the renderers had before tinting (null entry = none), so Untint restores exactly those.
        private class TintMarker : MonoBehaviour
        {
            internal readonly List<(Renderer Renderer, int Index, MaterialPropertyBlock Original)> Saved = new List<(Renderer, int, MaterialPropertyBlock)>();
        }

        private static Texture2D _white;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");

        private static void Tint(GameObject go, Color c, bool flat, bool multiply)
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _white.hideFlags = HideFlags.HideAndDontSave;
            }
            var marker = go.AddComponent<TintMarker>();
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    var mpb = new MaterialPropertyBlock();
                    if (r.HasPropertyBlock()) r.GetPropertyBlock(mpb, i);
                    MaterialPropertyBlock original = null;
                    if (!mpb.isEmpty)
                    {
                        original = new MaterialPropertyBlock();
                        r.GetPropertyBlock(original, i);
                    }
                    marker.Saved.Add((r, i, original));
                    if (m.HasProperty(ColorId)) mpb.SetColor(ColorId, multiply ? m.GetColor(ColorId) * c : c);
                    if (m.HasProperty(BaseColorId)) mpb.SetColor(BaseColorId, multiply ? m.GetColor(BaseColorId) * c : c);
                    if (flat)
                    {
                        if (m.HasProperty(MainTexId)) mpb.SetTexture(MainTexId, _white);
                        if (m.HasProperty(BaseMapId)) mpb.SetTexture(BaseMapId, _white);
                    }
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }

        // The models come from pools shared with the original items (and between our two cases): undo our tint when a
        // pooled, tinted instance is handed to another template.
        private static void Untint(GameObject go)
        {
            var marker = go.GetComponent<TintMarker>();
            if (marker == null) return;
            foreach (var (r, i, original) in marker.Saved)
            {
                if (r != null) r.SetPropertyBlock(original, i);
            }
            UnityEngine.Object.DestroyImmediate(marker);
        }
    }
}
