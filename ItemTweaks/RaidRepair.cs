// Repair kits in raid. Vanilla blocks item repair in raid (ItemContextInteractionsSwitcher.IsActive(Repair) returns
// false while InGameStatus.InRaid) and the whole repair stack is bound to the menu profile (trader repairers, menu Stash).
//
// What this does, only while the setting is on and a raid is running (everything else stays vanilla). No repair window
// ever opens in raid; repair is instant, like the stash quick repair of UIFixes:
//  - IsActive(Repair) postfix: re-allows the button (context menu and kit drag) for items in the player's raid equipment
//    when a suitable kit is carried in the raid equipment (any depth).
//  - SubInteractions postfix (runs last, after UIFixes): Repair is a plain action, not a trader submenu.
//  - ItemUiContext.OpenRepairWindow prefix: the one call both the context menu and a kit drag end in. Replaced by an
//    instant repair of the item (armor: its plates and inserts too) to the kit-repairable maximum, no confirmation.
//    A dragged kit is spent first, then the smallest kits first.
// Nothing patches RepairController (its static constructor throws if JIT-ed at plugin load); a controller bound to the
// raid profile is built lazily on the first repair and only used for its pure formula methods.
//
// Local repair mirrors SPT 4.1.6 RepairService.RepairItemByKit / RepairHelper.UpdateItemDurability / AddBuffToItem:
//  gain = kit points / kit divisor (client RepairController.GetRepairPrice, same formula as RepairService.GetKitDivisor);
//  Durability = min(Durability + gain, MaxDurability); MaxDurability -= round(rand(min,max kit degradation) * MaxDurability, 2)
//  (RepairableComponent.RepairKitDegradation = armor material or template values, as the server's repair helper uses);
//  durability above the new max is clamped; face shield hits reset; common/rare buff roll (config values of repair.json).
// Amount repaired = what the client repair UI would offer: MaxDurability - minimum expected kit degradation - Durability.
// Not mirrored: skill XP gain and the elite Charisma 50% no-wear roll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.Repairing;
using EFT.UI;
using HarmonyLib;
using UnityEngine;

namespace Krane.ItemTweaks
{
    internal static class RaidRepair
    {
        internal static ConfigEntry<bool> Enabled;

        private static RepairController _controller;
        private static GameWorld _controllerWorld;

        internal static void Init(ConfigFile config, Harmony harmony)
        {
            Enabled = config.Bind("Repair", "Allow Repair Kits In Raid", true,
                "Armor and weapon repair kits carried in raid equipment can be used in raid: drag a kit onto an item or use the item context menu Repair. Repair is instant, applied locally and saved at the end of the raid.");

            Patch(harmony, AccessTools.Method(typeof(ItemContextInteractionsSwitcher), nameof(ItemContextInteractionsSwitcher.IsActive), new[] { typeof(EItemInfoButton) }),
                postfix: nameof(IsActivePostfix));
            Patch(harmony, AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.OpenRepairWindow)), prefix: nameof(OpenRepairWindowPrefix));

            // Run after other mods' postfixes (UIFixes turns Repair into a trader submenu in this same getter).
            try
            {
                var getter = AccessTools.DeclaredProperty(typeof(BaseInventoryItemContextInteractions), nameof(BaseInventoryItemContextInteractions.SubInteractions))?.GetMethod;
                if (getter == null) throw new MissingMethodException("BaseInventoryItemContextInteractions.SubInteractions getter");
                harmony.Patch(getter, postfix: new HarmonyMethod(typeof(RaidRepair), nameof(SubInteractionsPostfix)) { priority = Priority.Last });
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null)
            => Plugin.SafePatch(harmony, typeof(RaidRepair), target, prefix, postfix);

        // Setting on, a raid is running and we have a local player. InGameStatus.InRaid is what vanilla's Gameplay check reads.
        internal static bool Active
        {
            get
            {
                try
                {
#pragma warning disable CS0618
                    return Enabled.Value && InGameStatus.InRaid && Singleton<GameWorld>.Instantiated
                        && Singleton<GameWorld>.Instance.MainPlayer != null;
#pragma warning restore CS0618
                }
                catch
                {
                    return false;
                }
            }
        }

