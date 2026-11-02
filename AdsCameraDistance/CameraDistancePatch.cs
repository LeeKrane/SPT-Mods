using System;
using EFT;
using EFT.Animations;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.AdsCameraDistance
{
    // The local player's ProceduralWeaponAnimation, set when its weapon is drawn (_isYourPlayer is decided by
    // ObjectInHands.AfterGetFromPoolInit from Player.IsYourPlayer) and dropped when that Firearms returns to
    // the pool (weapon switch, drop, raid end). Bots and observed players never match, so the camera hook
    // exits on one reference compare for them.
    internal static class LocalPwaPatch
    {
        internal static ProceduralWeaponAnimation Pwa;
        // The Firearms that set Pwa. The PWA is shared by all of the player's Firearms, so on a weapon swap the
        // old one may return to the pool after the new one initialised; only the current owner clears Pwa.
        private static Firearms _owner;

        [HarmonyPatch(typeof(Firearms), nameof(Firearms.AfterGetFromPoolInit))]
        [HarmonyPostfix]
        private static void InitPostfix(Firearms __instance, ProceduralWeaponAnimation proceduralWeaponAnimation, bool isYourPlayer)
        {
            if (!isYourPlayer)
                return;
            Pwa = proceduralWeaponAnimation;
            _owner = __instance;
            CameraDistancePatch.ResetEye();
        }

        [HarmonyPatch(typeof(Firearms), nameof(Firearms.OnReturnToPool))]
        [HarmonyPrefix]
        private static void ReturnPrefix(Firearms __instance)
        {
            if ((object)__instance == _owner)
                Clear();
        }

        // Do not hold the player graph in a static across raids.
        [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Dispose))]
        [HarmonyPostfix]
        private static void DisposePostfix()
        {
            Clear();
            // Only here, not on weapon swaps: the lens entries must outlive a swap, so a still-scaled lens can be
            // restored. At raid end every lens is restored before its entry is dropped.
            LensMaterial.Clear();
        }

        private static void Clear()
        {
            Pwa = null;
            CameraDistancePatch.Shift = 0f;
            CameraDistancePatch.ResetEye();
            _owner = null;
            CameraDistancePatch.ClearCache();
        }
    }

    // FirstPersonStrategy.ApplyCameraTransformations runs UpdateCurrentScopeBoneTarget, CalculateCameraPosition,
    // then LerpCamera. Only this strategy calls CalculateCameraPosition: mounted (MountingStrategy ->
    // MountedWeaponCamera) and stationary weapons (StationaryStrategy -> StationaryCamera) use their own camera
    // methods and are untouched. Inside it, the aiming branch (IsAiming, no blindfire, has sights) sets
    // _vCameraTarget to the sight's position in the camera parent's space; every other state takes the else
    // branch, which this patch skips by repeating the branch condition.
    //
    // The offset is subtracted from _vCameraTarget.z (the camera-to-sight axis; +z points toward the sight, as
    // the aim plane places the eye at -depth behind it). LerpCamera then eases the camera toward it
    // (CameraSmoothTime), so changes glide. FOV is not touched.
    internal static class CameraDistancePatch
    {
        private static AccessTools.FieldRef<ProceduralWeaponAnimation, IFirearmAnimationDataRepresenter> _animationData;
        internal static readonly Fault Fault = new Fault("Camera hook");
        // Camera shift toward the sight (camera parent's local z, positive = closer) that the offset currently
        // causes, for LensFovPatch. It follows the offset with the same easing LerpCamera applies to the z
        // target, so the lens compensation tracks the glide (also back to vanilla after a reset or un-aim)
        // without picking up recoil or other camera motion. 0 when no offset applies.
        internal static float Shift;
        // The vanilla eye (camera target before the offset) and the camera's z axis, in the weapon transform's
        // local space, captured on the last aiming frame for LensFovPatch. The target is derived from the sight
        // on the weapon, so in weapon space it does not move with weapon bob, and the lens can be read later in
        // the frame (or a frame later) without the two drifting apart. See LensFovPatch.LensRatio.
        // Whether the sight last aimed through has a stored offset. Updated on aiming frames only and kept after
        // un-aiming, so LensFovPatch's raised optic resolution does not flip back and forth around every
        // scope-in (each flip recreates the optic render texture, which stutters).
        internal static bool SightHasOffset;
        internal static UnityEngine.Transform EyeSpace;
        internal static UnityEngine.Vector3 EyeLocal;
        internal static UnityEngine.Vector3 EyeAxisLocal;

        // Cached key for the current (weapon, sight, sub-sight); rebuilt only when one of them changes.
        private static Weapon _cWeapon;
        private static SightComponent _cSight;
        private static int _cSub = -1;
        private static int _cAim = -1;
        private static string _cKey;
        // Cached offset for _cKey, refreshed when the store changes.
        private static int _cVersion = -1;
        private static float _cOffset;

        public static bool Resolve()
        {
            try
            {
                _animationData = AccessTools.FieldRefAccess<ProceduralWeaponAnimation, IFirearmAnimationDataRepresenter>("_firearmAnimationData");
                // Touch the public members used per frame, so a rename fails here rather than in the hook.
                _ = AccessTools.Field(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation._vCameraTarget)).FieldType;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Member lookup failed: {e}");
                return false;
            }
        }

        // Drops the cached weapon and sight, so they do not outlive the raid or the drawn weapon.
        internal static void ClearCache()
        {
            _cWeapon = null;
            _cSight = null;
            _cKey = null;
            _cSub = -1;
            _cAim = -1;
            _cVersion = -1;
        }

        // Same condition as the aiming branch of CalculateCameraPosition, plus first person and not mounted.
        private static bool Applicable(ProceduralWeaponAnimation pwa)
        {
            return pwa.IsAiming
                && pwa.FirstPersonPointOfView
                && !pwa.IsMountedState
                && pwa.ScopeAimTransforms.Count > 0
                && UnityEngine.Mathf.Approximately(pwa.BlindfireBlender.Value, 0f)
                && pwa.CurrentScope != null;
        }

        [HarmonyPatch(typeof(ProceduralWeaponAnimation), nameof(ProceduralWeaponAnimation.CalculateCameraPosition))]
        [HarmonyPostfix]
        private static void Postfix(ProceduralWeaponAnimation __instance)
        {
            if ((object)__instance != LocalPwaPatch.Pwa)
                return;
            float target = 0f;
            if (Plugin.Enabled && !Fault.Blocked && !Plugin.Store.ReadFailed)
            {
                try
                {
                    float offset = 0f;
                    if (Applicable(__instance))
                    {
                        CaptureEye(__instance);
                        string key = KeyFor(__instance);
                        if (key != null)
                            offset = OffsetFor(key);
                        SightHasOffset = offset != 0f;
                    }
                    if (offset != 0f)
                    {
                        __instance._vCameraTarget.z -= offset * Plugin.ZSign;
                        target = -offset * Plugin.ZSign;
                    }
                    Fault.Ok();
                }
                catch (Exception e)
                {
                    Fault.Hit(e);
                }
            }
            // Same easing as LerpCamera's z (CameraSmoothTime * dt per frame), snapped once it is negligible.
            Shift = UnityEngine.Mathf.Lerp(Shift, target, UnityEngine.Mathf.Clamp01(__instance.CameraSmoothTime * UnityEngine.Time.deltaTime));
            if (UnityEngine.Mathf.Abs(Shift - target) < 0.00005f)
                Shift = target;
            // The optic updater may stop running once the player stops aiming; restore its lens here instead.
            if (Shift == 0f && !__instance.IsAiming)
                LensMaterial.RestoreCurrent();
        }

        // Before the offset is applied, so the vanilla eye is stored.
        private static void CaptureEye(ProceduralWeaponAnimation pwa)
        {
            UnityEngine.Transform weapon = pwa.HandsContainer.Weapon;
            UnityEngine.Transform rig = pwa.HandsContainer.CameraTransform;
            if (weapon == null || rig == null || rig.parent == null)
                return;
            UnityEngine.Transform parent = rig.parent;
            EyeLocal = weapon.InverseTransformPoint(parent.TransformPoint(pwa._vCameraTarget));
            EyeAxisLocal = weapon.InverseTransformVector(parent.TransformVector(0f, 0f, 1f));
            EyeSpace = weapon;
        }

        // A newly drawn weapon (or raid end) drops the captured eye: it belongs to the previous weapon. Shift is
        // kept, since the shared camera is still gliding with it.
        internal static void ResetEye()
        {
            EyeSpace = null;
        }

        // Key of the local player's current aiming state, or null when no offset applies (not aiming, mounted...).
        public static string CurrentKey()
        {
            ProceduralWeaponAnimation pwa = LocalPwaPatch.Pwa;
            if (pwa == null || !Applicable(pwa))
                return null;
            return KeyFor(pwa);
        }

        private static float OffsetFor(string key)
        {
            if (_cVersion != Plugin.Store.Version)
            {
                _cOffset = Plugin.Store.Get(key);
                _cVersion = Plugin.Store.Version;
            }
            // Clamped here rather than on load, so narrowing Min / Max never rewrites stored values.
            return OffsetMath.Clamp(_cOffset, Plugin.Min, Plugin.Max);
        }

        // `<weaponTpl>|<scopeTpl>|<SelectedScope>`; sights without a SightComponent (iron sights built into
        // the weapon) give `<weaponTpl>|iron|<AimIndex>`.
        private static string KeyFor(ProceduralWeaponAnimation pwa)
        {
            IFirearmAnimationDataRepresenter data = _animationData(pwa);
            Weapon weapon = data?.Weapon;
            if (weapon == null)
                return null;
            SightComponent sight = pwa.CurrentScope.Mod;
            int sub = sight != null ? sight.SelectedScope : 0;
            int aim = sight != null ? 0 : pwa.AimIndex;
            if ((object)weapon == _cWeapon && (object)sight == _cSight && sub == _cSub && aim == _cAim && _cKey != null)
                return _cKey;

            _cWeapon = weapon;
            _cSight = sight;
            _cSub = sub;
            _cAim = aim;
            _cKey = sight != null
                ? OffsetMath.BuildKey(weapon.StringTemplateId, sight.Item.StringTemplateId, sub)
                : OffsetMath.BuildIronKey(weapon.StringTemplateId, aim);
            _cVersion = -1;
            return _cKey;
        }
    }
}
