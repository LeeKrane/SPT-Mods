using System;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // Vanilla shows "xN" on the optic panel only when an adjustable scope sits exactly on a native edge
    // and hides it in between. For adjustable scopes, that handler is skipped (HudSuppressPatch) and the
    // label is shown instead: once per hotkey press, and on scroll whenever the rounded percent changes.
    // Only for the local player's current sight, and only while its variable sub-scope is selected.
    internal static class HudPatch
    {
        private static BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> _screen;

        [HarmonyPatch(typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>), "SubscribeOnPlayer")]
        [HarmonyPostfix]
        private static void SubscribePostfix(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> __instance)
        {
            _screen = __instance;
        }

        // The player unsubscription lives in a compiler-generated closure; Close ends the screen's life.
        [HarmonyPatch(typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>), "Close")]
        [HarmonyPostfix]
        private static void ClosePostfix(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> __instance)
        {
            // The handler registry is not touched: opening the inventory closes this screen while the weapon
            // stays in hands. LocalHandlersPatch drops it when the Firearms returns to the pool.
            if (__instance == _screen)
                _screen = null;
        }

        // Runs every frame for every update-enabled handler (bots included). On a frame where the FOV did
        // not change, one injected bool read and out: no lookup, no allocation, no singleton, no call.
        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.method_4))]
        [HarmonyPostfix]
        private static void ScrollPostfix(ScopeZoomHandler __instance, bool ____isFieldOfViewEquals,
            float ___float_3, SightComponent ____sightComponent)
        {
            // Idle bool first, then the registry compare, so bots cost one read when idle and one short
            // reference scan when their FOV moves. float_3 == 0: method_4 moves the FOV by at most
            // FovSpeed * |float_3| * dt (ScopeZoomHandler.cs:291), so it cannot have changed (a vanilla
            // mid-glide reversal leaves the handler "not equal" with no movement left).
            if (____isFieldOfViewEquals || ___float_3 == 0f || !Plugin.LabelOn)
                return;
            Handle(__instance, ____sightComponent, false);
        }

        // Shared tail of both handler postfixes: only registered (handler, sight) pairs reach Update.
        private static void Handle(ScopeZoomHandler handler, SightComponent sight, bool force)
        {
            LocalHandlers.Entry entry = LocalHandlers.Find(handler, sight);
            if (entry != null)
                Update(entry, handler, sight, force);
        }

        // Hotkey press (ForceChangeScopeState is also called for every sight on weapon changes; Applies
        // filters to the aimed one). Event-driven, not per frame.
        [HarmonyPatch(typeof(ScopeZoomHandler), nameof(ScopeZoomHandler.ForceChangeScopeState))]
        [HarmonyPostfix]
        private static void TogglePostfix(ScopeZoomHandler __instance, SightComponent ____sightComponent)
        {
            if (Plugin.LabelOn)
                Handle(__instance, ____sightComponent, true);
        }

        // "Our label replaces vanilla's for this sight", for callers that already hold a registered (handler,
        // sight) pair (the Update/draw path). The registry's cached ProceduralWeaponAnimation belongs to the local
        // player's weapon, so one property read and a reference compare decide it.
        public static bool Applies(SightComponent sight)
        {
            return Plugin.LabelOn && LocalHandlers.IsCurrent(sight) && ScopeSight.IsVariableSelected(sight);
        }

        // Same, for HudSuppressPatch, which has no registry proof: a registered handler must also still drive
        // the sight (so the replacement label can actually be drawn) before vanilla is suppressed. Vanilla
        // raises SmoothSightChanged with the player's CurrentAimingMod (EFT/Player.cs:12570), so the
        // current-sight check is implied and skipped here; HasSight implies the local player's registry.
        public static bool AppliesWithRegistry(SightComponent sight)
        {
            return Plugin.LabelOn && ScopeSight.IsVariableSelected(sight) && LocalHandlers.HasSight(sight);
        }

        // Only reached on a frame where the FOV changed (scroll glide) or on a toggle, for a registered
        // (handler, sight) pair: the sight was validated adjustable in LocalHandlers.Set, so MinFov/MaxFov
        // are safe to read. The stored percent follows the handler even while its sight is not the current
        // one, so switching back to a changed sight does not draw.
        private static void Update(LocalHandlers.Entry entry, ScopeZoomHandler handler, SightComponent sight, bool force)
        {
            try
            {
                if (_screen == null)
                    return;

                // Vanilla's own percent: InverseLerp(MinFov, MaxFov, _filedOfView) (ScopeZoomHandler.cs:402-405).
                float fov = handler.FiledOfView;
                int percent = (int)Math.Round(handler.ZoomFactor() * 100f);
                int previous = entry.Percent;
                entry.Percent = percent;
                if (!force && percent == previous)
                    return;

                if (!Applies(sight))
                    return;

                Draw(_screen, sight, percent, fov, handler.MinFov);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static void Draw(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> screen,
            SightComponent sight, int percent, float fov, float minFov)
        {
            // Applies guaranteed a selected variable entry, so VariableEntry is that non-empty entry.
            float minZoom = GroupKeys.MinLabel(ScopeSight.VariableEntry(sight));
            float zoom = ZoomMath.ApproxZoom(fov, minFov, minZoom);
            screen._opticCratePanel.Show(ZoomMath.FormatLabel(percent, zoom));
            screen.HideRaidTime();
        }
    }

    // Skips the vanilla label for the sights HudPatch draws. It never draws: all drawing comes from the
    // handler postfixes, which fire for every FOV change and toggle. Applied only when HudPatch applied fully.
    internal static class HudSuppressPatch
    {
        [HarmonyPatch(typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>), "SmoothSightChanged")]
        [HarmonyPrefix]
        private static bool SmoothSightChangedPrefix(SightComponent sightComponent)
        {
            try
            {
                return !HudPatch.AppliesWithRegistry(sightComponent);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return true;
            }
        }
    }
}
