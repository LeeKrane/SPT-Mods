// Toggle adjustable optics between per-group configured zoom points instead of their native extremes.
//
// The magnification hotkey ends in ScopeZoomHandler.ForceChangeScopeState, which jumps to whichever
// native FOV edge is farther from the current one. TogglePatch keeps that rule but uses the group's
// Min % / Max % points. Percent is linear in FOV between MinMaxFov.x (0%) and MinMaxFov.y (100%).
// Zoom is stored as FOV in SightComponent.ScopeZoomValue and never sent over the network, so all of
// this is local. Only the local player's scopes are touched.
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
                "Show percent and approximate zoom on every zoom change of an adjustable scope. Off: vanilla label.");
            GroupConfig.Bind(Config);

            var harmony = new Harmony("krane.scopezoompresets");
            Apply(harmony, typeof(TogglePatch));
            Apply(harmony, typeof(RaidStartPatch));
        }

        // A missing game member makes only that patch class fail; the rest of the game stays vanilla.
        internal static void Apply(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.PatchAll(patchClass);
            }
            catch (Exception e)
            {
                Log.LogError($"{patchClass.Name} not applied: {e}");
            }
        }
    }
}
