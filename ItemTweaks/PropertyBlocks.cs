// Shared by the case tint (Plugin) and the med recolor (MedRecolor): item models come from pools shared with the vanilla items, so every
// property block we set is undone when a pooled model is handed out again. The marker remembers, per renderer and material index, the
// block it had before our first change (null = none) and restores exactly that.
using System.Collections.Generic;
using UnityEngine;

namespace Krane.ItemTweaks
{
    internal sealed class PropertyBlockMarker : MonoBehaviour
    {
        private readonly List<(Renderer Renderer, int Index, MaterialPropertyBlock Original)> saved = new List<(Renderer, int, MaterialPropertyBlock)>();
        private readonly HashSet<(Renderer, int)> keys = new HashSet<(Renderer, int)>();

        internal static PropertyBlockMarker For(GameObject go) => go.GetComponent<PropertyBlockMarker>() ?? go.AddComponent<PropertyBlockMarker>();

        // The renderer's current block, ready to edit and set back. Its state before our first change is saved once.
        internal MaterialPropertyBlock Edit(Renderer r, int index)
        {
            var mpb = new MaterialPropertyBlock();
            if (r.HasPropertyBlock()) r.GetPropertyBlock(mpb, index);
            if (keys.Add((r, index)))
            {
                MaterialPropertyBlock original = null;
                if (!mpb.isEmpty)
                {
                    original = new MaterialPropertyBlock();
                    r.GetPropertyBlock(original, index);
                }
                saved.Add((r, index, original));
            }
            return mpb;
        }

        // Puts back the saved blocks and removes the marker. Main thread only.
        internal static void Restore(GameObject go)
        {
            var marker = go.GetComponent<PropertyBlockMarker>();
            if (marker == null) return;
            foreach (var (r, i, original) in marker.saved)
            {
                if (r != null) r.SetPropertyBlock(original, i);
            }
            DestroyImmediate(marker);
        }
    }
}
