// Client side of the ItemTweaks4Server med clones that reuse a vanilla model with other colors: Propital II (light orange instead
// of yellow), Adrenaline II (dark orange instead of red), Zagustin II (dark violet instead of purple) and Surv16 (light gray
// instead of the dark Surv12 roll-up kit).
//
// A tint cannot do this (multiplying red never gives orange), so the albedo itself is recolored: the model's _MainTex (bundle
// textures are not CPU-readable) is blitted to a RenderTexture, read back, recolored per pixel in HSV and set on the renderer with a
// MaterialPropertyBlock (shared materials untouched). One recolored copy per source texture and item, made once and kept.
// Only materials whose _MainTex has the item's texture name are touched: the in-hands model is the arms rig with the syringe or kit
// as a child, and the arms must stay as they are.
//
// Models: loot, inventory and icon models come from ObjectsFactory.CreateItemAsync (Plugin.CreateItemPostfix calls Apply), the
// in-hands model (first and third person) from ObjectsFactory.CreateItemUsablePrefabAsync, which returns a completed task. Both are
// pooled per bundle and shared with the vanilla item, so every reuse first restores the property blocks saved before recoloring.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

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

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
        private static readonly Dictionary<(int Source, MongoID Item), Texture2D> Cache = new Dictionary<(int, MongoID), Texture2D>();

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(MedRecolor), AccessTools.Method(typeof(ObjectsFactory), nameof(ObjectsFactory.CreateItemUsablePrefabAsync)),
                postfix: nameof(UsablePrefabPostfix));
        }

        // The task is already completed (the prefab comes from the pool synchronously) and the caller uses the model right away.
        private static void UsablePrefabPostfix(Item item, Task<GameObject> __result)
        {
            if (item == null || __result == null || !__result.IsCompleted || __result.IsFaulted) return;
            GameObject go = __result.Result;
            if (go == null) return;
            var template = item.TemplateId;
            Plugin.RunOnMainThread(() => Apply(go, template));
        }

        // Restores a pooled model, then recolors it when the template has a spec. Main thread only.
        internal static void Apply(GameObject go, MongoID template)
        {
            var marker = go.GetComponent<RecolorMarker>();
            if (marker != null)
            {
                foreach (var (r, i, original) in marker.Saved)
                {
                    if (r != null) r.SetPropertyBlock(original, i);
                }
                UnityEngine.Object.DestroyImmediate(marker);
                marker = null;
            }
            if (!Specs.TryGetValue(template, out var spec)) return;

            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || !m.HasProperty(MainTexId)) continue;
                    var source = m.GetTexture(MainTexId);
                    if (source == null || source.name != spec.Texture) continue;
                    var recolored = Recolored(source, template, spec);
                    if (recolored == null) continue;

                    var mpb = new MaterialPropertyBlock();
                    MaterialPropertyBlock original = null;
                    if (r.HasPropertyBlock())
                    {
                        r.GetPropertyBlock(mpb, i);
                        if (!mpb.isEmpty)
                        {
                            original = new MaterialPropertyBlock();
                            r.GetPropertyBlock(original, i);
                        }
                    }
                    if (marker == null) marker = go.AddComponent<RecolorMarker>();
                    marker.Saved.Add((r, i, original));
                    mpb.SetTexture(MainTexId, recolored);
                    r.SetPropertyBlock(mpb, i);
                }
            }
        }

        private sealed class RecolorMarker : MonoBehaviour
        {
            internal readonly List<(Renderer Renderer, int Index, MaterialPropertyBlock Original)> Saved = new List<(Renderer, int, MaterialPropertyBlock)>();
        }

        private static Texture2D Recolored(Texture source, MongoID template, Spec spec)
        {
            var key = (source.GetInstanceID(), template);
            if (Cache.TryGetValue(key, out var cached) && cached != null) return cached;
            try
            {
                int w = source.width, h = source.height;
                var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var previous = RenderTexture.active;
                Texture2D copy;
                try
                {
                    Graphics.Blit(source, rt);
                    RenderTexture.active = rt;
                    copy = new Texture2D(w, h, TextureFormat.RGBA32, true, false);
                    copy.ReadPixels(new Rect(0, 0, w, h), 0, 0, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                    RenderTexture.ReleaseTemporary(rt);
                }
                var pixels = copy.GetPixels32();
                for (int p = 0; p < pixels.Length; p++) pixels[p] = spec.Map(pixels[p]);
                copy.SetPixels32(pixels);
                copy.Apply(true, true);
                copy.name = source.name + "_krane";
                copy.wrapMode = source.wrapMode;
                copy.filterMode = source.filterMode;
                copy.anisoLevel = source.anisoLevel;
                copy.hideFlags = HideFlags.HideAndDontSave;
                Cache[key] = copy;
                return copy;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return null;
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
