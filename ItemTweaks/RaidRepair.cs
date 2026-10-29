// Repair kits in raid. Vanilla blocks item repair in raid (ItemContextInteractionsSwitcher.IsActive(Repair) returns
// false while InGameStatus.InRaid) and the whole repair stack is bound to the menu profile: ItemUiContext.Session is
// null in raid, so Session?.RepairController is null, and RepairController scans the menu profile's Stash for kits.
//
// What this does, only while the setting is on and a raid is running (everything else stays vanilla):
//  - IsActive(Repair) postfix: re-allows the button (context menu and kit drag) for items in the player's raid equipment
//    when a suitable kit is carried in the raid equipment (any depth).
//  - OpenRepairWindow prefix: swaps in a RepairController bound to the raid player's Profile (built without a backend
//    session, no trader repairers).
//  - FormRepairKitsCollection prefix: scans MainPlayer's Equipment instead of the Stash.
//  - RepairerParametersPanel.AddRepairKit prefix: same Stash.Contains -> Equipment check.
//  - GetBaseRepairPriceCoefficient prefix: vanilla reads the menu-only HideoutRepresentation singleton (display only).
//  - RepairKitsCollection.RepairItems prefix: instead of RepairController.RepairItemsByRepairKit -> server "Repair"
//    operation, applies the repair locally. The end-of-raid profile save persists Durability/MaxDurability/Resource.
//
// Local repair mirrors SPT 4.1.6 RepairService.RepairItemByKit / RepairHelper.UpdateItemDurability / AddBuffToItem:
//  gain = kit points / kit divisor (client RepairController.GetRepairPrice, same formula as RepairService.GetKitDivisor);
//  Durability = min(Durability + gain, MaxDurability); MaxDurability -= round(rand(min,max kit degradation) * MaxDurability, 2)
//  (RepairableComponent.RepairKitDegradation = armor material or template values, as the server's repair helper uses);
//  durability above the new max is clamped; face shield hits reset; common/rare buff roll (config values of repair.json).
// Not mirrored: skill XP gain and the elite Charisma 50% no-wear roll.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using BepInEx.Configuration;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using EFT.Repairing;
using EFT.Trading;
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

        // These members are not reachable by direct access against the referenced assembly (the others used below are), so reflect.
        private static readonly FieldInfo InitialDragged = AccessTools.Field(typeof(RepairerParametersPanel), "_initialDraggedRepairKit");
        private static readonly FieldInfo Dragged = AccessTools.Field(typeof(RepairerParametersPanel), "_draggedRepairKit");
        private static readonly FieldInfo PanelItem = AccessTools.Field(typeof(RepairerParametersPanel), "_item");
        private static readonly FieldInfo PanelStrategy = AccessTools.Field(typeof(RepairerParametersPanel), "_repairStrategy");
        private static readonly FieldInfo RepairChangedEvent = AccessTools.Field(typeof(RepairController), "_onSuccessfulRepairChangedEvent");


        internal static void Init(ConfigFile config, Harmony harmony)
        {
            Enabled = config.Bind("Repair", "Allow Repair Kits In Raid", true,
                "Armor and weapon repair kits carried in raid equipment can be used in raid: drag a kit onto an item or use the item context menu Repair. Repair is applied locally and saved at the end of the raid.");

            Patch(harmony, AccessTools.Method(typeof(ItemContextInteractionsSwitcher), nameof(ItemContextInteractionsSwitcher.IsActive), new[] { typeof(EItemInfoButton) }),
                postfix: nameof(IsActivePostfix));
            Patch(harmony, AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.OpenRepairWindow)), prefix: nameof(OpenRepairWindowPrefix));
            Patch(harmony, AccessTools.Method(typeof(RepairerParametersPanel), nameof(RepairerParametersPanel.AddRepairKit)), prefix: nameof(AddRepairKitPrefix));
            Patch(harmony, AccessTools.Method(typeof(RepairKitsCollection), nameof(RepairKitsCollection.RepairItems)), prefix: nameof(RepairItemsPrefix));
            _harmony = harmony;
        }

        private static Harmony _harmony;
        private static bool _controllerPatched;

        // Patching a RepairController method JIT-compiles it, which runs RepairController's static constructor. At
        // plugin load that throws (game singletons not ready) and leaves the type broken for the session, so the
        // profile never finishes loading. Patch it on the first in-raid repair window instead, long after the
        // session has initialized the type normally.
        private static void EnsureControllerPatches()
        {
            if (_controllerPatched) return;
            _controllerPatched = true;
            Patch(_harmony, AccessTools.Method(typeof(RepairController), nameof(RepairController.FormRepairKitsCollection)), prefix: nameof(FormKitsPrefix));
            Patch(_harmony, AccessTools.Method(typeof(RepairController), nameof(RepairController.GetBaseRepairPriceCoefficient)), prefix: nameof(PriceCoefficientPrefix));
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

        // A controller bound to the raid player's profile. The real constructor needs an IEftSession (for trader repairers),
        // which does not exist in raid, so build the object without it and fill the same fields the constructor does.
        private static RepairController GetController()
        {
            var profile = RaidProfile;
            var world = Singleton<GameWorld>.Instance;
            if (_controller != null && ReferenceEquals(_controllerWorld, world)) return _controller;

            var c = (RepairController)FormatterServices.GetUninitializedObject(typeof(RepairController));
            var skills = profile.Skills;
            c._profile = profile;
            c._profileInventory = profile.Inventory;
            c._skillManager = skills;
            c._preallocatedRepairKits = new List<RepairKit>();
            var traders = typeof(RepairController).GetField("<TraderRepairers>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? typeof(RepairController).GetField("_traderRepairers", BindingFlags.Instance | BindingFlags.NonPublic);
            if (traders == null) throw new MissingFieldException("RepairController.TraderRepairers backing field");
            traders.SetValue(c, new List<Trader>());
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

        private static bool IsOurs(RepairController c) => c != null && ReferenceEquals(c, _controller) && Active
            && ReferenceEquals(_controllerWorld, Singleton<GameWorld>.Instance);

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
                    if (kit == null || kit.Resource <= 0f || !InEquipment(kit) || kit.PinLockState == EItemPinLockState.Locked) return;
                    if (!__instance._itemController.Examined(kit) || !__instance.CanRepair(ctx, item)) return;
                }
                else
                {
                    var profile = RaidProfile;
                    if (!KitsInEquipment().Any(k => k.Resource > 0f && k.PinLockState != EItemPinLockState.Locked && profile.Examined(k) && KitCanRepairTree(k, item))) return;
                }
                __result = true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static void OpenRepairWindowPrefix(ref RepairController repairController)
        {
            if (!Active) return;
            try
            {
                EnsureControllerPatches();
                repairController = GetController();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        private static bool FormKitsPrefix(RepairController __instance, Predicate<RepairKit> predicate, ref IEnumerable<RepairKitsCollection> __result)
        {
            if (!IsOurs(__instance)) return true;
            try
            {
                var dict = new Dictionary<RepairKitTemplate, RepairKitsCollection>();
                foreach (RepairKit kit in KitsInEquipment())
                {
                    if (kit.Resource > 0f && __instance._profile.Examined(kit) && predicate(kit) && kit.PinLockState != EItemPinLockState.Locked)
                    {
                        if (dict.TryGetValue(kit.RepairKitTemplate, out var existing)) existing.AddRepairKit(kit);
                        else dict.Add(kit.RepairKitTemplate, __instance.CreateRepairKitsCollection(kit));
                    }
                }
                __result = dict.Values.ToList();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return true;
            }
        }

        // Display only (price rate text); vanilla reads the menu-only hideout representation.
        private static bool PriceCoefficientPrefix(RepairController __instance, ref double __result)
        {
            if (!IsOurs(__instance)) return true;
            __result = 0.0;
            return false;
        }

        private static bool AddRepairKitPrefix(RepairerParametersPanel __instance, RepairKit repairKit)
        {
            if (!Active) return true;
            try
            {
                if (repairKit == (RepairKit)InitialDragged.GetValue(__instance)) Dragged.SetValue(__instance, repairKit);
                var dragged = (RepairKit)Dragged.GetValue(__instance);
                if ((InEquipment(repairKit) || repairKit == dragged) && __instance.CanRepair(repairKit, (Item)PanelItem.GetValue(__instance)))
                    ((IRepairStrategy)PanelStrategy.GetValue(__instance)).AddRepairKitToRepairers(repairKit);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                return true;
            }
        }

        private static bool RepairItemsPrefix(RepairKitsCollection __instance, RepairItem repairItem, RepairKit draggedRepairKit, ref Task<IResult> __result)
        {
            if (!IsOurs(__instance._repairController)) return true;
            try
            {
                __result = Task.FromResult(ApplyLocalRepair(__instance, repairItem, draggedRepairKit));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                __result = Task.FromResult<IResult>(new FailedResult(e.Message));
            }
            return false;
        }

        private static IResult ApplyLocalRepair(RepairKitsCollection collection, RepairItem repairItem, RepairKit dragged)
        {
            var controller = collection._repairController;
            Item item = Equipment?.GetAllItems().FirstOrDefault(i => i.Id == repairItem.Id);
            RepairableComponent repairable = item?.GetItemComponent<RepairableComponent>();
            if (repairable == null) return new FailedResult("You can't repair this kind of item");

            // Plan the kit spending first (smallest kit first, the dragged one before all, as RepairKitsCollection.RepairItems does).
            // Kits that left the raid equipment since the window was built are skipped. Nothing is mutated until everything succeeded.
            var kits = collection._repairKits.Where(k => InEquipment(k) && k.PinLockState != EItemPinLockState.Locked).OrderBy(k => k.Resource).ToList();
            if (dragged != null && kits.Remove(dragged)) kits.Insert(0, dragged);
            float need = repairItem.Count, used = 0f;
            var spend = new List<KeyValuePair<RepairKit, float>>();
            foreach (RepairKit kit in kits)
            {
                if (need <= 0.0001f) break;
                float res = kit.Resource;
                if (res <= 0f) continue;
                float take = Mathf.Min(res, need);
                spend.Add(new KeyValuePair<RepairKit, float>(kit, res - take));
                used += take;
                need -= take;
            }
            if (used <= 0f) return new FailedResult("Not enough repair points");

            // Kit points per durability point: GetRepairPrice(kit, 1, item) is exactly RepairService.GetKitDivisor
            // (it does not use the base price coefficient that PriceCoefficientPrefix zeroes).
            double divisor = controller.GetRepairPrice(collection._repairKitTemplate, 1.0, item);
            if (double.IsNaN(divisor) || double.IsInfinity(divisor) || divisor <= 0.0)
            {
                Plugin.Log.LogError($"Raid repair aborted: invalid kit divisor {divisor} for {item.TemplateId}");
                return new FailedResult("Repair failed");
            }
            float gain = (float)(used / divisor);

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
            item.RaiseRefreshEvent();
            (RepairChangedEvent?.GetValue(controller) as Action)?.Invoke();
            return new SuccessfulResult();
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