        private static Profile RaidProfile => Singleton<GameWorld>.Instance.MainPlayer.Profile;

        private static InventoryEquipment Equipment => RaidProfile?.Inventory?.Equipment;

        private static bool InEquipment(Item item)
        {
            var eq = Equipment;
            return item != null && eq != null && item.IsChildOf(eq);
        }

        private static IEnumerable<RepairKit> KitsInEquipment()
        {
            var eq = Equipment;
            if (eq == null) return Enumerable.Empty<RepairKit>();
            return eq.GetAllItems().OfType<RepairKit>();
        }

        private static bool KitCanRepairTree(RepairKit kit, Item item)
        {
            if (kit.CanRepair(item)) return true;
            return item.GetItemComponentsInChildren<RepairableComponent>().Any(c => kit.CanRepair(c.Item));
        }

        private static bool KitUsable(RepairKit kit) => kit.Resource > 0f && kit.PinLockState != EItemPinLockState.Locked && InEquipment(kit);

        // Only used for its pure formula methods (repair price/quality, skill buff chance). The real constructor needs an
        // IEftSession (trader repairers), which does not exist in raid, so build the object without it and fill the fields
        // those methods read.
        private static RepairController GetController()
        {
            var profile = RaidProfile;
            var world = Singleton<GameWorld>.Instance;
            if (_controller != null && ReferenceEquals(_controllerWorld, world)) return _controller;

            var c = (RepairController)FormatterServices.GetUninitializedObject(typeof(RepairController));
            var skills = profile.Skills;
            c._profile = profile;
            c._skillManager = skills;
            c._lightVestRepairParameters = new RepairController.RepairParameters(skills, EBonusType.UnlockArmorRepair, EBonusType.RepairArmorBonus, ESkillId.LightVests,
                RepairController.LightVestWearChanceReducePerLevel, RepairController.SkillsSettings.LightVests.BuffSettings, skills.LightVestDeteriorationChanceOnRepairReduce);
            c._heavyVestRepairParameters = new RepairController.RepairParameters(skills, EBonusType.UnlockArmorRepair, EBonusType.RepairArmorBonus, ESkillId.HeavyVests,
                RepairController.HeavyVestWearChanceReducePerLevel, RepairController.SkillsSettings.HeavyVests.BuffSettings, skills.HeavyVestDeteriorationChanceOnRepairReduce);
            c._weaponRepairParameters = new RepairController.RepairParameters(skills, EBonusType.UnlockWeaponRepair, EBonusType.RepairWeaponBonus, ESkillId.WeaponTreatment,
                RepairController.WeaponTreatmentWearChanceReducePerLevel, RepairController.SkillsSettings.WeaponTreatment.BuffSettings, skills.WearChanceRepairGunsReduceEliteLevel);
            c._intellectRepairParameters = new RepairController.RepairParameters(skills, EBonusType.UnlockArmorRepair, EBonusType.RepairArmorBonus, ESkillId.Intellect,
                RepairController.IntellectWearChanceReducePerLevel, null, null);
            _controller = c;
            _controllerWorld = world;
            return c;
        }

