using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using Object = UnityEngine.Object;

namespace Krane.AdsCameraDistance
{
    // The lens shader ignores _MarkTex's tiling/offset, so the reticle cannot be shrunk through the material's
    // texture transform. Instead the reticle texture is redrawn shrunk and centred into a render texture that
    // replaces _MarkTex: the reticle is drawn in lens space and grows with the lens by ratio, so drawing it at
    // ratio^(growth - 1) of its size leaves it at ratio^growth of its vanilla on-screen size. Only shrinking is
    // done (factor <= 1): a lens smaller than vanilla (eye farther) keeps the original reticle.
    //
    // The redraw is a clamped blit (Unity handles the render-texture orientation), so the margin outside the
    // shrunk reticle repeats the texture's edge pixels: lines reaching the edge continue to the lens edge
    // (EdgeFill.Extend). With EdgeFill.Transparent the margin is then overwritten with transparent black, so
    // edge rings or tints are not smeared; that needs an alpha channel, so sources without one always extend.
    // The margin is symmetric around the centre, so it is orientation-independent.
    //
    // The scaled copy exists only while its lens is scaled: it is released when the lens is restored. Whatever
    // the game sets on _MarkTex itself (another reticle, the same one again, or none) is adopted as the new
    // original. Errors only pause the reticle scaling (own Fault); the lens image and FOV keep working.
    internal static class ReticleScaler
    {
        internal enum EdgeFill
        {
            // Repeat the texture's edge pixels into the margin: lines reaching the edge continue to the lens edge.
            Extend,
            // Leave the margin transparent: nothing is smeared, but such lines end before the lens edge.
            Transparent,
        }

        // 1 = reticle grows with the lens (no redraw), 0 = vanilla on-screen size.
        internal static float Growth;
        internal static EdgeFill Fill = EdgeFill.Extend;

        private static readonly int _markId = Shader.PropertyToID("_MarkTex");
        private static readonly Fault Fault = new Fault("Reticle scaling");
        // Redraw while the size changes by at least Threshold (at most every Interval), and once more exactly
        // when it has stayed within Threshold but off by at least Settle for SettleTime. Smaller jitter
        // (sway changes the eye-to-lens distance a little every frame) never redraws.
        private const float Threshold = 0.005f;
        private const float Interval = 0.05f;
        private const float Settle = 0.001f;
        private const float SettleTime = 0.25f;

        private static Material _clearMaterial;
        private static bool _noClearMaterial;

        private sealed class Entry
        {
            public Material Mat;
            // What the game has set on _MarkTex (may be null: no reticle).
            public Texture Original;
            // Present only while Applied.
            public RenderTexture Scaled;
            // Scaled is set on the material.
            public bool Applied;
            public float Drawn = 1f;
            public EdgeFill DrawnFill;
            public float LastDraw = -1f;
            public float PendingSince = -1f;
            // Formats the redraw cannot reproduce (alpha-only) keep the vanilla reticle.
            public bool Unsupported;
        }

        private static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        internal static void Apply(Material mat, float ratio)
        {
            if (Fault.Blocked)
                return;
            try
            {
                ApplyInner(mat, ratio);
                Fault.Ok();
            }
            catch (Exception e)
            {
                Fault.Hit(e);
                try
                {
                    Restore(mat);
                }
                catch
                {
                    // Already failing; the next retry restores or redraws.
                }
            }
        }

