using System;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace Krane.ScopeZoomPresets
{
    // method_4 maps float_4 through CameraData.FieldOfViewCurve, which is exact only at the native edges
    // that vanilla writes. Anywhere else float_4 has to be the curve's inverse of the zoom we want.
    internal static class CurveSync
    {
        // Callers pass a registered entry (LocalHandlers.Set validated the sight and built the curve once). A
        // handler whose curve failed validation keeps vanilla's float_4 = fov.
        public static float Input(LocalHandlers.Entry entry, float fov)
        {
            if (!entry.CurveValid)
                return fov;
            return ZoomMath.CurveInputFov(fov, entry.Handler.MinFov, entry.Handler.MaxFov, entry.Curve);
        }

        private static readonly AccessTools.FieldRef<ScopeZoomHandler, float> FloatFour =
            AccessTools.FieldRefAccess<ScopeZoomHandler, float>("float_4");
        private static readonly AccessTools.FieldRef<ScopeZoomHandler, SightComponent> Sight =
            AccessTools.FieldRefAccess<ScopeZoomHandler, SightComponent>("_sightComponent");

        // The sight the handler currently drives (set by Init). A pooled handler reused elsewhere has another.
        public static SightComponent HandlerSight(ScopeZoomHandler handler)
        {
            return Sight(handler);
        }

        // float_4 for the handler's current FOV, used by the registry setup and RaidStartPatch's re-Init.
        public static void Correct(LocalHandlers.Entry entry)
        {
            FloatFour(entry.Handler) = Input(entry, entry.Handler.FiledOfView);
        }
    }

    // Vanilla's direction reversal mid-glide goes method_5 -> method_6: float_3 = 0, float_4 = _filedOfView,
    // uncorrected (EFT/ScopeZoomHandler.cs:303-323). method_4 maps float_4 through the curve right after, so
    // the next value would jump. method_6 is patched directly. It is applied at plugin load, before the raid's
    // JIT compiles its callers: a MonoMod detour replaces the method body, so callers compiled afterwards
    // call the detour instead of inlining the original.
    [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.method_6))]
    internal static class CurveReversalPatch
    {
        [HarmonyPostfix]
        private static void Postfix(ScopeZoomHandler __instance, SightComponent ____sightComponent,
            ref float ___float_4)
        {
            LocalHandlers.Entry entry = LocalHandlers.Find(__instance, ____sightComponent);
            if (entry == null)
                return;
            try
            {
                ___float_4 = CurveSync.Input(entry, __instance.FiledOfView);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}
