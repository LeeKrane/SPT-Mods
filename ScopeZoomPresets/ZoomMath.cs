using System;
using System.Globalization;

namespace Krane.ScopeZoomPresets
{
    // FOV math for adjustable optics. minFov is MinMaxFov.x (widest FOV, lowest zoom), maxFov is
    // MinMaxFov.y (narrowest FOV, highest zoom). The game has no magnification formula for in-between
    // zooms; this models it as a log-space interpolation of the half-angle tangent between the two native
    // edges, so both edges are exact (they map to the scope's labels) and in-between values follow optics.
    internal static class ZoomMath
    {
        // Exact inverse of FovToZoom. Zooms at or beyond a label map to the native edge FOV.
        public static float ZoomToFov(float zoom, float minZoom, float maxZoom, float minFov, float maxFov)
        {
            if (!(maxZoom > minZoom) || !(zoom > minZoom))
                return minFov;
            if (zoom >= maxZoom)
                return maxFov;
            double k = Math.Log(zoom / minZoom) / Math.Log(maxZoom / minZoom);
            double tanMin = Math.Tan(minFov * Math.PI / 360.0);
            double tanMax = Math.Tan(maxFov * Math.PI / 360.0);
            double tanHalf = tanMin * Math.Pow(tanMax / tanMin, k);
            float fov = (float)(2.0 * Math.Atan(tanHalf) * 180.0 / Math.PI);
            return Math.Max(maxFov, Math.Min(minFov, fov));
        }

        public static float FovToZoom(float fov, float minZoom, float maxZoom, float minFov, float maxFov)
        {
            if (!(maxZoom > minZoom) || fov >= minFov)
                return minZoom;
            if (fov <= maxFov)
                return maxZoom;
            double tanMin = Math.Tan(minFov * Math.PI / 360.0);
            double tanMax = Math.Tan(maxFov * Math.PI / 360.0);
            double tanFov = Math.Tan(fov * Math.PI / 360.0);
            double k = Math.Log(tanMin / tanFov) / Math.Log(tanMin / tanMax);
            return (float)(minZoom * Math.Pow(maxZoom / minZoom, k));
        }

        public static float SnapZoom(float zoom)
        {
            return (float)(Math.Round(zoom * 10.0, MidpointRounding.AwayFromZero) / 10.0);
        }

        public static int ToTenths(float zoom)
        {
            return (int)Math.Round(zoom * 10.0, MidpointRounding.AwayFromZero);
        }

        // Same rule as vanilla ForceChangeScopeState: jump to the point farther away, tie goes to hi.
        public static float ToggleTarget(float current, float lo, float hi)
        {
            return Math.Abs(current - lo) > Math.Abs(current - hi) ? lo : hi;
        }

        // Inverse of a monotonic non-decreasing curve on [0, 1]: the t where curve(t) == ratio.
        public static float InverseCurve(Func<float, float> curve, float ratio)
        {
            ratio = Math.Max(0f, Math.Min(1f, ratio));
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 24; i++)
            {
                float mid = (lo + hi) / 2f;
                if (curve(mid) < ratio)
                    lo = mid;
                else
                    hi = mid;
            }
            return (lo + hi) / 2f;
        }

        // method_4 maps its scroll target through the FOV curve; vanilla only writes the edges, where the
        // curve is exact. This gives the value to store so the curve maps back to an in-between target.
        public static float CurveInputFov(float target, float minFov, float maxFov, Func<float, float> curve)
        {
            // Requires minFov > maxFov (IsSupportedRange, checked once when a handler is registered).
            float range = minFov - maxFov;
            // Vanilla's own edge values map exactly; keep them exact instead of bisection noise.
            if (target >= minFov)
                return minFov;
            if (target <= maxFov)
                return maxFov;
            float t = InverseCurve(curve, (target - maxFov) / range);
            return maxFov + (minFov - maxFov) * t;
        }

        // Vanilla's clamps and InverseLerp assume MinMaxFov.x > .y (wide to narrow); anything else is
        // unsupported and the plugin leaves that sight vanilla.
        public static bool IsSupportedRange(float minFov, float maxFov)
        {
            return minFov > maxFov;
        }

        private static readonly float[] RoundTripProbes = { 0.1f, 0.25f, 0.5f, 0.75f, 0.9f };

        // The inverse and the FOV mapping assume curve(0) = 0, curve(1) = 1, no decrease in between, and that
        // the bisection inverse maps back (no jumps). Checked once per registered handler (event-driven) on 64
        // samples plus a few round trips; a curve that fails is left alone (vanilla behavior).
        public static bool IsCurveValid(Func<float, float> curve)
        {
            const int steps = 64;
            float previous = curve(0f);
            if (!(Math.Abs(previous) < 0.01f))
                return false;
            for (int i = 1; i <= steps; i++)
            {
                float value = curve(i / (float)steps);
                if (!(value >= previous - 0.0001f))
                    return false;
                previous = value;
            }
            if (!(Math.Abs(previous - 1f) < 0.01f))
                return false;
            foreach (float probe in RoundTripProbes)
            {
                if (!(Math.Abs(curve(InverseCurve(curve, probe)) - probe) < 0.01f))
                    return false;
            }
            return true;
        }

        public static void Order(ref float lo, ref float hi)
        {
            if (lo > hi)
                (lo, hi) = (hi, lo);
        }

        // "N.Nx" strings by tenths, built on first use: the HUD path then allocates only for a zoom it has
        // not shown before.
        private static readonly string[] ZoomLabels = new string[1001];

        public static string FormatZoom(int tenths)
        {
            if (tenths < 0 || tenths >= ZoomLabels.Length)
                return Build(tenths);
            return ZoomLabels[tenths] ?? (ZoomLabels[tenths] = Build(tenths));
        }

        private static string Build(int tenths)
        {
            return (tenths / 10).ToString(CultureInfo.InvariantCulture) + "." + (Math.Abs(tenths) % 10).ToString(CultureInfo.InvariantCulture) + "x";
        }
    }
}
