using System;
using System.Globalization;
using System.Linq;

namespace Krane.ScopeZoomPresets
{
    // Fixed list of adjustable optic groups, keyed by the native label range in the scope's Zooms entry.
    internal static class GroupKeys
    {
        public static readonly (float Min, float Max)[] Ranges =
        {
            (1, 4), (1, 6), (1, 8), (2.5f, 8), (3, 9), (3, 10),
            (3, 20), (3, 24), (4, 16), (5, 25), (6.5f, 20), (7, 35),
        };

        public static string Name(float min, float max)
        {
            return $"{Format(min)}x-{Format(max)}x";
        }

        public static string Find(float[] zooms)
        {
            if (zooms == null || zooms.Length == 0)
                return null;
            float min = (float)Math.Round(zooms.Min(), 1);
            float max = (float)Math.Round(zooms.Max(), 1);
            foreach (var range in Ranges)
            {
                if (Math.Abs(range.Min - min) < 0.01f && Math.Abs(range.Max - max) < 0.01f)
                    return Name(range.Min, range.Max);
            }
            return null;
        }

        private static string Format(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
