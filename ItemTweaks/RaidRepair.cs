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
//  Durability = min(Durability + gain, MaxDurability); MaxDurability -= round(rand(min,max kit degradation) * MaxDurability * wear%, 2)
//  where wear% is the "In-Raid Wear Percent" setting (default 10%: a loss the server would roll as 3.5 becomes 0.35).
//  Degradation base = RepairableComponent.RepairKitDegradation: armor material kit values (as the server); for weapons the
//  client maps it to the template's trader repair values (WeaponTemplate: MinRepairKitDegradation => MinRepairDegradation)
//  and has no kit values at all, while the server uses Min/MaxRepairKitDegradation (usually 0.005 lower). Accepted: at the
//  scaled wear the difference is negligible;
//  durability above the new max is clamped; face shield hits reset; common/rare buff roll (config values of repair.json).
// Amount repaired = what the client repair UI would offer: MaxDurability - minimum expected (scaled) kit degradation - Durability.
// Elite Charisma: like RepairService.ShouldRepairKitApplyDurabilityLoss, each repaired item has a 50% chance to take no max
// durability loss (rolled before the amount is computed, so the kit is not wasted). The client's skill-based
// HasEliteNoWearRepair is not used: the server ignores it for kit repairs. Not mirrored: skill XP gain.
// Targets: the item itself; for armor also the items in its slots, recursively (plates, inserts, visors), never items
// stored in its grids (armored rig pockets). Kits are collected once, planning covers all targets before anything is mutated.
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
        internal static ConfigEntry<int> WearPercent;

        private static RepairController _controller;
        private static GameWorld _controllerWorld;

        internal static void Init(ConfigFile config, Harmony harmony)
        {
            Enabled = config.Bind("Repair", "Allow Repair Kits In Raid", true,
                "Armor and weapon repair kits carried in raid equipment can be used in raid: drag a kit onto an item or use the item context menu Repair. Repair is instant, applied locally and saved at the end of the raid.");
            WearPercent = config.Bind("Repair", "In-Raid Wear Percent", 10,
                new ConfigDescription("Max durability lost by an in-raid kit repair, in percent of the normal kit repair loss (100 = as a stash kit repair, 0 = no loss). Applies to weapons, armor and plates.",
                    new AcceptableValueRange<int>(0, 100)));

            Patch(harmony, AccessTools.Method(typeof(ItemContextInteractionsSwitcher), nameof(ItemContextInteractionsSwitcher.IsActive), new[] { typeof(EItemInfoButton) }),
                postfix: nameof(IsActivePostfix));
            Patch(harmony, AccessTools.Method(typeof(ItemUiContext), nameof(ItemUiContext.OpenRepairWindow)), prefix: nameof(OpenRepairWindowPrefix));

            // Run after other mods' postfixes (UIFixes turns Repair into a trader submenu in this same getter). The getter is
            // virtual: patch the base declaration and any subclass that overrides it (none in 4.1.6, checked by name).
            var baseType = typeof(BaseInventoryItemContextInteractions);
            var types = new List<Type> { baseType };
            try { types.AddRange(baseType.Assembly.GetTypes().Where(t => t.IsSubclassOf(baseType))); }
            catch (ReflectionTypeLoadException e) { types.AddRange(e.Types.Where(t => t != null && t.IsSubclassOf(baseType))); }
            bool any = false;
            foreach (Type t in types)
            {
                var getter = AccessTools.DeclaredProperty(t, nameof(BaseInventoryItemContextInteractions.SubInteractions))?.GetMethod;
                if (getter == null) continue;
                any = true;
                Patch(harmony, getter, postfix: nameof(SubInteractionsPostfix), priority: Priority.Last);
            }
            if (!any) Plugin.Log.LogError("BaseInventoryItemContextInteractions.SubInteractions getter not found");
        }

        private static void Patch(Harmony harmony, MethodInfo target, string prefix = null, string postfix = null, int? priority = null)
            => Plugin.SafePatch(harmony, typeof(RaidRepair), target, prefix, postfix, priority);

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

        private const float KitEpsilon = 0.01f;

        private static List<RepairKit> KitsInEquipment()
        {
            var eq = Equipment;
            if (eq == null) return new List<RepairKit>();
            return eq.GetAllItems().OfType<RepairKit>().ToList();
        }

        private static bool KitUsable(RepairKit kit)
            => kit.Resource >= KitEpsilon && kit.PinLockState != EItemPinLockState.Locked && InEquipment(kit) && RaidProfile.Examined(kit);

        // What vanilla repairs for this item: the item itself; armor (ArmorRepairStrategy) also everything merged into it through
        // its slots (plates, inserts, visors, nested), in vanilla order. Grid contents (armored rig pockets) are never targets.
        private static List<Item> RepairTargets(Item item)
        {
            var list = new List<Item>();
            if (item.GetItemComponent<ArmorHolderComponent>() == null)
            {
                if (item.GetItemComponent<RepairableComponent>() != null) list.Add(item);
                return list;
            }
            CollectSlotItems(item, list);
            return list.OrderBy(OrderRepair).ToList();
        }

        private static void CollectSlotItems(Item item, List<Item> list)
        {
            if (item.GetItemComponent<RepairableComponent>() != null) list.Add(item);
            if (item is not CompoundItem compound || compound.Slots == null) return;
            foreach (Slot slot in compound.Slots)
            {
                Item contained = slot?.ContainedItem;
                if (contained != null) CollectSlotItems(contained, list);
            }
        }

        // ArmorRepairStrategy.OrderRepair
        private static readonly EBodyPartColliderType[] ColliderOrder =
        {
            EBodyPartColliderType.HeadCommon, EBodyPartColliderType.ParietalHead, EBodyPartColliderType.Eyes, EBodyPartColliderType.Jaw,
            EBodyPartColliderType.RibcageUp, EBodyPartColliderType.RibcageLow, EBodyPartColliderType.BackHead, EBodyPartColliderType.Ears,
            EBodyPartColliderType.NeckFront, EBodyPartColliderType.SpineTop, EBodyPartColliderType.SpineDown, EBodyPartColliderType.NeckBack,
            EBodyPartColliderType.Pelvis, EBodyPartColliderType.PelvisBack, EBodyPartColliderType.RightSideChestUp, EBodyPartColliderType.LeftSideChestUp,
            EBodyPartColliderType.RightSideChestDown, EBodyPartColliderType.LeftSideChestDown, EBodyPartColliderType.RightUpperArm, EBodyPartColliderType.LeftUpperArm,
            EBodyPartColliderType.RightForearm, EBodyPartColliderType.LeftForearm, EBodyPartColliderType.RightThigh, EBodyPartColliderType.LeftThigh,
            EBodyPartColliderType.RightCalf, EBodyPartColliderType.LeftCalf,
        };

        private static int OrderRepair(Item item)
        {
            if (!item.TryGetItemComponent<ArmorComponent>(out var armor)) return 0;
            if (item.CurrentAddress is SlotItemAddress addr && !addr.Slot.Locked) return -100;
            foreach (var collider in armor.ArmorColliders)
            {
                int i = Array.IndexOf(ColliderOrder, collider);
                if (i >= 0) return -30 + i;
            }
            return 0;
        }

        // Only used for its pure formula methods (repair price/quality, skill buff chance). The real constructor needs an
        // IEftSession (trader repairers), which does not exist in raid, so build the object without it and fill every field
        // the called methods (and the rest of the non-session API) read.
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
                List<Item> targets = RepairTargets(item);
                if (targets.Count == 0) return;
                if (__instance._itemContext is RepairItemContext ctx)
                {
                    RepairKit kit = ctx.RepairKit;
                    if (kit == null || !KitUsable(kit) || !__instance._itemController.Examined(kit)) return;
                    if (!targets.Any(kit.CanRepair)) return;
                }
                else if (!KitsInEquipment().Any(k => KitUsable(k) && targets.Any(k.CanRepair))) return;
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
        // Arguments are taken by type, not by name, so a renamed parameter cannot break the patch.
        private static bool OpenRepairWindowPrefix(object[] __args)
        {
            if (!Active) return true;
            try
            {
                InstantRepair(__args?.OfType<ItemContext>().FirstOrDefault());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
                NotificationManager.DisplayWarningNotification("Repair failed");
            }
            return false;
        }

        private sealed class Plan
        {
            public Item Item;
            public RepairableComponent Repairable;
            public float Gain, NewDurability, NewMax;
            public bool Buffed;
            public float BuffValue;
            public double BuffThreshold;
            public EBuffRarity BuffRarity;
            public ERepairBuffType BuffType;
        }

        private static void InstantRepair(ItemContext itemContext)
        {
            Item item = itemContext?.Item;
            if (!InEquipment(item)) { NotificationManager.DisplayWarningNotification("Item is not in your equipment"); return; }
            RepairKit dragged = (itemContext as RepairItemContext)?.RepairKit;
            var controller = GetController();

            // Kits collected once: dragged first, then smallest first. `remaining` is the planned kit state.
            List<RepairKit> kits = KitsInEquipment().Where(KitUsable).OrderBy(k => k.Resource).ToList();
            if (dragged != null && kits.Remove(dragged)) kits.Insert(0, dragged);
            var remaining = kits.ToDictionary(k => k, k => k.Resource);

            // Plan every target first; nothing is mutated until all of them are planned.
            bool kitFound = false;
            var plans = new List<Plan>();
            foreach (Item target in RepairTargets(item))
            {
                RepairableComponent repairable = target.GetItemComponent<RepairableComponent>();
                var usable = kits.Where(k => remaining[k] >= KitEpsilon && k.CanRepair(target)).ToList();
                if (repairable == null || usable.Count == 0) continue;
                kitFound = true;
                Plan plan = PlanTarget(controller, target, repairable, usable, remaining);
                if (plan != null) plans.Add(plan);
            }

            if (plans.Count == 0)
            {
                NotificationManager.DisplayWarningNotification(kitFound ? "Nothing to repair" : "No suitable repair kit");
                return;
            }

            Apply(plans, remaining);

            RepairableComponent root = item.GetItemComponent<RepairableComponent>();
            string text = "Item successfully repaired to".Localized();
            NotificationManager.DisplayMessageNotification(root != null ? $"{text} {root.Durability:F1}" : text);
            Singleton<GUISounds>.Instance.PlayUISound(EUISoundType.RepairComplete);
        }

        // Elite Charisma, RepairService.ShouldRepairKitApplyDurabilityLoss: 50% chance per item that max durability is not reduced.
        private static bool RollNoWear()
        {
            Skill charisma = RaidProfile.Skills?.Charisma;
            return charisma != null && charisma.IsEliteLevel && UnityEngine.Random.value < 0.5f;
        }

        // "In-Raid Wear Percent" as a factor on every max durability loss (and on the expected loss the amount is planned with).
        private static float WearFactor => Mathf.Clamp(WearPercent.Value, 0, 100) / 100f;

        // RepairKitsCollection.GetDegradationValues + ArmorRepairStrategy/DefaultRepairStrategy.HowMuchRepairScoresCanAccept,
        // with the minimum degradation 0 only when this repair is a no-wear one.
        private static float WantedGain(RepairController controller, Item target, RepairableComponent repairable, bool noWear)
        {
            Vector2 degradation = (target.IsArmorMod() ? 1f : (float)controller.GetBaseRepairQuality(target)) * repairable.TemplateDurability * repairable.RepairKitDegradation;
            return Mathf.Max(0f, repairable.MaxDurability - (noWear ? 0f : degradation.x * WearFactor) - repairable.Durability);
        }

        // Pure: computes the result for one target and spends points in `remaining` only. Returns null when nothing to do.
        private static Plan PlanTarget(RepairController controller, Item item, RepairableComponent repairable, List<RepairKit> kits, Dictionary<RepairKit, float> remaining)
        {
            bool noWear = RollNoWear();
            float want = WantedGain(controller, item, repairable, noWear);
            if (want / repairable.TemplateDurability < 0.001f) return null;

            float gain = 0f;
            var spend = new List<KeyValuePair<RepairKit, float>>();
            foreach (RepairKit kit in kits)
            {
                float left = want - gain;
                if (left <= 0.0001f) break;
                // Kit points per durability point: GetRepairPrice(kit, 1, item) is exactly RepairService.GetKitDivisor.
                double divisor = controller.GetRepairPrice(kit.RepairKitTemplate, 1.0, item);
                if (double.IsNaN(divisor) || double.IsInfinity(divisor) || divisor <= 0.0)
                    throw new InvalidOperationException($"invalid kit divisor {divisor} for {item.TemplateId}");
                float res = remaining[kit];
                if (res < KitEpsilon) continue;
                float take = Mathf.Min(left, (float)(res / divisor));
                if (take <= 0f) continue;
                float rest = res - (float)(take * divisor);
                spend.Add(new KeyValuePair<RepairKit, float>(kit, rest < KitEpsilon ? 0f : rest));
                gain += take;
            }
            if (gain <= 0f) return null;

            // RepairHelper.UpdateItemDurability: gain is added to Durability and MaxDurability, both capped at the current
            // MaxDurability (so max is unchanged); degradation is rand(kit degradation) * current max (* wear factor), to 2 dp.
            float maxBefore = repairable.MaxDurability;
            float newDurability = Mathf.Min(repairable.Durability + gain, maxBefore);
            Vector2 degradation = repairable.RepairKitDegradation;
            float loss = noWear ? 0f : (float)Math.Round(UnityEngine.Random.Range(degradation.x, degradation.y) * maxBefore * WearFactor, 2);
            float newMax = Mathf.Max(0f, maxBefore - loss);
            if (newDurability > newMax) newDurability = newMax;

            var plan = new Plan { Item = item, Repairable = repairable, Gain = gain, NewDurability = newDurability, NewMax = newMax };
            plan.Buffed = TryRollBuff(controller, item, repairable, gain, newDurability, out plan.BuffValue, out plan.BuffThreshold, out plan.BuffRarity, out plan.BuffType)
                && item.GetItemComponent<BuffComponent>() != null;

            foreach (var s in spend) remaining[s.Key] = s.Value;
            return plan;
        }

        private static void Apply(List<Plan> plans, Dictionary<RepairKit, float> remaining)
        {
            var touched = new List<RepairKit>();
            foreach (var pair in remaining)
            {
                if (Mathf.Approximately(pair.Key.Resource, pair.Value)) continue;
                pair.Key.RepairKitComponent.Resource = pair.Value;
                touched.Add(pair.Key);
            }
            foreach (Plan p in plans)
            {
                p.Repairable.MaxDurability = p.NewMax;
                p.Repairable.Durability = p.NewDurability;
                var faceShield = p.Item.GetItemComponent<FaceShieldComponent>();
                if (faceShield != null && faceShield.Hits > 0) faceShield.Hits = 0;
                var buff = p.Buffed ? p.Item.GetItemComponent<BuffComponent>() : null;
                if (buff != null)
                {
                    buff.Value = p.BuffValue;
                    buff.ThresholdDurability = p.BuffThreshold;
                    buff.Rarity = p.BuffRarity;
                    buff.BuffType = p.BuffType;
                }
            }
            foreach (var k in touched) k.RaiseRefreshEvent();
            foreach (Plan p in plans)
            {
                p.Item.UpdateAttributes();
                p.Item.RaiseRefreshEvent(refreshIcon: true);
            }
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
