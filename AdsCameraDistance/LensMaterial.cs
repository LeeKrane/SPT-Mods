using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;

namespace Krane.AdsCameraDistance
{
    // Scales the lens image (CW FX/OpticSight _Scales.x) together with LensFovPatch's widened optic FOV. The
    // widened FOV fills the larger lens but shrinks the target; enlarging the image by ratio^ImageStrength
    // brings it back to its vanilla on-screen size. The strength was tuned in-game (SR-25 + Razor at -0.03): a
    // full ratio left targets slightly larger than vanilla. ReticleScaler handles the reticle of the same lens.
    //
    // Only the lens currently aimed through is scaled: a lens that stops being current (sight switch, un-aim)
    // and every lens at raid end are restored to their original values.
    internal static class LensMaterial
    {
        private const float ImageStrength = 0.62f;
        private static readonly int _scalesId = Shader.PropertyToID("_Scales");

        private sealed class Entry
        {
            public Material Mat;
            public bool HasScales;
            public Vector4 Original;
            public float Written = 1f;
            // Created by this plugin (the renderer's material was a shared asset), destroyed on Clear.
            public bool Owned;
            public Renderer Renderer;
            public Material Shared;
        }

        private static readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();
        private static Renderer _currentRenderer;
        private static Entry _current;

        internal static void Apply(OpticSight sight, float ratio)
        {
            Renderer renderer = sight.LensRenderer;
            if (renderer == null)
                return;
            if ((object)renderer != _currentRenderer)
            {
                RestoreCurrent();
                if (ratio == 1f)
                    return;
                _current = EntryFor(renderer);
                _currentRenderer = renderer;
                if (_current == null)
                    return;
            }
            if (_current == null || _current.Mat == null)
                return;
            ReticleScaler.Apply(_current.Mat, ratio);
            float factor = ratio == 1f ? 1f : Mathf.Pow(ratio, ImageStrength);
            if (!_current.HasScales || factor == _current.Written)
                return;
            Vector4 scales = _current.Original;
            scales.x *= factor;
            _current.Mat.SetVector(_scalesId, scales);
            _current.Written = factor;
        }

        // The lens shader is shared by lenses of the same model only if their material is a shared asset; EFT
        // normally gives each lens its own instance ("(Instance)"). If it does not, the renderer gets a private
        // copy, so other lenses of the same model keep vanilla values.
        private static Entry EntryFor(Renderer renderer)
        {
            Material mat = renderer.sharedMaterial;
            if (mat == null)
                return null;
            if (_entries.TryGetValue(mat.GetInstanceID(), out Entry entry))
                return entry;
            entry = new Entry { Renderer = renderer };
            if (!mat.name.EndsWith("(Instance)"))
            {
                entry.Shared = mat;
                mat = new Material(mat) { name = mat.name + " (Instance)" };
                renderer.sharedMaterial = mat;
                entry.Owned = true;
            }
            entry.Mat = mat;
            entry.HasScales = mat.HasProperty(_scalesId);
            if (entry.HasScales)
                entry.Original = mat.GetVector(_scalesId);
            _entries[mat.GetInstanceID()] = entry;
            return entry;
        }

        // Back to vanilla for the lens that was being scaled.
        internal static void RestoreCurrent()
        {
            if (_current != null)
                Restore(_current);
            _current = null;
            _currentRenderer = null;
        }

        private static void Restore(Entry entry)
        {
            if (entry.Mat == null)
                return;
            ReticleScaler.Restore(entry.Mat);
            if (entry.HasScales && entry.Written != 1f)
            {
                entry.Mat.SetVector(_scalesId, entry.Original);
                entry.Written = 1f;
            }
        }

        // Raid end: restore every lens, hand shared materials back, drop the entries.
        internal static void Clear()
        {
            foreach (Entry entry in _entries.Values)
            {
                Restore(entry);
                if (entry.Owned)
                {
                    if (entry.Renderer != null && entry.Shared != null)
                        entry.Renderer.sharedMaterial = entry.Shared;
                    if (entry.Mat != null)
                        Object.Destroy(entry.Mat);
                }
            }
            _entries.Clear();
            _current = null;
            _currentRenderer = null;
            ReticleScaler.Clear();
        }
    }
}
