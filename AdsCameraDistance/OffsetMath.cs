using System;
using System.Globalization;

namespace Krane.AdsCameraDistance
{
    // Pure math and formatting. Offsets are metres added to the camera z target: negative = closer to the sight.
    internal static class OffsetMath
    {
        public const float Epsilon = 0.00005f;

        // Min/Max may be configured in either order, and must contain 0 (vanilla).
        public static void Normalize(ref float min, ref float max)
        {
            if (min > max)
            {
                float t = min;
                min = max;
                max = t;
            }
            if (min > 0f)
                min = 0f;
            if (max < 0f)
                max = 0f;
        }

        public static float Clamp(float value, float min, float max)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;
            return value < min ? min : value > max ? max : value;
        }

        // Rounds to 0.1 mm so repeated steps do not drift, then clamps. Scrolling up (positive notches) goes
        // closer, i.e. more negative.
        public static float Adjust(float current, int notches, float step, float min, float max)
        {
            float value = current - notches * step;
            value = (float)Math.Round(value, 4);
            return Clamp(value, min, max);
        }

        public static bool IsVanilla(float offset)
        {
            return Math.Abs(offset) < Epsilon;
        }

        public static string FormatLabel(float offset)
        {
            if (IsVanilla(offset))
                return "Eye dist vanilla";
            return "Eye dist " + offset.ToString("0.000", CultureInfo.InvariantCulture);
        }

        public static string BuildKey(string weaponTemplateId, string scopeTemplateId, int subScope)
        {
            return weaponTemplateId + "|" + scopeTemplateId + "|" + subScope.ToString(CultureInfo.InvariantCulture);
        }

        public static string BuildIronKey(string weaponTemplateId, int aimIndex)
        {
            return BuildKey(weaponTemplateId, "iron", aimIndex);
        }
    }
}