        private static void ApplyInner(Material mat, float ratio)
        {
            if (!mat.HasProperty(_markId))
                return;
            float factor = Growth >= 1f || ratio <= 1f ? 1f : Mathf.Pow(ratio, Growth - 1f);
            Texture current = mat.GetTexture(_markId);
            int id = mat.GetInstanceID();
            if (!_entries.TryGetValue(id, out Entry entry))
            {
                if (factor == 1f || current == null)
                    return;
                Sweep();
                entry = new Entry { Mat = mat, Original = current };
                _entries[id] = entry;
            }
            else if (entry.Applied ? current != entry.Scaled : current != entry.Original)
            {
                // The game set _MarkTex itself (mode or illumination switch, re-enable, reticle off): adopt it.
                entry.Original = current;
                entry.Unsupported = false;
                Unapply(entry, false);
            }

            if (factor == 1f || entry.Unsupported || entry.Original == null)
            {
                Unapply(entry, true);
                return;
            }

            float now = Time.unscaledTime;
            float diff = Mathf.Abs(factor - entry.Drawn);
            bool redraw = !entry.Applied || !entry.Scaled.IsCreated() || entry.DrawnFill != EffectiveFill(entry.Original);
            if (!redraw && diff >= Threshold)
                redraw = now - entry.LastDraw >= Interval;
            if (!redraw && diff >= Settle)
            {
                if (entry.PendingSince < 0f)
                    entry.PendingSince = now;
                redraw = now - entry.PendingSince >= SettleTime;
            }
            if (!redraw)
                return;

            if (!Redraw(entry, factor))
            {
                entry.Unsupported = true;
                Unapply(entry, true);
                return;
            }
            mat.SetTexture(_markId, entry.Scaled);
            entry.Applied = true;
            entry.Drawn = factor;
            entry.LastDraw = now;
            entry.PendingSince = -1f;
        }

        private static EdgeFill EffectiveFill(Texture src)
        {
            return Fill == EdgeFill.Transparent && GraphicsFormatUtility.HasAlphaChannel(src.graphicsFormat)
                ? EdgeFill.Transparent
                : EdgeFill.Extend;
        }

        private static bool Redraw(Entry entry, float factor)
        {
            Texture src = entry.Original;
            if (entry.Scaled == null)
            {
                GraphicsFormat format = TargetFormat(src.graphicsFormat);
                if (format == GraphicsFormat.None)
                {
                    Plugin.Log.LogWarning($"Reticle '{src.name}' has format {src.graphicsFormat}, which cannot be scaled; it keeps its vanilla size.");
                    return false;
                }
                entry.Scaled = new RenderTexture(src.width, src.height, 0, format)
                {
                    useMipMap = true,
                    autoGenerateMips = false,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Trilinear,
                    anisoLevel = src.anisoLevel,
                    name = src.name + "_krane_scaled",
                };
            }
            if (!entry.Scaled.IsCreated())
                entry.Scaled.Create();

            RenderTexture previous = RenderTexture.active;
            RenderTexture mipped = null;
            TextureWrapMode wrap = src.wrapMode;
            FilterMode filter = src.filterMode;
            try
            {
                // Shrinking samples several source texels per target texel; without source mips (or with point
                // filtering) thin lines would drop out, so sample a mip-mapped, trilinear copy instead.
                Texture sample = src;
                if (src.mipmapCount <= 1 || src.filterMode == FilterMode.Point)
                {
                    mipped = RenderTexture.GetTemporary(new RenderTextureDescriptor(src.width, src.height, entry.Scaled.graphicsFormat, 0)
                    {
                        useMipMap = true,
                        autoGenerateMips = false,
                    });
                    mipped.filterMode = FilterMode.Trilinear;
                    mipped.wrapMode = TextureWrapMode.Clamp;
                    Graphics.Blit(src, mipped);
                    mipped.GenerateMips();
                    sample = mipped;
                }
                else
                {
                    src.wrapMode = TextureWrapMode.Clamp;
                }

                // Destination uv u samples the source at u / factor + (0.5 - 0.5 / factor): centred, shrunk.
                float scale = 1f / factor;
                Graphics.Blit(sample, entry.Scaled, new Vector2(scale, scale), new Vector2(0.5f - 0.5f * scale, 0.5f - 0.5f * scale));
                EdgeFill fill = EffectiveFill(src);
                if (fill == EdgeFill.Transparent)
                    ClearMargin(entry.Scaled, factor);
                entry.DrawnFill = fill;
                entry.Scaled.GenerateMips();
            }
            finally
            {
                src.wrapMode = wrap;
                src.filterMode = filter;
                if (mipped != null)
                    RenderTexture.ReleaseTemporary(mipped);
                RenderTexture.active = previous;
            }
            return true;
        }

