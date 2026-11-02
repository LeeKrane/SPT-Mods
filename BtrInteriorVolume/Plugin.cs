// Turns down the BTR as heard from inside it.
//
// BtrSoundController owns indoor and outdoor copies of the engine/movement sounds
// (VehicleMovementSoundContext) and the turret rotation sound (VehicleRotationSoundPlayer), and
// cross-fades them on boarding. Both expose a per-instance multiplier: OverallVolume (read every
// frame through MaxAllowedVolume) and _volumeMult (pushed to the source by SetBaseVolume). A
// VolumeScaler added to each controller scales the indoor copies' multipliers while the local
// player boards, sits in or leaves the BTR. Fika runs vanilla BTRView.GoIn(Player) for observed
// players too, which fades the indoor sounds in on this client, so the boarding check uses the
// local player's own BtrState. Suspension impacts pick VehicleInMixer while indoor; their volume
// is scaled in PlayImpactSound, after the caller's 0.5 play threshold.
using Audio.Vehicles;
using Audio.Vehicles.BTR;
using BepInEx;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace Krane.BtrInteriorVolume
{
    [BepInPlugin("krane.btrinteriorvolume", "Krane's BtrInteriorVolume", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<float> Volume;

        private void Awake()
        {
            Volume = Config.Bind("General", "InteriorVolume", 0.5f, new ConfigDescription(
                "Volume of the BTR engine, movement, turret and suspension sounds while you sit inside. 1 = vanilla, 0 = silent.",
                new AcceptableValueRange<float>(0f, 1f)));

            new Harmony("krane.btrinteriorvolume").PatchAll(typeof(Plugin).Assembly);
        }
    }

    internal class VolumeScaler : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<VehicleRotationSoundPlayer, float> TurretBaseVolume =
            AccessTools.FieldRefAccess<VehicleRotationSoundPlayer, float>("_baseVolume");

        // Factor applied to indoor suspension impacts; 1 when no local player is in a BTR.
        internal static float Current = 1f;

        private VehicleMovementSoundContext _movement;
        private VehicleRotationSoundPlayer _turret;
        private float _movementVolume;
        private float _turretMult;
        private float _applied = 1f;

        internal void Init(BtrSoundController controller)
        {
            _movement = controller._indoorSoundContext;
            _turret = controller._indoorTurret;
            if (_movement != null)
                _movementVolume = _movement.OverallVolume;
            if (_turret != null)
                _turretMult = _turret._volumeMult;
        }

        private void LateUpdate()
        {
            var player = Singleton<GameWorld>.Instantiated ? Singleton<GameWorld>.Instance.MainPlayer : null;
            var state = player != null ? player.BtrState : EPlayerBtrState.Outside;
            var factor = state == EPlayerBtrState.GoIn || state == EPlayerBtrState.Inside || state == EPlayerBtrState.GoOut
                ? Plugin.Volume.Value
                : 1f;
            Current = factor;
            if (factor == _applied)
                return;
            _applied = factor;
            if (_movement != null)
                _movement.OverallVolume = _movementVolume * factor;
            if (_turret != null)
            {
                _turret._volumeMult = _turretMult * factor;
                _turret.SetBaseVolume(TurretBaseVolume(_turret));
            }
        }

        private void OnDestroy() => Current = 1f;
    }

    [HarmonyPatch(typeof(BtrSoundController), nameof(BtrSoundController.Initialize))]
    internal static class InitializePatch
    {
        private static void Postfix(BtrSoundController __instance)
        {
            if (__instance.GetComponent<VolumeScaler>() == null)
                __instance.gameObject.AddComponent<VolumeScaler>().Init(__instance);
        }
    }

    [HarmonyPatch(typeof(SoundSuspensionImpactSoundPlayer), nameof(SoundSuspensionImpactSoundPlayer.PlayImpactSound))]
    internal static class SuspensionVolumePatch
    {
        private static void Prefix(ref float volume, AudioMixerGroup mixerGroup)
        {
            if (VolumeScaler.Current != 1f && MonoBehaviourSingleton<BetterAudio>.Instantiated
                && mixerGroup == MonoBehaviourSingleton<BetterAudio>.Instance.VehicleInMixer)
                volume *= VolumeScaler.Current;
        }
    }
}
