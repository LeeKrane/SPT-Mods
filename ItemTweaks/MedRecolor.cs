// Client side of the ItemTweaks4Server clones that reuse a model with other colors: Propital-X (light orange instead of yellow),
// Adrenaline-X (dark orange instead of red), Zagustin-X (dark violet instead of purple), SJ6-X (cyan instead of blue), Nightfall (the
// Surv12 roll-up kit, much darker) and .300 Winchester Magnum CBJ (the WTT-Armory FMJ cartridge, darker).
//
// A tint cannot do this (multiplying red never gives orange), so the albedo itself is recolored: the model's _MainTex (bundle
// textures are not CPU-readable) is blitted to an sRGB RenderTexture, read back as raw sRGB bytes (no color conversion in either path),
// recolored per pixel in HSV and set on the renderer with a MaterialPropertyBlock (shared materials untouched). One copy per texture
// name and item, made once per session and kept: the bundle texture can be unloaded and reloaded between raids (new instance, same
// name and pixels), the copy stays valid.
// Loot, inventory and icon models need the copy at once (icons are cached per template, the sync ObjectsFactory.CreateItem takes no
// result from an unfinished task): ReadPixels plus a recolor on all cores, a one-time hitch per item per session, usually in the
// stash. If a background recolor already has its pixels, that one is finished instead. In-hands models (the in-raid case: another
// player injects one) never wait: AsyncGPUReadback, recolor on a worker thread, upload on the main thread; the model keeps its vanilla
// colors until then and is skipped if the pooled model was handed out again in the meantime. A background recolor that fails or never
// returns (10 s) is dropped and retried on the next request (not after 3 failures); only a failed synchronous recolor is final. No
// graphics device (headless client): nothing is done.
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
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Krane.ItemTweaks
{
    internal static class MedRecolor
    {
        private sealed class Spec
        {
            internal string[] Textures;
            internal Func<Color32, Color32> Map;
            // Material colors (_SpecColor, _ReflectColor) multiplied through the property block, for items whose reflections
            // would otherwise keep the model light after the albedo is darkened.
            internal (int Id, float Factor)[] ColorFactors;
        }

        // Hue ranges and targets in degrees, tuned on the vanilla textures (stimulator_* 1024x1024, roll-up kit diffuse).
        private static readonly Dictionary<MongoID, Spec> Specs = new Dictionary<MongoID, Spec>
        {
            // Propital-X: yellow (about 50 degrees) to light orange.
            { (MongoID)"3d8e6f5ae7ea46f17f0fa1a5", new Spec { Textures = new[] { "stimulator_propital" }, Map = c => HueShift(c, 50f, 30f, 32f, 0.8f, 1.3f) } },
            // Adrenaline-X: red to dark orange.
            { (MongoID)"746f40f23c943aa94650ab0b", new Spec { Textures = new[] { "stimulator_adrenaline" }, Map = c => HueShift(c, 0f, 25f, 24f, 1f, 1.15f) } },
            // Zagustin-X: purple (about 275 degrees) to dark violet.
            { (MongoID)"3f5582d3b2ae5f2af6083599", new Spec { Textures = new[] { "stimulator_zagustin" }, Map = c => HueShift(c, 275f, 35f, 268f, 1f, 0.6f) } },
            // SJ6-X: blue (about 220 degrees) to cyan.
            { (MongoID)"99bdf12eeb8cff157a1ad9cf", new Spec { Textures = new[] { "stimulator_SJ6" }, Map = c => HueShift(c, 220f, 45f, 185f, 1f, 1.1f) } },
            // Nightfall: the whole kit (red cross too) multiplied by 0.35, darker than the Redline dossier's default 0.5 tint. The vanilla
            // Surv12 albedo is already dark (mean about 32/255, only the buckles are light), so the material's specular and cube
            // reflection (_SpecColor 0.74, _ReflectColor alpha 0.5) are scaled down as well, else they keep the model looking light.
            { (MongoID)"6190dcad54cbff2ac9b7b8f4", new Spec
                {
                    Textures = new[] { "item_meds_survival_first_aid_rollup_kit_LOD0_diff" },
                    Map = c => Darken(c, 0.35f),
                    ColorFactors = new[] { (Shader.PropertyToID("_SpecColor"), 0.35f), (Shader.PropertyToID("_ReflectColor"), 0.35f) },
                } },
            // .300 Winchester Magnum CBJ: the WTT-Armory FMJ bullet and casing darkened.
            { (MongoID)"5efdb8528bd5573c2e4faf1d", new Spec { Textures = new[] { "Bullet.001Diffuse", "Casing.002Diffuse" }, Map = c => Darken(c, 0.45f) } },
        };

        // One entry per (texture name, item). Result once made; Failed after a failed synchronous recolor (final). Pending while a
        // background recolor runs (Generation tells a stale callback apart, Mapping holds its worker once the pixels are read back).
        // Waiters get the copy, or null when it could not be made.
        private sealed class Recolor
        {
            internal Texture2D Result;
            internal bool Failed;
            internal bool Pending;
            internal float StartedAt;
            internal int Generation;
            internal int AsyncFailures;
            internal Task<Color32[]> Mapping;
            // Copy factory of the source the background recolor read (its size matches Mapping's pixels).
            internal Func<Texture2D> MappingCopy;
            // Temporary RenderTexture of the background readback, released by its callback or when the recolor is dropped.
            internal RenderTexture PendingRt;
            internal readonly List<Action<Texture2D>> Waiters = new List<Action<Texture2D>>();
        }

        private const float AsyncTimeout = 10f;
        private const int MaxAsyncFailures = 3;
        private const GraphicsFormat Format = GraphicsFormat.R8G8B8A8_SRGB;

        private static readonly Dictionary<(string Texture, MongoID Item), Recolor> Recolors = new Dictionary<(string, MongoID), Recolor>();
        private static bool? _hasGpu;

        private static bool HasGpu => _hasGpu ?? (_hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null).Value;

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(MedRecolor), AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateItemUsablePrefabAsync)),
                postfix: nameof(UsablePrefabPostfix));
        }

        // The pool usually returns a completed task and the caller uses the model at once (the sync CreateItemUsablePrefab unwraps it),
        // so the task is never replaced: a finished model is handled now, an unfinished one when its task completes. Nothing here may
        // throw into the game's in-hands creation.
        private static void UsablePrefabPostfix(Item item, Task<GameObject> __result)
        {
            try
            {
                if (item == null || __result == null) return;
                var template = item.TemplateId;
                if (__result.IsCompleted)
                    Handle(__result, template);
                else
                    __result.ContinueWith(t => Handle(t, template), TaskContinuationOptions.ExecuteSynchronously);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static void Handle(Task<GameObject> task, MongoID template)
        {
            if (task.Status != TaskStatus.RanToCompletion || ReferenceEquals(task.Result, null)) return;
            var go = task.Result;
            // Hot path (every in-hands model of every player): items without a spec only need a restore (a no-op without our marker).
            if (!Specs.ContainsKey(template) && Plugin.IsMainThread)
            {
                try
                {
                    if (go != null) PropertyBlockMarker.Restore(go);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError(e);
                }
                return;
            }
            Plugin.RunOnMainThread(() =>
            {
                try
                {
                    if (go == null) return;
                    PropertyBlockMarker.Restore(go);
                    Apply(go, template, sync: false);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError(e);
                }
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
                    if (source == null || Array.IndexOf(spec.Textures, source.name) < 0) continue;
                    // The marker doubles as the model's identity: Restore destroys it when the pooled model is handed out again,
                    // so a late copy is only set if this marker is still the model's.
                    if (marker == null) marker = PropertyBlockMarker.For(go);
                    var renderer = r;
                    var index = i;
                    var mine = marker;
                    if (spec.ColorFactors != null)
                    {
                        var block = mine.Edit(renderer, index);
                        foreach (var (id, factor) in spec.ColorFactors)
                        {
                            if (!m.HasProperty(id)) continue;
                            var color = m.GetColor(id);
                            block.SetColor(id, new Color(color.r * factor, color.g * factor, color.b * factor, color.a));
                        }
                        renderer.SetPropertyBlock(block, index);
                    }
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

        // Calls done (main thread) with the recolored copy, or null when it cannot be made: now when ready (sync: made now if needed),
        // else once the background recolor finishes.
        private static void WhenRecolored(Texture source, MongoID template, Spec spec, bool sync, Action<Texture2D> done)
        {
            var key = (source.name, template);
            if (!Recolors.TryGetValue(key, out var recolor))
            {
                recolor = new Recolor();
                Recolors[key] = recolor;
            }
            if (recolor.Result != null)
            {
                done(recolor.Result);
                return;
            }
            if (recolor.Failed)
            {
                done(null);
                return;
            }
            if (recolor.Pending && Time.realtimeSinceStartup - recolor.StartedAt > AsyncTimeout)
                DropAsync(recolor, new TimeoutException("GPU readback of " + source.name + " never returned"));

            if (sync)
            {
                MakeSync(source, spec, recolor);
                done(recolor.Result);
                return;
            }
            if (recolor.AsyncFailures >= MaxAsyncFailures)
            {
                done(null);
                return;
            }
            recolor.Waiters.Add(done);
            if (recolor.Pending) return;
            try
            {
                StartAsync(source, spec, recolor);
            }
            catch (Exception e)
            {
                DropAsync(recolor, e);
            }
        }

        // Synchronous copy: the background recolor's pixels when it already has them, else read back now. A failure here is final.
        private static void MakeSync(Texture source, Spec spec, Recolor recolor)
        {
            try
            {
                Color32[] ready = null;
                var readyCopy = recolor.MappingCopy;
                if (recolor.Pending && recolor.Mapping != null)
                {
                    try { ready = recolor.Mapping.Result; }
                    catch { ready = null; }
                }
                // Supersede the background recolor: its callbacks see another generation and stop (its readback callback still
                // releases its RenderTexture).
                recolor.Pending = false;
                recolor.Mapping = null;
                recolor.MappingCopy = null;
                recolor.Generation++;
                Finish(recolor, ready != null && readyCopy != null ? Upload(readyCopy, ready) : ReadNow(source, spec));
            }
            catch (Exception e)
            {
                recolor.Failed = true;
                Plugin.Log.LogError(e);
                Notify(recolor, null);
            }
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

        private static RenderTexture BlitToTemporary(Texture source)
        {
            var rt = RenderTexture.GetTemporary(new RenderTextureDescriptor(source.width, source.height, Format, 0));
            try
            {
                Graphics.Blit(source, rt);
                return rt;
            }
            catch
            {
                RenderTexture.ReleaseTemporary(rt);
                throw;
            }
        }

        private static Texture2D ReadNow(Texture source, Spec spec)
        {
            var rt = BlitToTemporary(source);
            var previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                RenderTexture.active = rt;
                copy = CopyFactory(source)();
                copy.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
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

        private static Texture2D Upload(Func<Texture2D> newCopy, Color32[] pixels)
        {
            var copy = newCopy();
            try
            {
                copy.SetPixels32(pixels);
                copy.Apply(true, true);
                return copy;
            }
            catch
            {
                UnityEngine.Object.Destroy(copy);
                throw;
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

        // Background recolor: readback in the requested sRGB format (raw bytes, as ReadPixels), recolor on a worker, upload on the main thread.
        private static void StartAsync(Texture source, Spec spec, Recolor recolor)
        {
            var generation = ++recolor.Generation;
            var name = source.name;
            var newCopy = CopyFactory(source);
            var rt = BlitToTemporary(source);
            recolor.Pending = true;
            recolor.StartedAt = Time.realtimeSinceStartup;
            recolor.Mapping = null;
            recolor.MappingCopy = null;
            recolor.PendingRt = rt;
            try
            {
                AsyncGPUReadback.Request(rt, 0, Format, request =>
                {
                    Color32[] pixels = null;
                    try
                    {
                        if (!request.hasError) pixels = request.GetData<Color32>().ToArray();
                    }
                    finally
                    {
                        // Not if DropAsync already released it (timeout).
                        if (ReferenceEquals(recolor.PendingRt, rt))
                        {
                            recolor.PendingRt = null;
                            RenderTexture.ReleaseTemporary(rt);
                        }
                    }
                    if (recolor.Generation != generation || !recolor.Pending) return;
                    if (pixels == null)
                    {
                        DropAsync(recolor, new InvalidOperationException("GPU readback of " + name + " failed"));
                        return;
                    }
                    recolor.MappingCopy = newCopy;
                    recolor.Mapping = Task.Run(() =>
                    {
                        MapAll(pixels, spec);
                        return pixels;
                    });
                    recolor.Mapping.ContinueWith(t => Plugin.RunOnMainThread(() =>
                    {
                        if (recolor.Generation != generation || !recolor.Pending) return;
                        if (t.IsFaulted)
                        {
                            DropAsync(recolor, t.Exception);
                            return;
                        }
                        try
                        {
                            recolor.Pending = false;
                            recolor.Mapping = null;
                            recolor.MappingCopy = null;
                            Finish(recolor, Upload(newCopy, t.Result));
                        }
                        catch (Exception e)
                        {
                            DropAsync(recolor, e);
                        }
                    }));
                });
            }
            catch
            {
                recolor.PendingRt = null;
                RenderTexture.ReleaseTemporary(rt);
                throw;
            }
        }

        // A background recolor that failed or got lost: its waiters get null (their models keep vanilla colors), the next request
        // retries. Generation moves on so a late callback of it does nothing.
        private static void DropAsync(Recolor recolor, Exception e)
        {
            recolor.Pending = false;
            recolor.Mapping = null;
            recolor.MappingCopy = null;
            recolor.Generation++;
            recolor.AsyncFailures++;
            if (recolor.PendingRt != null)
            {
                RenderTexture.ReleaseTemporary(recolor.PendingRt);
                recolor.PendingRt = null;
            }
            Plugin.Log.LogWarning(e);
            Notify(recolor, null);
        }

        private static void Finish(Recolor recolor, Texture2D copy)
        {
            recolor.Result = copy;
            Notify(recolor, copy);
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

        // Value scaled, so the color keeps its hue but gets darker.
        private static Color32 Darken(Color32 c, float factor)
        {
            return new Color32((byte)(c.r * factor), (byte)(c.g * factor), (byte)(c.b * factor), c.a);
        }
    }
}
