using System;
using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // At raid start, set every adjustable scope of the local player's equipment that is in an enabled
    // group to the lower of its Min % and Max % points. ScopeZoomHandler.Init reads ScopeZoomValue when the weapon visuals are
    // built; for a weapon already in hands, Init is run again so the live handler picks it up.
    [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.OnGameStarted))]
    internal static class RaidStartPatch
    {
        private static bool _logged;

        [HarmonyPostfix]
        private static void Postfix(GameWorld __instance)
        {
            // TarkovApplication also calls OnGameStarted after narrate scenes; not a raid start.
            if (__instance is NarrateGameWorld)
                return;

            try
            {
                // New raid, local player alive again (also covers a missed Dispose).
                LocalHandlers.SetWorld(__instance);
                LocalHandlers.LocalDead = false;
                Player player = __instance.MainPlayer;
                // Inert when the registry is disabled; LogScopesOnce below still runs.
                if (player == null || LocalHandlers.Disabled)
                    return;

                // The weapon may have been drawn while LocalDead was still set from the previous raid (the
                // Dispose that clears it can run later than the new raid's weapon draw), which left Set
                // inert. Register it now that the flag is clear.
                var firearm = player.HandsController as Player.FirearmController;
                if (firearm?.Firearms != null)
                    LocalHandlers.Set(firearm.Firearms);

                foreach (SightComponent sight in player.Equipment.GetItemComponentsInChildren<SightComponent>())
                {
                    GroupSettings group = ScopeSight.EnabledGroup(sight);
                    if (group == null)
                        continue;
                    group.Percents(out float lo, out _);
                    var fov = sight.AdjustableOpticData.MinMaxFov;
                    sight.ScopeZoomValue = ZoomMath.PercentToFov(lo, fov.x, fov.y);
                }

                if (firearm?.Firearms != null)
                {
                    foreach (SightModVisualControllers controller in firearm.Firearms._sightModVisualControllers)
                    {
                        // Per handler: one failing re-Init must not skip the others.
                        try
                        {
                            // Only registered (handler, sight) pairs: the same handlers the registry gates on.
                            if (controller != null && ScopeSight.EnabledGroup(controller.SightMod) != null
                                && controller.TryGetZoomHandler(out ScopeZoomHandler handler)
                                && LocalHandlers.Find(handler, controller.SightMod) is LocalHandlers.Entry entry)
                            {
                                handler.Init(controller.SightMod);
                                CurveSync.Correct(entry);
                            }
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogError(e);
                        }
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
                // Template-wide values, computed once per template.
                string fov = $"MinMaxFov=({GroupKeys.Invariant(template.MinMaxFov.x)}, {GroupKeys.Invariant(template.MinMaxFov.y)})";
                string variableGroup = GroupKeys.Find(GroupKeys.VariableEntry(template.Zooms)) ?? "unlisted";
                for (int i = 0; i < template.Zooms.Length; i++)
                {
                    float[] zooms = template.Zooms[i];
                    string group = GroupKeys.IsVariable(zooms) ? GroupKeys.Find(zooms) ?? "unlisted" : "fixed";
                    Plugin.Log.LogInfo($"{template._id} {template._name} scope {i} " +
                        $"Zooms=[{(zooms == null ? "null" : string.Join(", ", zooms.Select(GroupKeys.Invariant)))}] " +
                        $"{fov} group={group} templateGroup={variableGroup}");
                }
            }
        }
    }
}
