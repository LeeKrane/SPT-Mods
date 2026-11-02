// Turns down the BTR as heard from inside it.
//
// BtrSoundController owns two copies of the engine/movement loops (VehicleMovementSoundContext) and
// the turret rotation sound (VehicleRotationSoundPlayer): one indoor, one outdoor. Entering or
// leaving cross-fades them through IVolumeControl.SetBaseVolume. The indoor copies are registered
// from BtrSoundController.Initialize, and every SetBaseVolume on them is scaled by the slider. The
// last unscaled value is kept so a slider change is reapplied at once. Suspension impacts share one
// controller and switch mixer by environment, so they are scaled only while it is set to Indoor.
using System.Collections.Generic;
using Audio.Vehicles;
using Audio.Vehicles.BTR;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using HarmonyLib;
using UnityEngine;

namespace Krane.BtrInteriorVolume
{
    [BepInPlugin("krane.btrinteriorvolume", "Krane's BtrInteriorVolume", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> Volume;

        // Indoor volume controls -> last unscaled base volume the game asked for.
        internal static readonly Dictionary<IVolumeControl, float> Indoor = new Dictionary<IVolumeControl, float>();

        private void Awake()
        {
            Volume = Config.Bind("General", "InteriorVolume", 0.5f, new ConfigDescription(
                "Volume of the BTR engine, movement, turret and suspension sounds while you sit inside. 1 = vanilla, 0 = silent.",
                new AcceptableValueRange<float>(0f, 1f)));
            Volume.SettingChanged += (_, __) => Reapply();

            new Harmony("krane.btrinteriorvolume").PatchAll(typeof(Plugin).Assembly);
        }

        private static void Reapply()
        {
            foreach (var entry in new List<KeyValuePair<IVolumeControl, float>>(Indoor))
            {
                if (entry.Key as Object == null)
                    Indoor.Remove(entry.Key);
                else
                    entry.Key.SetBaseVolume(entry.Value);
            }
        }

        internal static void Scale(IVolumeControl control, ref float volume)
        {
            if (!Indoor.ContainsKey(control))
                return;
            Indoor[control] = volume;
            volume *= Volume.Value;
        }
    }

    [HarmonyPatch(typeof(BtrSoundController), nameof(BtrSoundController.Initialize))]
    internal static class InitializePatch
    {
        // Prefix: Initialize already cross-fades to Outdoor, which sets the indoor volumes.
        private static void Prefix(BtrSoundController __instance)
        {
            foreach (var key in new List<IVolumeControl>(Plugin.Indoor.Keys))
            {
                if (key as Object == null)
                    Plugin.Indoor.Remove(key);
            }
            if (__instance._indoorSoundContext != null)
                Plugin.Indoor[__instance._indoorSoundContext] = 0f;
            if (__instance._indoorTurret != null)
                Plugin.Indoor[__instance._indoorTurret] = 0f;
        }
    }

    [HarmonyPatch(typeof(VehicleMovementSoundContext), nameof(VehicleMovementSoundContext.SetBaseVolume))]
    internal static class MovementVolumePatch
    {
        private static void Prefix(VehicleMovementSoundContext __instance, ref float volume) => Plugin.Scale(__instance, ref volume);
    }

    [HarmonyPatch(typeof(VehicleRotationSoundPlayer), nameof(VehicleRotationSoundPlayer.SetBaseVolume))]
    internal static class TurretVolumePatch
    {
        private static void Prefix(VehicleRotationSoundPlayer __instance, ref float volume) => Plugin.Scale(__instance, ref volume);
    }

    [HarmonyPatch(typeof(SoundSuspensionController), nameof(SoundSuspensionController.CalculateVolume))]
    internal static class SuspensionVolumePatch
    {
        private static void Postfix(EnvironmentType ____currentEnvironment, ref float __result)
        {
            if (____currentEnvironment == EnvironmentType.Indoor)
                __result *= Plugin.Volume.Value;
        }
    }
}
