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
            if (percent <= 0f)
                return minFov;
            if (percent >= 100f)
                return maxFov;
            float t = percent / 100f;
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

        // "N% · ~" for 0..100, built once: the label path then only formats the zoom and concatenates.
        private static readonly string[] LabelPrefixes = BuildPrefixes();

        private static string[] BuildPrefixes()
        {
            var prefixes = new string[101];
            for (int i = 0; i < prefixes.Length; i++)
                prefixes[i] = Prefix(i);
            return prefixes;
        }

        private static string Prefix(int percent)
        {
            return percent.ToString(CultureInfo.InvariantCulture) + "% · ~";
        }

        // Plain string concatenation, no boxing or interpolation handler.
        public static string FormatLabel(int percent, float zoom)
        {
            string prefix = percent >= 0 && percent < LabelPrefixes.Length ? LabelPrefixes[percent] : Prefix(percent);
            return prefix + zoom.ToString("0.0", CultureInfo.InvariantCulture) + "x";
        }
    }
}