        // Context menu entry and kit drag target.
        private static void IsActivePostfix(ItemContextInteractionsSwitcher __instance, EItemInfoButton button, ref bool __result)
        {
            if (__result || button != EItemInfoButton.Repair || !Active) return;
            try
            {
                Item item = __instance._item;
                if (item == null || !item.ItemInteractionButtons.Contains(button)) return;
                if (__instance.BadContextType || !__instance.Examined || !InEquipment(item)) return;
                if (!item.GetItemComponentsInChildren<RepairableComponent>().Any()) return;
                if (__instance._itemContext is RepairItemContext ctx)
                {
                    RepairKit kit = ctx.RepairKit;
                    if (kit == null || !KitUsable(kit)) return;
                    if (!__instance._itemController.Examined(kit) || !__instance.CanRepair(ctx, item)) return;
                }
                else
                {
                    var profile = RaidProfile;
                    if (!KitsInEquipment().Any(k => KitUsable(k) && profile.Examined(k) && KitCanRepairTree(k, item))) return;
                }
                __result = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        // Repair stays a direct action in raid: drop it from the submenu list.
        private static void SubInteractionsPostfix(ref IEnumerable<EItemInfoButton> __result)
        {
            if (__result == null || !Active) return;
            __result = __result.Where(b => b != EItemInfoButton.Repair);
        }

        // Both the context menu Repair and dropping a kit on an item end here. Never open the window in raid.
        private static bool OpenRepairWindowPrefix(ItemContext itemContext)
        {
            if (!Active) return true;
            try
            {
                InstantRepair(itemContext);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                NotificationManager.DisplayWarningNotification("Repair failed");
            }
            return false;
        }

        private static void InstantRepair(ItemContext itemContext)
        {
            Item item = itemContext?.Item;
            if (!InEquipment(item)) { NotificationManager.DisplayWarningNotification("Item is not in your equipment"); return; }
            RepairKit dragged = (itemContext as RepairItemContext)?.RepairKit;
            var controller = GetController();

            // Armor repairs its plates and inserts too (vanilla ArmorRepairStrategy); anything else only itself.
            List<Item> targets = item.GetItemComponent<ArmorHolderComponent>() != null
                ? item.GetItemComponentsInChildren<RepairableComponent>().Select(c => c.Item).Distinct().ToList()
                : new List<Item> { item };

            bool kitFound = false;
            int repaired = 0;
            foreach (Item target in targets)
            {
                RepairableComponent repairable = target.GetItemComponent<RepairableComponent>();
                if (repairable == null) continue;
                var kits = KitsInEquipment().Where(k => KitUsable(k) && k.CanRepair(target) && ProfileExamined(k)).OrderBy(k => k.Resource).ToList();
                if (dragged != null && kits.Remove(dragged)) kits.Insert(0, dragged);
                if (kits.Count == 0) continue;
                kitFound = true;

                float want = WantedGain(controller, target, repairable);
                if (want / repairable.TemplateDurability < 0.001f) continue;
                if (ApplyLocalRepair(controller, target, repairable, kits, want)) repaired++;
            }

            if (repaired > 0)
            {
                RepairableComponent root = item.GetItemComponent<RepairableComponent>();
                string text = "Item successfully repaired to".Localized();
                NotificationManager.DisplayMessageNotification(root != null ? $"{text} {root.Durability:F1}" : text);
                Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.RepairComplete);
            }
            else
            {
                NotificationManager.DisplayWarningNotification(kitFound ? "Nothing to repair" : "No suitable repair kit");
            }
        }

        private static bool ProfileExamined(RepairKit kit) => RaidProfile.Examined(kit);

        // RepairKitsCollection.GetDegradationValues + ArmorRepairStrategy/DefaultRepairStrategy.HowMuchRepairScoresCanAccept.
        private static float WantedGain(RepairController controller, Item target, RepairableComponent repairable)
        {
            Vector2 degradation = (target.IsArmorMod() ? 1f : (float)controller.GetBaseRepairQuality(target)) * repairable.TemplateDurability * repairable.RepairKitDegradation;
            if (controller.HasEliteNoWearRepair(target)) degradation.x = 0f;
            return Mathf.Max(0f, repairable.MaxDurability - degradation.x - repairable.Durability);
        }

        // Plans the kit spending first, nothing is mutated until everything succeeded.
        private static bool ApplyLocalRepair(RepairController controller, Item item, RepairableComponent repairable, List<RepairKit> kits, float want)
        {
            float gain = 0f;
            var spend = new List<KeyValuePair<RepairKit, float>>();
            foreach (RepairKit kit in kits)
            {
                float remaining = want - gain;
                if (remaining <= 0.0001f) break;
                // Kit points per durability point: GetRepairPrice(kit, 1, item) is exactly RepairService.GetKitDivisor.
                double divisor = controller.GetRepairPrice(kit.RepairKitTemplate, 1.0, item);
                if (double.IsNaN(divisor) || double.IsInfinity(divisor) || divisor <= 0.0)
                {
                    Plugin.Log.LogError($"Raid repair aborted: invalid kit divisor {divisor} for {item.TemplateId}");
                    return false;
                }
                float res = kit.Resource;
                float take = Mathf.Min(remaining, (float)(res / divisor));
                if (take <= 0f) continue;
                spend.Add(new KeyValuePair<RepairKit, float>(kit, Mathf.Max(0f, res - (float)(take * divisor))));
                gain += take;
            }
            if (gain <= 0f) return false;

            // RepairHelper.UpdateItemDurability: gain is added to Durability and MaxDurability, both capped at the current
            // MaxDurability (so max is unchanged); degradation is rand(kit degradation) * current max, to 2 dp.
            float maxBefore = repairable.MaxDurability;
            float newDurability = Mathf.Min(repairable.Durability + gain, maxBefore);
            Vector2 degradation = repairable.RepairKitDegradation;
            float loss = (float)Math.Round(UnityEngine.Random.Range(degradation.x, degradation.y) * maxBefore, 2);
            float newMax = Mathf.Max(0f, maxBefore - loss);
            if (newDurability > newMax) newDurability = newMax;

            bool buffed = TryRollBuff(controller, item, repairable, gain, newDurability, out float buffValue, out double buffThreshold, out EBuffRarity buffRarity, out ERepairBuffType buffType);
            var buff = buffed ? item.GetItemComponent<BuffComponent>() : null;
            var faceShield = item.GetItemComponent<FaceShieldComponent>();

            // Everything computed; mutate.
            foreach (var s in spend) s.Key.RepairKitComponent.Resource = s.Value;
            repairable.MaxDurability = newMax;
            repairable.Durability = newDurability;
            if (faceShield != null && faceShield.Hits > 0) faceShield.Hits = 0;
            if (buff != null)
            {
                buff.Value = buffValue;
                buff.ThresholdDurability = buffThreshold;
                buff.Rarity = buffRarity;
                buff.BuffType = buffType;
            }

            foreach (var s in spend) s.Key.RaiseRefreshEvent();
            item.UpdateAttributes();
            item.RaiseRefreshEvent(refreshIcon: true);
            return true;
        }

        // RepairService.AddBuffToItem / ShouldBuffItem / AddBuff with repair.json repairKit values (plain multipliers, not percents):
        // rarity Common 5 : Rare 1, armor DamageReduction, weapon WeaponSpread or MalfunctionProtections 1 : 1, active from 75-90% of the new durability.
        private static bool TryRollBuff(RepairController controller, Item item, RepairableComponent repairable, float gain, float newDurability,
            out float value, out double threshold, out EBuffRarity rarity, out ERepairBuffType type)
        {
            value = 0f; threshold = 0.0; rarity = EBuffRarity.Common; type = ERepairBuffType.DamageReduction;
            if (!controller.IsEligibleForBuff(item)) return false;
            if (!controller.TryGetEnhancementChance(item, gain / repairable.TemplateDurability, out double chance)) return false;
            if (UnityEngine.Random.value > chance) return false;
            if (item.GetItemComponent<BuffComponent>() == null) return false;

            bool rare = UnityEngine.Random.value * 6f >= 5f;
            type = item is Weapon
                ? (UnityEngine.Random.value < 0.5f ? ERepairBuffType.WeaponSpread : ERepairBuffType.MalfunctionProtections)
                : ERepairBuffType.DamageReduction;
            float min, max;
            switch (type)
            {
                case ERepairBuffType.WeaponSpread: min = rare ? 0.8f : 0.9f; max = rare ? 0.9f : 0.99f; break;
                case ERepairBuffType.MalfunctionProtections: min = rare ? 0.75f : 0.94f; max = rare ? 0.9f : 0.96f; break;
                default: min = rare ? 0.8f : 0.9f; max = rare ? 0.9f : 0.98f; break;
            }
            double percent = UnityEngine.Random.Range(75f, 90f);
            value = UnityEngine.Random.Range(min, max);
            threshold = Math.Round(percent / 100.0 * newDurability);
            rarity = rare ? EBuffRarity.Rare : EBuffRarity.Common;
            return true;
        }
    }
}
