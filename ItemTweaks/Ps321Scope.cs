// Client side of the Valday PS-321 (template 41a2f314a21e64a4862d6bd1, an ItemTweaks4Server clone of the PS-320 that reuses
// its bundle). Everything here is local rendering of your own first-person view; other players and the headless client are untouched.
//
// Magnification: the template's Zooms is only a label; the real zoom is the prefab's ScopeCameraData.FieldOfView, copied to the
// optic camera in OpticComponentUpdater.CopyComponentFromOptic when an optic is enabled (only while aiming through it). The
// PS-320's modes are FOV 24.5 (1x) and 4.0833 (6x, = 24.5 / 6); the postfix sets the zoomed mode to 4.0833 * 6 / 5 = 4.9 (5x).
// The original caches by optic instance id, so the postfix clears that cache to make the next optic (maybe the same pooled
// PS-320 model) apply its own FOV again.
//
// Camera closer: ProceduralWeaponAnimation places the aiming camera at min(_currentAimingPlane.Depth, _farPlane.Depth - ...)
// in front of the weapon root (a larger depth is further forward, eye guards subtract from it). FindCameraDepth runs after every
// scope, mode or weapon change; its postfix moves both planes EyeForward forward for the PS-321. The planes are replaced, never
// mutated: _currentAimingPlane is the weapon prefab's shared FarPlane, _farPlane is rebuilt by UpdateFarPlane.
//
// Less blackness: the lens shader darkens the picture when the eye leaves the lens axis (sway, movement). _Scales.y is the size of
// the clear area (PS-320 0.474, the forgiving Vudu / Razor / Tango6T 0.94), _Shifts.y how far the shadow moves with the offset
// (PS-320 0.058, Vudu 0.039). Set per lens renderer with a MaterialPropertyBlock (the material is shared with the PS-320) while
// aiming through it, removed when the optic is disabled (aim ends), so pooled models never carry it over.
// The reticle is drawn in optic camera space, so the wider 5x FOV draws it about 17% smaller than on the PS-320; accepted.
using System;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Animations;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace Krane.ItemTweaks
{
    internal static class Ps321Scope
    {
        internal const string TemplateId = "41a2f314a21e64a4862d6bd1";
        private static readonly MongoID TemplateMongoId = TemplateId;

        // PS-320 prefab modes: FOV 24.5 (1x) and 4.0833 (6x). Below the limit is the zoomed mode.
        private const float ZoomedFovLimit = 15f;
        private const float SourceZoom = 6f;
        private const float Zoom = 5f;
        // Metres the aiming camera moves forward, towards the eyepiece.
        private const float EyeForward = 0.02f;
        // Lens shadow: clear area scale and shift scale, applied to the prefab's own values.
        private const float ClearAreaFactor = 1.5f;
        private const float ShadowShiftFactor = 0.75f;
        private const string PlaneName = "ps321Plane";

        private static readonly int ScalesId = Shader.PropertyToID("_Scales");
        private static readonly int ShiftsId = Shader.PropertyToID("_Shifts");

        private static readonly AccessTools.FieldRef<OpticComponentUpdater, Camera> OpticCamera =
            AccessTools.FieldRefAccess<OpticComponentUpdater, Camera>("opticCamera");
        private static readonly AccessTools.FieldRef<OpticComponentUpdater, int> CachedOpticId =
            AccessTools.FieldRefAccess<OpticComponentUpdater, int>("cachedOpticId");
        private static readonly AccessTools.FieldRef<OpticComponentUpdater, float> OpticSightFov =
            AccessTools.FieldRefAccess<OpticComponentUpdater, float>("opticSightFov");

        // Lens renderers carrying our property block, with the block they had before (null = none).
        private static readonly Dictionary<Renderer, MaterialPropertyBlock> Lenses = new Dictionary<Renderer, MaterialPropertyBlock>();

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(Ps321Scope), AccessTools.Method(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.FindCameraDepth)),
                postfix: nameof(FindCameraDepthPostfix));
            Plugin.SafePatch(harmony, typeof(Ps321Scope), AccessTools.Method(typeof(OpticComponentUpdater), nameof(OpticComponentUpdater.CopyComponentFromOptic)),
                postfix: nameof(CopyComponentFromOpticPostfix));
            Plugin.SafePatch(harmony, typeof(Ps321Scope), AccessTools.Method(typeof(OpticSight), nameof(OpticSight.OnDisable)),
                postfix: nameof(OpticDisablePostfix));
        }

        // Aiming ended: drop the lens block so a pooled model reused as another scope (bots, other players) never keeps it.
        private static void OpticDisablePostfix(OpticSight __instance)
        {
            try { SetLens(__instance.LensRenderer, false); }
            catch (Exception e) { Plugin.Log.LogError(e); }
        }

        private static bool IsPs321(ProceduralWeaponAnimation.SightNBone scope) =>
            scope?.Mod?.Item != null && scope.Mod.Item.TemplateId == TemplateMongoId;

        private static void FindCameraDepthPostfix(ProceduralWeaponAnimation __instance)
        {
            try
            {
                if (__instance.ScopeAimTransforms == null || __instance.ScopeAimTransforms.Count < 1) return;
                var far = __instance._farPlane;
                bool ours = __instance == MainPlayerPwa() && __instance.CurrentScope.IsOptic && IsPs321(__instance.CurrentScope);
                if (ours)
                {
                    var plane = __instance._currentAimingPlane;
                    if (plane != null)
                        __instance._currentAimingPlane = new WeaponPrefab.AimPlane { Name = PlaneName, Depth = plane.Depth + EyeForward };
                    if (far != null && far.Name != PlaneName)
                        __instance._farPlane = new WeaponPrefab.AimPlane { Name = PlaneName, Depth = far.Depth + EyeForward };
                }
                else if (far != null && far.Name == PlaneName)
                {
                    // Another scope of the same weapon is now current, without UpdateFarPlane having run since.
                    __instance._farPlane = new WeaponPrefab.AimPlane { Name = "scopePlane", Depth = far.Depth - EyeForward };
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static void CopyComponentFromOpticPostfix(OpticComponentUpdater __instance, OpticSight opticSight)
        {
            try
            {
                if (opticSight == null) return;
                bool ours = IsMainPlayerPs321(opticSight);
                SetLens(opticSight.LensRenderer, ours);
                if (!ours || !(opticSight.CameraData is ScopeCameraData data)) return;

                // The 1x mode keeps its FOV; the zoomed mode goes from SourceZoom to Zoom.
                if (data.FieldOfView < ZoomedFovLimit)
                {
                    var camera = OpticCamera(__instance);
                    if (camera != null)
                    {
                        camera.fieldOfView = data.FieldOfView * SourceZoom / Zoom;
                        OpticSightFov(__instance) = camera.fieldOfView;
                    }
                }
                CachedOpticId(__instance) = 0;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static ProceduralWeaponAnimation MainPlayerPwa() =>
            Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer?.ProceduralWeaponAnimation : null;

        // Optics are enabled only while the local player aims through them, so the current first-person scope tells which item it is.
        private static bool IsMainPlayerPs321(OpticSight opticSight)
        {
            var pwa = MainPlayerPwa();
            if (pwa == null || !pwa.FirstPersonPointOfView || pwa.ScopeAimTransforms == null || pwa.ScopeAimTransforms.Count < 1) return false;
            var cache = opticSight.GetComponentInParent<ScopePrefabCache>();
            if (cache == null) return false;
            foreach (var scope in pwa.ScopeAimTransforms)
            {
                if (scope.ScopePrefabCache == cache) return IsPs321(scope);
            }
            return false;
        }

        private static void SetLens(Renderer lens, bool ours)
        {
            if (lens == null) return;
            if (Lenses.TryGetValue(lens, out var original))
            {
                if (ours) return;
                lens.SetPropertyBlock(original);
                Lenses.Remove(lens);
                return;
            }
            if (!ours) return;

            var material = lens.sharedMaterial;
            if (material == null || !material.HasProperty(ScalesId) || !material.HasProperty(ShiftsId)) return;
            // Dead renderers of unloaded raids are dropped here, before they could pile up.
            var dead = new List<Renderer>();
            foreach (var key in Lenses.Keys)
                if (key == null) dead.Add(key);
            foreach (var key in dead) Lenses.Remove(key);

            var mpb = new MaterialPropertyBlock();
            MaterialPropertyBlock saved = null;
            if (lens.HasPropertyBlock())
            {
                lens.GetPropertyBlock(mpb);
                saved = new MaterialPropertyBlock();
                lens.GetPropertyBlock(saved);
            }
            var scales = material.GetVector(ScalesId);
            var shifts = material.GetVector(ShiftsId);
            scales.y *= ClearAreaFactor;
            shifts.y *= ShadowShiftFactor;
            mpb.SetVector(ScalesId, scales);
            mpb.SetVector(ShiftsId, shifts);
            lens.SetPropertyBlock(mpb);
            Lenses[lens] = saved;
        }
    }
}
