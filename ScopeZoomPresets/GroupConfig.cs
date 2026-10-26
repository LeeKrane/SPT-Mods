using System.Collections.Generic;
using BepInEx.Configuration;

namespace Krane.ScopeZoomPresets
{
    internal sealed class GroupSettings
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<float> MinZoom;
        public ConfigEntry<float> MaxZoom;

        // Min above Max is allowed in the config; the two are swapped here. Values are snapped to 0.1x.
        public void Points(out float lo, out float hi)
        {
            lo = ZoomMath.SnapZoom(MinZoom.Value);
            hi = ZoomMath.SnapZoom(MaxZoom.Value);
            ZoomMath.Order(ref lo, ref hi);
        }
    }

    internal static class GroupConfig
    {
        private static readonly Dictionary<string, GroupSettings> Groups = new Dictionary<string, GroupSettings>();

        public static void Bind(ConfigFile config)
        {
            foreach (var range in GroupKeys.Ranges)
            {
                string section = GroupKeys.Name(range.Min, range.Max);
                var zoom = new AcceptableValueRange<float>(range.Min, range.Max);
                Groups[section] = new GroupSettings
                {
                    Enabled = config.Bind(section, "Enabled", false,
                        "Use this group's toggle points and start its scopes at the lower of Min zoom and Max zoom at raid start."),
                    MinZoom = config.Bind(section, "Min zoom", range.Min, new ConfigDescription(
                        "Low-zoom toggle point in x magnification.", zoom)),
                    MaxZoom = config.Bind(section, "Max zoom", range.Max, new ConfigDescription(
                        "High-zoom toggle point in x magnification.", zoom)),
                };
                Snap(Groups[section].MinZoom);
                Snap(Groups[section].MaxZoom);
            }
        }

        // Keeps the stored value on the 0.1 grid. The reassignment fires SettingChanged once more, which then
        // finds the value already snapped.
        private static void Snap(ConfigEntry<float> entry)
        {
            entry.SettingChanged += (_, __) =>
            {
                float snapped = ZoomMath.SnapZoom(entry.Value);
                if (snapped != entry.Value)
                    entry.Value = snapped;
            };
        }

        public static GroupSettings Get(string key)
        {
            return key != null && Groups.TryGetValue(key, out var settings) ? settings : null;
        }
    }
}