        // Same channel layout, colour space and range class as the source, as an uncompressed render format.
        // Alpha-only sources have no renderable equivalent that keeps the alpha channel: unsupported.
        // Sources without alpha sample as alpha 1, which the RGBA target reproduces.
        private static GraphicsFormat TargetFormat(GraphicsFormat source)
        {
            if (GraphicsFormatUtility.IsAlphaOnlyFormat(source))
                return GraphicsFormat.None;
            bool srgb = GraphicsFormatUtility.IsSRGBFormat(source);
            if (GraphicsFormatUtility.IsHDRFormat(source) || GraphicsFormatUtility.IsHalfFormat(source) || GraphicsFormatUtility.IsIEEE754Format(source))
                return GraphicsFormat.R16G16B16A16_SFloat;
            if (GraphicsFormatUtility.GetComponentCount(source) == 1)
                return GraphicsFormat.R8_UNorm;
            return srgb ? GraphicsFormat.R8G8B8A8_SRGB : GraphicsFormat.R8G8B8A8_UNorm;
        }

        // Overwrites the four bands outside the centred [0.5 - f/2, 0.5 + f/2] square with (0, 0, 0, 0).
        // factor is <= 1, so the bands are never inverted.
        private static void ClearMargin(RenderTexture target, float factor)
        {
            Material clear = ClearMaterial();
            if (clear == null)
                return;
            float a = 0.5f - 0.5f * factor;
            float b = 0.5f + 0.5f * factor;
            RenderTexture.active = target;
            GL.PushMatrix();
            GL.LoadOrtho();
            clear.SetPass(0);
            GL.Begin(GL.QUADS);
            Quad(0f, 0f, 1f, a);
            Quad(0f, b, 1f, 1f);
            Quad(0f, a, a, b);
            Quad(b, a, 1f, b);
            GL.End();
            GL.PopMatrix();
        }

        private static void Quad(float x0, float y0, float x1, float y1)
        {
            GL.Vertex3(x0, y0, 0f);
            GL.Vertex3(x0, y1, 0f);
            GL.Vertex3(x1, y1, 0f);
            GL.Vertex3(x1, y0, 0f);
        }

        // Unity's built-in colored shader, set to overwrite (no blending) with transparent black.
        private static Material ClearMaterial()
        {
            if (_clearMaterial != null || _noClearMaterial)
                return _clearMaterial;
            Shader shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _noClearMaterial = true;
                Plugin.Log.LogWarning("Hidden/Internal-Colored not found; reticle edge fill Transparent behaves like Extend.");
                return null;
            }
            _clearMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _clearMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _clearMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            _clearMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _clearMaterial.SetInt("_ZWrite", 0);
            _clearMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            _clearMaterial.SetColor("_Color", new Color(0f, 0f, 0f, 0f));
            return _clearMaterial;
        }

        // Puts the game's reticle back and frees the scaled copy. With restore false the game already replaced
        // the copy on the material, so only the copy is freed.
        private static void Unapply(Entry entry, bool restore)
        {
            if (entry.Applied && restore && entry.Mat != null && entry.Mat.GetTexture(_markId) == entry.Scaled)
                entry.Mat.SetTexture(_markId, entry.Original);
            entry.Applied = false;
            entry.Drawn = 1f;
            entry.PendingSince = -1f;
            if (entry.Scaled != null)
            {
                entry.Scaled.Release();
                Object.Destroy(entry.Scaled);
                entry.Scaled = null;
            }
        }

        // Drops entries whose lens material was destroyed mid-raid (dropped or discarded weapons). Runs when a
        // new lens is first scaled.
        private static void Sweep()
        {
            List<int> dead = null;
            foreach (KeyValuePair<int, Entry> pair in _entries)
            {
                if (pair.Value.Mat == null)
                    (dead ?? (dead = new List<int>())).Add(pair.Key);
            }
            if (dead == null)
                return;
            foreach (int id in dead)
            {
                Unapply(_entries[id], false);
                _entries.Remove(id);
            }
        }

        // The lens is no longer scaled (sight switch, un-aim): the game's reticle back, copy freed. A reticle
        // the game set in the meantime is left as it is.
        internal static void Restore(Material mat)
        {
            if (mat != null && _entries.TryGetValue(mat.GetInstanceID(), out Entry entry))
                Unapply(entry, true);
        }

        // Raid end: restore every reticle still set to a scaled copy and free the copies.
        internal static void Clear()
        {
            foreach (Entry entry in _entries.Values)
                Unapply(entry, true);
            _entries.Clear();
        }
    }
}
