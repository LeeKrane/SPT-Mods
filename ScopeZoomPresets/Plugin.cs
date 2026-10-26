// Toggle adjustable optics between per-group configured zoom points instead of their native extremes.
//
// The magnification hotkey ends in ScopeZoomHandler.ForceChangeScopeState, which jumps to whichever
// native FOV edge is farther from the current one. TogglePatch keeps that rule but uses the group's
// Min zoom / Max zoom points (x magnification, 0.1 steps), mapped to FOV between MinMaxFov.x (the low label) and MinMaxFov.y (the high label).
// Zoom is stored as FOV in SightComponent.ScopeZoomValue, ordinary item state that Fika syncs like
// vanilla; values always stay inside the scope's native FOV range. The toggle and the HUD label are
// local only: their patches gate on the local player's sights.
using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    [BepInPlugin("krane.scopezoompresets", "Krane's ScopeZoomPresets", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ShowLabel;

        private void Awake()
        {
            Log = Logger;
            ShowLabel = Config.Bind("HUD", "Show zoom label", true,
                "Show the zoom as N.Nx on each hotkey press and on scroll whenever the rounded zoom changes, for the sight currently selected on your weapon. Off: vanilla label.");
            ShowLabel.SettingChanged += (_, __) => RefreshLabel();
            GroupConfig.Bind(Config);

            var harmony = new Harmony("krane.scopezoompresets");
            Apply(harmony, typeof(TogglePatch));
            Apply(harmony, typeof(RaidStartPatch));
            LocalHandlers.Disabled = !Apply(harmony, typeof(LocalHandlersPatch));
            Apply(harmony, typeof(CurveReversalPatch));
            // The vanilla label is only suppressed when the replacement label is fully in place.
            _labelActive = Apply(harmony, typeof(HudPatch)) && Apply(harmony, typeof(HudSuppressPatch));
            RefreshLabel();
        }

        // True once both HUD patch classes applied. Without it the HUD code stays inert, so a partially
        // applied HudPatch (Harmony keeps the patches it applied before a failure) shows no label.
        private static bool _labelActive;

        // Applied and enabled in config, as one plain static for the per-frame hook (no ConfigEntry access).
        internal static bool LabelOn;

        private static void RefreshLabel()
        {
            LabelOn = _labelActive && ShowLabel.Value && !LocalHandlers.Disabled;
        }

        // A missing game member makes only that patch class fail; the rest of the game stays vanilla.
        // PatchAll(Type) applies one method at a time and keeps earlier ones when a later one fails, so
        // classes are split by what must go together and the result decides what is switched on.
        internal static bool Apply(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.PatchAll(patchClass);
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"{patchClass.Name} not applied: {e}");
                return false;
            }
        }
    }
}
