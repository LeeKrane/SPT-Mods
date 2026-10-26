using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla ForceChangeScopeState jumps to whichever native FOV edge is farther away. For the local
    // player's scopes in an enabled group, jump between the configured points instead and repeat the
    // vanilla side effects (target, stored zoom, switcher rotation, state event).
    [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
    internal static class TogglePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ScopeZoomHandler __instance, SightComponent ____sightComponent,
            ref float ____filedOfView, ref float ___float_2, ref float ___float_4)
        {
            LocalHandlers.Entry entry = LocalHandlers.Find(__instance, ____sightComponent);
            // Presets need a valid curve; scopes with an unusual one keep the vanilla edge toggle.
            if (entry == null || !entry.CurveValid)
                return true;
            try
            {
                GroupSettings group = ScopeSight.EnabledGroup(____sightComponent);
                if (group == null)
                    return true;

                if (!ScopeSight.TryLabels(____sightComponent, out float minZoom, out float maxZoom))
                    return true;
                group.Points(out float loZoom, out float hiZoom);
                float lo = ZoomMath.ZoomToFov(loZoom, minZoom, maxZoom, __instance.MinFov, __instance.MaxFov);
                float hi = ZoomMath.ZoomToFov(hiZoom, minZoom, maxZoom, __instance.MinFov, __instance.MaxFov);
                float target = ZoomMath.ToggleTarget(____filedOfView, lo, hi);
                // method_4 maps float_4 through the FOV curve, which only vanilla's edge values survive.
                // Everything that can throw is computed before the first field write.
                float curveInput = CurveSync.Input(entry, target);

                ____filedOfView = target;
                ___float_2 = target;
                ___float_4 = curveInput;
                ____sightComponent.ScopeZoomValue = target;
                __instance.method_11();
                __instance.method_9();
                // Like a vanilla edge toggle: method_9 only resets scroll state on an edge, and the target
                // can be in between, so drop leftover scroll momentum (float_3/7/8) explicitly.
                __instance.method_8();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return true;
            }
        }
    }
}
