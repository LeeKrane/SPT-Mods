using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // At raid start, set every adjustable scope of the local player's equipment that is in an enabled
    // group to its Min % point. ScopeZoomHandler.Init reads ScopeZoomValue when the weapon visuals are
    // built; for a weapon already in hands, Init is run again so the live handler picks it up.
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class RaidStartPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(GameWorld __instance)
        {
            try
            {
                Player player = __instance.MainPlayer;
                if (player == null)
                    return;

                foreach (SightComponent sight in player.Equipment.GetItemComponentsInChildren<SightComponent>())
                {
                    GroupSettings group = ScopeSight.EnabledGroup(sight);
                    if (group == null)
                        continue;
                    group.Percents(out float lo, out _);
                    var fov = sight.AdjustableOpticData.MinMaxFov;
                    sight.ScopeZoomValue = ZoomMath.PercentToFov(lo, fov.x, fov.y);
                }

                if (player.HandsController is Player.FirearmController firearm && firearm.Firearms != null)
                {
                    foreach (SightModVisualControllers controller in firearm.Firearms._sightModVisualControllers)
                    {
                        if (controller != null && ScopeSight.EnabledGroup(controller.SightMod) != null
                            && controller.TryGetZoomHandler(out ScopeZoomHandler handler))
                            handler.Init(controller.SightMod);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }

            try
            {
                LogScopesOnce();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        // One line per adjustable optic template and scope index, to check where modded scopes land.
        private static void LogScopesOnce()
        {
            if (_logged || !Singleton<ItemFactory>.Instantiated)
                return;
            _logged = true;
            foreach (var template in Singleton<ItemFactory>.Instance.ItemTemplates.Values.OfType<SightModTemplate>())
            {
                if (!template.IsAdjustableOptic || template.Zooms == null)
                    continue;
                for (int i = 0; i < template.Zooms.Length; i++)
                {
                    float[] zooms = template.Zooms[i];
                    string group = GroupKeys.Find(zooms) ?? "unlisted";
                    Plugin.Log.LogInfo($"{template._id} {template._name} scope {i} " +
                        $"Zooms=[{(zooms == null ? "null" : string.Join(", ", zooms))}] MinMaxFov=({template.MinMaxFov.x}, {template.MinMaxFov.y}) group={group}");
                }
            }
        }
    }
}
