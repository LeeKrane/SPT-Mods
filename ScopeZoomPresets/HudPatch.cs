using System;
using System.Linq;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla shows "xN" on the optic panel only when an adjustable scope sits exactly on a native edge
    // and hides it in between. For adjustable scopes, that handler is skipped and the label is shown on
    // every zoom change instead: once per hotkey press, and on scroll whenever the rounded percent changes.
    internal static class HudPatch
    {
        private static EftBattleUIScreen _screen;
        private static ScopeZoomHandler _lastHandler;
        private static int _lastPercent = -1;

        [HarmonyPatch(typeof(EftBattleUIScreen), "SubscribeOnPlayer")]
        [HarmonyPostfix]
        private static void SubscribePostfix(EftBattleUIScreen __instance)
        {
            _screen = __instance;
        }

        [HarmonyPatch(typeof(EftBattleUIScreen), "SmoothSightChanged")]
        [HarmonyPrefix]
        private static bool SmoothSightChangedPrefix(SightComponent sightComponent)
        {
            return !(Plugin.ShowLabel.Value && ScopeSight.IsAdjustable(sightComponent));
        }

        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.method_4))]
        [HarmonyPostfix]
        private static void ScrollPostfix(ScopeZoomHandler __instance, SightComponent ____sightComponent, float ____filedOfView)
        {
            Show(__instance, ____sightComponent, ____filedOfView, false);
        }

        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
        [HarmonyPostfix]
        private static void TogglePostfix(ScopeZoomHandler __instance, SightComponent ____sightComponent, float ____filedOfView)
        {
            Show(__instance, ____sightComponent, ____filedOfView, true);
        }

        private static void Show(ScopeZoomHandler handler, SightComponent sight, float fov, bool force)
        {
            try
            {
                if (!Plugin.ShowLabel.Value || _screen == null || !ScopeSight.IsAdjustable(sight))
                    return;
                int percent = (int)Math.Round(ZoomMath.FovToPercent(fov, handler.MinFov, handler.MaxFov));
                if (!force && handler == _lastHandler && percent == _lastPercent)
                    return;
                if (!ScopeSight.IsLocal(sight))
                    return;

                // A different scope starts updating (aim-in, weapon switch): remember its zoom without
                // showing, so the label appears on the first actual change.
                bool newHandler = handler != _lastHandler;
                _lastHandler = handler;
                _lastPercent = percent;
                if (newHandler && !force)
                    return;

                float[] zooms = ScopeSight.Zooms(sight);
                float minZoom = zooms != null && zooms.Length > 0 ? zooms.Min() : 1f;
                float zoom = ZoomMath.ApproxZoom(fov, handler.MinFov, minZoom);
                _screen._opticCratePanel.Show(ZoomMath.FormatLabel(percent, zoom));
                _screen.HideRaidTime();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }
    }
}
