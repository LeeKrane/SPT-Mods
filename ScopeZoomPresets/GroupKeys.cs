using System;
using System.Globalization;

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
            float min = (float)Math.Round(MinLabel(zooms), 1);
            float max = (float)Math.Round(MaxLabel(zooms), 1);
            for (int i = 0; i < Ranges.Length; i++)
            {
                if (Math.Abs(Ranges[i].Min - min) < 0.01f && Math.Abs(Ranges[i].Max - max) < 0.01f)
                    return Name(Ranges[i].Min, Ranges[i].Max);
            }
            return null;
        }

        // Lowest and highest label of a non-empty entry. Allocation-free: MinLabel is read on the HUD path.
        public static float MinLabel(float[] entry)
        {
            float min = entry[0];
            for (int i = 1; i < entry.Length; i++)
                min = Math.Min(min, entry[i]);
            return min;
        }

        public static float MaxLabel(float[] entry)
        {
            float max = entry[0];
            for (int i = 1; i < entry.Length; i++)
                max = Math.Max(max, entry[i]);
            return max;
        }

        // Full-precision, culture independent (log output); Format below rounds for group names.
        public static string Invariant(float value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        // A scope entry is variable when its labels span more than one value. Allocation-free: it is
        // checked on the per-frame HUD path.
        public static bool IsVariable(float[] entry)
        {
            if (entry == null)
                return false;
            for (int i = 1; i < entry.Length; i++)
            {
                if (entry[i] != entry[0])
                    return true;
            }
            return false;
        }

        // Sights like the NcSTAR ADO list a variable optic and a fixed backup ([[3, 9], [1]]); the
        // handler always drives the variable one. The entry of the selected sub-scope when that is
        // variable, otherwise the first variable entry; null when there is none.
        public static float[] VariableEntry(float[][] zooms, int selected = -1)
        {
            if (zooms == null)
                return null;
            if (selected >= 0 && selected < zooms.Length && IsVariable(zooms[selected]))
                return zooms[selected];
            for (int i = 0; i < zooms.Length; i++)
            {
                if (IsVariable(zooms[i]))
                    return zooms[i];
            }
            return null;
        }

        private static string Format(float value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
