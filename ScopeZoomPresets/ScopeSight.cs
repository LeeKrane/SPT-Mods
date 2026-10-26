using EFT.InventoryLogic;

namespace Krane.ScopeZoomPresets
{
    internal static class ScopeSight
    {
        public static float[] Zooms(SightComponent sight)
        {
            float[][] zooms = sight?._template?.Zooms;
            int index = sight?.SelectedScope ?? -1;
            return zooms != null && index >= 0 && index < zooms.Length ? zooms[index] : null;
        }

        // The variable entry the handler drives: the selected sub-scope's when variable, else the first one.
        public static float[] VariableEntry(SightComponent sight)
        {
            return GroupKeys.VariableEntry(sight?._template?.Zooms, sight?.SelectedScope ?? -1);
        }

        // False while a fixed backup sub-scope (e.g. the ADO's 1x) is selected.
        public static bool IsVariableSelected(SightComponent sight)
        {
            return GroupKeys.IsVariable(Zooms(sight));
        }

        public static bool IsAdjustable(SightComponent sight)
        {
            var data = sight?.AdjustableOpticData;
            return data != null && data.IsAdjustableOptic && ZoomMath.IsSupportedRange(data.MinMaxFov.x, data.MinMaxFov.y);
        }

        public static GroupSettings EnabledGroup(SightComponent sight)
        {
            if (!IsAdjustable(sight))
                return null;
            GroupSettings group = GroupConfig.Get(GroupKeys.Find(VariableEntry(sight)));
            return group != null && group.Enabled.Value ? group : null;
        }
    }
}
