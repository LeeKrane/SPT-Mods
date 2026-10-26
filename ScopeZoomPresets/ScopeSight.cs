using Comfort.Common;
using EFT;
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

        public static bool IsAdjustable(SightComponent sight)
        {
            var data = sight?.AdjustableOpticData;
            return data != null && data.IsAdjustableOptic && data.MinMaxFov.x != data.MinMaxFov.y;
        }

        // Bots and Fika observed players run the same ScopeZoomHandler code. A sight belongs to the
        // local player when it sits anywhere under the local player's equipment. GetRootItem cannot
        // be used: it stops at the weapon, before the InventoryEquipment parent.
        public static bool IsLocal(SightComponent sight)
        {
            if (sight?.Item == null || !Singleton<GameWorld>.Instantiated)
                return false;
            Player player = Singleton<GameWorld>.Instance.MainPlayer;
            return player != null && sight.Item.CurrentAddress?.IsChildOf(player.Equipment) == true;
        }

        public static GroupSettings EnabledGroup(SightComponent sight)
        {
            if (!IsAdjustable(sight))
                return null;
            GroupSettings group = GroupConfig.Get(GroupKeys.Find(Zooms(sight)));
            return group != null && group.Enabled.Value ? group : null;
        }
    }
}
