using System;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla ForceChangeScopeState jumps to whichever native FOV edge is farther away. For the local
    // player's scopes in an enabled group, jump between the configured points instead and repeat the
    // vanilla side effects (target, last edge state, stored zoom, switcher rotation, state event).
    [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
    internal static class TogglePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ScopeZoomHandler __instance, SightComponent ____sightComponent,
            ref float ____filedOfView, ref float ___float_2, ref float ___float_4,
            ref ESmoothScopeState ____lastEdgeSmoothScopeState)
        {
            try
            {
                if (!ScopeSight.IsLocal(____sightComponent))
                    return true;
                GroupSettings group = ScopeSight.EnabledGroup(____sightComponent);
                if (group == null)
                    return true;

                group.Percents(out float loPercent, out float hiPercent);
                float lo = ZoomMath.PercentToFov(loPercent, __instance.MinFov, __instance.MaxFov);
                float hi = ZoomMath.PercentToFov(hiPercent, __instance.MinFov, __instance.MaxFov);
                float target = ZoomMath.ToggleTarget(____filedOfView, lo, hi);

                ____filedOfView = target;
                ___float_2 = target;
                ___float_4 = target;
                ____lastEdgeSmoothScopeState = target == lo ? ESmoothScopeState.Max : ESmoothScopeState.Min;
                ____sightComponent.ScopeZoomValue = target;
                __instance.method_11();
                __instance.method_9();
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
