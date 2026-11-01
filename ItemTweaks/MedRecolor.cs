// Client side of the ItemTweaks4Server med clones that reuse a vanilla model with other colors: Propital II (light orange instead
// of yellow), Adrenaline II (dark orange instead of red), Zagustin II (dark violet instead of purple) and Surv16 (light gray
// instead of the dark Surv12 roll-up kit).
//
// A tint cannot do this (multiplying red never gives orange), so the albedo itself is recolored: the model's _MainTex (bundle
// textures are not CPU-readable) is blitted to a RenderTexture, read back, recolored per pixel in HSV and set on the renderer with a
// MaterialPropertyBlock (shared materials untouched). One copy per texture name and item, made once per session and kept: the
// bundle texture can be unloaded and reloaded between raids (new instance, same name and pixels), the copy stays valid.
// Loot, inventory and icon models need the copy at once (icons are cached per template, the sync ObjectsFactory.CreateItem takes no
// result from an unfinished task): read back with ReadPixels, pixels recolored on all cores, a one-time hitch per item per session,
// usually in the stash. In-hands models (the in-raid case: another player injects one) never wait: AsyncGPUReadback, recolor on a
// worker thread, upload on the main thread; the model keeps its vanilla colors until then and is skipped if the pooled model was
// handed out again in the meantime. No graphics device (headless client): nothing is done.
// Only materials whose _MainTex has the item's texture name are touched: the in-hands model is the arms rig with the syringe or kit
// as a child, and the arms must stay as they are.
//
// Models: loot, inventory and icon models come from ObjectsFactory.CreateItemAsync (Plugin.CreateItemPostfix restores and calls Apply),
// the in-hands model (first and third person) from ObjectsFactory.CreateItemUsablePrefabAsync. Both are pooled per bundle and shared
// with the vanilla item; PropertyBlockMarker undoes our blocks on every reuse.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace Krane.ItemTweaks
{
    internal static class MedRecolor
    {
        private sealed class Spec
        {
            internal string Texture;
            internal Func<Color32, Color32> Map;
        }

        // Hue ranges and targets in degrees, tuned on the vanilla textures (stimulator_* 1024x1024, roll-up kit diffuse).
        private static readonly Dictionary<MongoID, Spec> Specs = new Dictionary<MongoID, Spec>
        {
            // Propital II: yellow (about 50 degrees) to light orange.
            { (MongoID)"3d8e6f5ae7ea46f17f0fa1a5", new Spec { Texture = "stimulator_propital", Map = c => HueShift(c, 50f, 30f, 32f, 0.8f, 1.3f) } },
            // Adrenaline II: red to dark orange.
            { (MongoID)"746f40f23c943aa94650ab0b", new Spec { Texture = "stimulator_adrenaline", Map = c => HueShift(c, 0f, 25f, 24f, 1f, 1.15f) } },
            // Zagustin II: purple (about 275 degrees) to dark violet.
            { (MongoID)"3f5582d3b2ae5f2af6083599", new Spec { Texture = "stimulator_zagustin", Map = c => HueShift(c, 275f, 35f, 268f, 1f, 0.6f) } },
            // Surv16: the dark kit to light gray, the red cross stays.
            { (MongoID)"6190dcad54cbff2ac9b7b8f4", new Spec { Texture = "item_meds_survival_first_aid_rollup_kit_LOD0_diff", Map = c => LightGray(c, 0.38f, 1.4f) } },
        };

        // One entry per (texture name, item): Result once ready, Failed after an error (never retried), else in progress. Waiters get
        // the copy, or null after a failure.
        private sealed class Recolor
        {
            internal Texture2D Result;
            internal bool Failed;
            internal readonly List<Action<Texture2D>> Waiters = new List<Action<Texture2D>>();
        }

        private static readonly Dictionary<(string Texture, MongoID Item), Recolor> Recolors = new Dictionary<(string, MongoID), Recolor>();
        private static bool? _hasGpu;

        private static bool HasGpu => _hasGpu ?? (_hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null).Value;

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(MedRecolor), AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateItemUsablePrefabAsync)),
                postfix: nameof(UsablePrefabPostfix));
        }

        // The pool usually returns a completed task and the caller uses the model at once (the sync CreateItemUsablePrefab unwraps it),
        // so the task is never replaced: a finished model is handled now, an unfinished one when its task completes.
        private static void UsablePrefabPostfix(Item item, Task<GameObject> __result)
        {
            if (item == null || __result == null) return;
            var template = item.TemplateId;
            if (__result.IsCompleted)
                Handle(__result, template);
            else
                __result.ContinueWith(t => Handle(t, template), TaskContinuationOptions.ExecuteSynchronously);
        }

        private static void Handle(Task<GameObject> task, MongoID template)
        {
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
            var go = task.Result;
            Plugin.RunOnMainThread(() =>
            {
                PropertyBlockMarker.Restore(go);
                Apply(go, template, sync: false);
            });
        }

        // Recolors a model that was just restored, when the template has a spec. Main thread only. sync: the copy is made now if not
        // ready yet, else the model gets it once the background recolor is done.
        internal static void Apply(GameObject go, MongoID template, bool sync)
        {
            if (!Specs.TryGetValue(template, out var spec) || !HasGpu) return;
            PropertyBlockMarker marker = null;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(Plugin.MainTexId)) continue;
                    var source = m.GetTexture(Plugin.MainTexId);
                    if (source == null || source.name != spec.Texture) continue;
                    // The marker doubles as the model's identity: Restore destroys it when the pooled model is handed out again,
                    // so a late copy is only set if this marker is still the model's.
                    if (marker == null) marker = PropertyBlockMarker.For(go);
                    var renderer = r;
                    var index = i;
                    var mine = marker;
                    WhenRecolored(source, template, spec, sync, copy =>
                    {
                        if (copy == null || mine == null || go == null || renderer == null || go.GetComponent<PropertyBlockMarker>() != mine) return;
                        var mpb = mine.Edit(renderer, index);
                        mpb.SetTexture(Plugin.MainTexId, copy);
                        renderer.SetPropertyBlock(mpb, index);
                    });
                }
            }
        }

        // Calls done (main thread) with the recolored copy, or null after a failure: now when ready (sync: made now if needed), else
        // once the background recolor finishes.
        private static void WhenRecolored(Texture source, MongoID template, Spec spec, bool sync, Action<Texture2D> done)
        {
            var key = (source.name, template);
            if (!Recolors.TryGetValue(key, out var recolor))
            {
                recolor = new Recolor();
                Recolors[key] = recolor;
                if (!sync)
                {
                    recolor.Waiters.Add(done);
                    try { StartAsync(source, spec, recolor); }
                    catch (Exception e) { Fail(recolor, e); }
                    return;
                }
            }
            if (recolor.Result == null && !recolor.Failed && sync)
            {
                // Also when a background recolor is running: it is discarded when it finishes.
                try
                {
                    recolor.Result = MakeNow(source, spec);
                    Notify(recolor, recolor.Result);
                }
                catch (Exception e)
                {
                    Fail(recolor, e);
                }
            }
            if (recolor.Result != null) done(recolor.Result);
            else if (recolor.Failed) done(null);
            else recolor.Waiters.Add(done);
        }

        // Size and sampling of the source, read up front: the async path builds the copy after the source may have been unloaded.
        private static Func<Texture2D> CopyFactory(Texture source)
        {
            int w = source.width, h = source.height, aniso = source.anisoLevel;
            var name = source.name + "_krane";
            var wrap = source.wrapMode;
            var filter = source.filterMode;
            return () => new Texture2D(w, h, TextureFormat.RGBA32, true, false)
            {
                name = name,
                wrapMode = wrap,
                filterMode = filter,
                anisoLevel = aniso,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }

        private static Texture2D MakeNow(Texture source, Spec spec)
        {
            int w = source.width, h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = CopyFactory(source)();
                copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                RenderTexture.active = previous;
                var pixels = copy.GetPixels32();
                MapAll(pixels, spec);
                copy.SetPixels32(pixels);
                copy.Apply(true, true);
                return copy;
            }
            catch
            {
                if (copy != null) UnityEngine.Object.Destroy(copy);
                throw;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // Rows split over the cores; the HSV helpers are plain math, safe off the main thread.
        private static void MapAll(Color32[] pixels, Spec spec)
        {
            const int chunk = 16384;
            var parts = (pixels.Length + chunk - 1) / chunk;
            Parallel.For(0, parts, part =>
            {
                var end = Math.Min(pixels.Length, (part + 1) * chunk);
                for (int p = part * chunk; p < end; p++) pixels[p] = spec.Map(pixels[p]);
            });
        }

        private static void StartAsync(Texture source, Spec spec, Recolor recolor)
        {
            int w = source.width, h = source.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(source, rt);
            var name = source.name;
            var newCopy = CopyFactory(source);
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, request =>
            {
                var pixels = request.hasError ? null : request.GetData<Color32>().ToArray();
                RenderTexture.ReleaseTemporary(rt);
                if (pixels == null)
                {
                    Fail(recolor, new InvalidOperationException("GPU readback of " + name + " failed"));
                    return;
                }
                Task.Run(() => MapAll(pixels, spec)).ContinueWith(t => Plugin.RunOnMainThread(() =>
                {
                    // A sync recolor got there first (or failed): keep that outcome.
                    if (recolor.Result != null || recolor.Failed) return;
                    if (t.IsFaulted)
                    {
                        Fail(recolor, t.Exception);
                        return;
                    }
                    Texture2D copy = null;
                    try
                    {
                        copy = newCopy();
                        copy.SetPixels32(pixels);
                        copy.Apply(true, true);
                    }
                    catch (Exception e)
                    {
                        if (copy != null) UnityEngine.Object.Destroy(copy);
                        Fail(recolor, e);
                        return;
                    }
                    recolor.Result = copy;
                    Notify(recolor, copy);
                }));
            });
        }

        private static void Fail(Recolor recolor, Exception e)
        {
            recolor.Failed = true;
            Plugin.Log.LogError(e);
            Notify(recolor, null);
        }

        private static void Notify(Recolor recolor, Texture2D copy)
        {
            var waiters = recolor.Waiters.ToArray();
            recolor.Waiters.Clear();
            foreach (var waiter in waiters)
            {
                try { waiter(copy); }
                catch (Exception e) { Plugin.Log.LogError(e); }
            }
        }

        // Pixels within half degrees of center and saturated enough move to target (keeping half their hue offset), with saturation
        // and value scaled. Everything else (white labels, gray plastic) stays.
        private static Color32 HueShift(Color32 c, float center, float half, float target, float sat, float val)
        {
            Color.RGBToHSV(c, out var h, out var s, out var v);
            float dh = Mathf.DeltaAngle(center, h * 360f);
            if (Mathf.Abs(dh) > half || s < 0.2f) return c;
            float nh = Mathf.Repeat(target + dh * 0.5f, 360f) / 360f;
            Color32 o = Color.HSVToRGB(nh, Mathf.Clamp01(s * sat), Mathf.Clamp01(v * val));
            o.a = c.a;
            return o;
        }

        // Gray from luminance, lifted to a light gray; strongly colored bright pixels (the red cross) stay.
        private static Color32 LightGray(Color32 c, float lift, float gain)
        {
            Color.RGBToHSV(c, out _, out var s, out var v);
            if (s >= 0.5f && v > 0.3f) return c;
            float lum = (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;
            byte g = (byte)Mathf.RoundToInt(Mathf.Clamp01(lift + lum * gain) * 255f);
            return new Color32(g, g, g, c.a);
        }
    }
}
