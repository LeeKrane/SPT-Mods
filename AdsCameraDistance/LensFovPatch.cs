using System;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace Krane.AdsCameraDistance
{
    // The optic image is a square render texture from a separate optic camera. Moving the eye closer makes the
    // lens cover a wider angle on screen, and the lens shader maps that wider angle beyond the texture, so its
    // edges render black. This widens the optic camera's FOV by the same ratio the lens grew (vanilla eye
    // distance / current eye distance), which fills the lens again, and LensMaterial enlarges the lens image to
    // bring the target back to its vanilla on-screen size, while the lens and reticle are larger.
    //
    // OpticComponentUpdater.LateUpdate runs every frame before the optic camera renders. Vanilla sets the
    // optic FOV only when the sight changes (fixed optics) or every frame from the zoom handler (variable
    // optics), so the base FOV is always read from the sight's data, never from the camera, and restored once
    // no offset applies.
    internal static class LensFovPatch
    {
        internal static bool Enabled = true;
        private static bool _modified;
        private static readonly Fault Fault = new Fault("Lens FOV hook");
        // Optic render texture size (square) while an offset applies. A widened optic FOV spreads the same
        // pixels over a wider view, so the image gets blurrier unless this is raised. Otherwise vanilla's size.
        internal static int Resolution = 2048;
        private static int _vanillaResolution;
        private static bool _resolutionStuck;

        // Vanilla sets the size once at start. The raised size is used only while an offset applies to the
        // sight aimed through, so the plugin off, compensation off or sights without an offset cost nothing.
        // SetResolution recreates the render texture, so this runs only on a change.
        private static void ApplyResolution(bool raised)
        {
            OpticCameraManager manager = CameraManager.Instance.OpticCameraManager;
            if (manager == null || _resolutionStuck)
                return;
            if (_vanillaResolution == 0)
                _vanillaResolution = manager.OpticFinalResolution;
            int wanted = raised ? Resolution : _vanillaResolution;
            if (manager.OpticFinalResolution == wanted)
                return;
            manager.SetResolution(wanted);
            // Guard against a game version that derives the stored size: never rebuild every frame.
            if (manager.OpticFinalResolution != wanted)
            {
                _resolutionStuck = true;
                Plugin.Log.LogWarning($"Optic resolution {wanted} did not stick (now {manager.OpticFinalResolution}), no longer changing it.");
            }
        }

        [HarmonyPatch(typeof(OpticComponentUpdater), nameof(OpticComponentUpdater.LateUpdate))]
        [HarmonyPostfix]
        private static void Postfix(Camera ___opticCamera, OpticSight ____opticSight, ScopeZoomHandler ____scopeZoomHandler)
        {
            if (Fault.Blocked || ___opticCamera == null || ____opticSight == null)
                return;
            try
            {
                float ratio = Enabled && Plugin.Enabled ? LensRatio(____opticSight) : 1f;
                ApplyResolution(Enabled && Plugin.Enabled && CameraDistancePatch.Shift != 0f);
                LensMaterial.Apply(____opticSight, ratio);
                if (ratio != 1f || _modified)
                {
                    float baseFov = BaseFov(____opticSight, ____scopeZoomHandler);
                    if (baseFov > 0f)
                    {
                        ___opticCamera.fieldOfView = ratio == 1f ? baseFov : Widen(baseFov, ratio);
                        _modified = ratio != 1f;
                    }
                }
                Fault.Ok();
            }
            catch (Exception e)
            {
                Fault.Hit(e);
            }
        }

        private static float BaseFov(OpticSight sight, ScopeZoomHandler zoom)
        {
            if (sight.CameraData.IsAdjustableOptic)
                return zoom != null ? zoom.FiledOfView : 0f;
            return sight.CameraData is ScopeCameraData data ? data.FieldOfView : 0f;
        }

        // Keeps the image's angular size: tan(fov'/2) = tan(fov/2) * ratio.
        internal static float Widen(float fov, float ratio)
        {
            float half = Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * ratio);
            return Mathf.Clamp(half * 2f * Mathf.Rad2Deg, 0.1f, 170f);
        }

        // Vanilla eye-to-lens distance / current one, or 1 when no shift applies. Both eyes come from the
        // captured vanilla eye in weapon space (CameraDistancePatch.CaptureEye): the current eye is it moved by
        // the modelled Shift along the camera's z axis, the same delta the hook applies to the target. The
        // lens is the one this updater renders, read now; since eye and lens are both tied to the weapon,
        // weapon bob and the order of the two hooks in the frame do not matter. The geometry is exact for any
        // Shift, so the glide back after un-aiming (or a sign change of the offset) follows it without a model.
        private static float LensRatio(OpticSight sight)
        {
            float shift = CameraDistancePatch.Shift;
            Transform space = CameraDistancePatch.EyeSpace;
            if (shift == 0f || space == null || sight.LensRenderer == null)
                return 1f;
            Vector3 lens = sight.LensRenderer.transform.position;
            Vector3 eye = CameraDistancePatch.EyeLocal;
            float vanilla = Vector3.Distance(space.TransformPoint(eye), lens);
            float current = Vector3.Distance(space.TransformPoint(eye + CameraDistancePatch.EyeAxisLocal * shift), lens);
            if (current < 0.001f)
                return 1f;
            float ratio = Mathf.Clamp(vanilla / current, 0.25f, 8f);
            return Mathf.Abs(ratio - 1f) < 0.001f ? 1f : ratio;
        }
    }
}
