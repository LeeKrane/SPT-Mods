using System.Collections.Generic;
using BepInEx.Configuration;

namespace Krane.ScopeZoomPresets
{
    internal sealed class GroupSettings
    {
        public ConfigEntry<bool> Enabled;
        public ConfigEntry<int> MinPercent;
        public ConfigEntry<int> MaxPercent;

        // Min above Max is allowed in the config; the two are swapped here.
        public void Percents(out float lo, out float hi)
        {
            lo = MinPercent.Value;
            hi = MaxPercent.Value;
            ZoomMath.Order(ref lo, ref hi);
        }
    }

    internal static class GroupConfig
    {
        private static readonly Dictionary<string, GroupSettings> Groups = new Dictionary<string, GroupSettings>();

        public static void Bind(ConfigFile config)
        {
            var percent = new AcceptableValueRange<int>(0, 100);
            foreach (var range in GroupKeys.Ranges)
            {
                string section = GroupKeys.Name(range.Min, range.Max);
                Groups[section] = new GroupSettings
                {
                    Enabled = config.Bind(section, "Enabled", false,
                        "Use this group's toggle points and start its scopes at Min % at raid start."),
                    MinPercent = config.Bind(section, "Min %", 0, new ConfigDescription(
                        "Low-zoom toggle point. 0% is the scope's native lowest zoom.", percent)),
                    MaxPercent = config.Bind(section, "Max %", 100, new ConfigDescription(
                        "High-zoom toggle point. 100% is the scope's native highest zoom.", percent)),
                };
            }
        }

        public static GroupSettings Get(string key)
        {
            return key != null && Groups.TryGetValue(key, out var settings) ? settings : null;
        }
    }
}
