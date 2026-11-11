// Grid layout of the Secure container Omega (template 22776b1c216596a5e40a8e2c, ItemTweaks4Server): grids 4x2, 1x2, 5x1.
//
// A secure container has no layout prefab: GridLayoutName is only read for Backpack, Vest and HideoutAreaContainer
// (their constructors add a GridLayoutComponent, which ContainedGridsView.CreateGrids resolves to "UI/Rig Layouts/<name>").
// Every other container gets the window's template, a GeneratedGridsView: one GridView per grid, instantiated under its own
// transform in grid order (GridView has a LayoutElement, sized by OnGridResized), laid out by a layout group on that transform,
// horizontally, so the three grids come out in one row. No vanilla layout name could fix it (none has these three grids).
//
// Postfix on GeneratedGridsView.Show: for Omega only, the first two grid views go into a horizontal row object and the root's
// layout group is replaced by a vertical one (spacing, padding copied), so the 5x1 sits below the row, left-aligned.
// Re-showing the same view is fine (idempotent). Window, stash and raid all go through GridWindow, so one patch covers them.
using System;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Krane.ItemTweaks
{
    internal static class OmegaLayout
    {
        internal const string TemplateId = "22776b1c216596a5e40a8e2c";
        private static readonly MongoID TemplateMongoId = TemplateId;
        private const string RowName = "krane.OmegaRow";
        // Grids in the top row; the rest go below it.
        private const int TopRowGrids = 2;

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(OmegaLayout),
                AccessTools.DeclaredMethod(typeof(GeneratedGridsView), nameof(GeneratedGridsView.Show),
                    new[] { typeof(CompoundItem), typeof(ItemContext), typeof(ItemController), typeof(FilterPanel), typeof(ItemUiContext), typeof(bool) }),
                postfix: nameof(ShowPostfix));
        }

        private static void ShowPostfix(GeneratedGridsView __instance, CompoundItem compoundItem)
        {
            try
            {
                if (compoundItem == null || compoundItem.TemplateId != TemplateMongoId) return;
                var views = __instance.GridViews;
                var root = __instance.transform as RectTransform;
                if (root == null || views == null || views.Length <= TopRowGrids) return;

                var row = root.Find(RowName) as RectTransform;
                if (row == null)
                {
                    float spacing = ReplaceRootLayout(root);
                    var go = new GameObject(RowName, typeof(RectTransform));
                    row = (RectTransform)go.transform;
                    row.SetParent(root, false);
                    row.anchorMin = row.anchorMax = row.pivot = new Vector2(0f, 1f);
                    var hlg = go.AddComponent<HorizontalLayoutGroup>();
                    ConfigureGroup(hlg);
                    hlg.spacing = spacing;
                }

                // Row first, then the remaining grids in grid order, each in its own parent's order.
                row.SetAsFirstSibling();
                for (int i = 0; i < views.Length; i++)
                {
                    if (views[i] == null) continue;
                    var rt = (RectTransform)views[i].transform;
                    EnsureLayoutElement(rt);
                    rt.SetParent(i < TopRowGrids ? (Transform)row : root, false);
                    rt.SetAsLastSibling();
                }

                LayoutRebuilder.ForceRebuildLayoutImmediate(root);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        // Swap the root's layout group for a vertical one, keeping spacing and padding. Returns the old spacing.
        private static float ReplaceRootLayout(RectTransform root)
        {
            var old = root.GetComponent<LayoutGroup>();
            float spacing = 0f;
            var padding = new RectOffset();
            if (old != null)
            {
                padding = new RectOffset(old.padding.left, old.padding.right, old.padding.top, old.padding.bottom);
                if (old is HorizontalOrVerticalLayoutGroup hv) spacing = hv.spacing;
                else Plugin.Log.LogWarning("Omega layout: root layout is " + old.GetType().Name + ", spacing not copied");
                UnityEngine.Object.DestroyImmediate(old);
            }
            else Plugin.Log.LogWarning("Omega layout: root has no layout group");
            var vlg = root.gameObject.AddComponent<VerticalLayoutGroup>();
            ConfigureGroup(vlg);
            vlg.spacing = spacing;
            vlg.padding = padding;
            return spacing;
        }

        // Children are sized by their own layout elements, never stretched; everything top-left.
        private static void ConfigureGroup(HorizontalOrVerticalLayoutGroup g)
        {
            g.childAlignment = TextAnchor.UpperLeft;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = false;
            g.childScaleWidth = false;
            g.childScaleHeight = false;
        }

        // A group needs a size to read from each grid; GridView normally has a LayoutElement.
        private static void EnsureLayoutElement(RectTransform rt)
        {
            if (rt.GetComponent<LayoutElement>() != null) return;
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.minWidth = rt.sizeDelta.x;
            le.minHeight = rt.sizeDelta.y;
        }
    }
}
