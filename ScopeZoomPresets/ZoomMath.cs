using System;
using System.Globalization;

namespace Krane.ScopeZoomPresets
{
    // FOV math for adjustable optics. minFov is MinMaxFov.x (widest FOV, lowest zoom), maxFov is
    // MinMaxFov.y (narrowest FOV, highest zoom). Percent is linear in FOV, like the game's ZoomFactor.
    internal static class ZoomMath
    {
        public static float PercentToFov(float percent, float minFov, float maxFov)
        {
            float t = Math.Max(0f, Math.Min(100f, percent)) / 100f;
            return minFov + (maxFov - minFov) * t;
        }

        public static float FovToPercent(float fov, float minFov, float maxFov)
        {
            if (minFov == maxFov)
                return 0f;
            float t = (fov - minFov) / (maxFov - minFov);
            return Math.Max(0f, Math.Min(1f, t)) * 100f;
        }

        // The game has no magnification formula; this scales the low label by the ratio of the
        // half-angle tangents, so it is only an approximation.
        public static float ApproxZoom(float fov, float minFov, float minZoom)
        {
            return (float)(minZoom * Math.Tan(minFov * Math.PI / 360.0) / Math.Tan(fov * Math.PI / 360.0));
        }

        // Same rule as vanilla ForceChangeScopeState: jump to the point farther away, tie goes to hi.
        public static float ToggleTarget(float current, float lo, float hi)
        {
            return Math.Abs(current - lo) > Math.Abs(current - hi) ? lo : hi;
        }

        public static void Order(ref float lo, ref float hi)
        {
            if (lo > hi)
                (lo, hi) = (hi, lo);
        }

        public static string FormatLabel(int percent, float zoom)
        {
            return $"{percent}% · ~{zoom.ToString("0.0", CultureInfo.InvariantCulture)}x";
        }
    }
}
