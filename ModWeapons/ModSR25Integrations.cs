using System.Collections;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace ModWeapons;

/// <summary>
/// Quests, bots (database and Progressive Bot System pools) and the Jaeger / NATO Quartermaster offers. TypePriority sits after NATO Quartermaster
/// (TraderRegistration + 2), which builds its assort in its own OnLoad, and before TraderCallbacks,
/// RagfairCallbacks and PresetCallbacks so flea trader offers and presets pick everything up.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 10)]
public sealed class ModSR25Integrations(
    ILogger<ModSR25Integrations> logger,
    TemplateTable templateTable,
    GlobalTable globalTable,
    BotTable botTable,
    TradersTable tradersTable,
    HandbookHelper handbookHelper,
    IServiceProvider services) : IOnLoad
{
    private static readonly MongoId Sr25 = new(Ids.Sr25);
    private static readonly MongoId ModSr25 = new(Ids.ModSr25);

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!templateTable.Items.ContainsKey(ModSr25))
        {
            logger.LogWarning("[ModWeapons] Item missing, quests, bots and traders skipped");
            return Task.CompletedTask;
        }

        Guard("Quests", AddToQuests);
        Guard("Bots", AddToBots);
        Guard("Progressive Bot System", AddToApbs);
        Guard("Jaeger", AddJaegerOffer);
        Guard("NATO Quartermaster", AddNatoOffer);
        return Task.CompletedTask;
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { logger.LogError("[ModWeapons] {What} failed: {Error}", what, e); }
    }

    // ---- Quests ----------------------------------------------------------------------------------

    private void AddToQuests()
    {
        var sr25 = Ids.Sr25;
        var added = 0;
        var quests = 0;
        foreach (var quest in templateTable.Quests.Values)
        {
            var before = added;
            var types = quest.Conditions;
            if (types == null) continue;
            foreach (var list in new[] { types.Started, types.AvailableForStart, types.AvailableForFinish, types.Success, types.Fail })
            {
                foreach (var condition in list ?? [])
                {
                    // HandoverItem / FindItem / SellItemToTrader / WeaponAssembly list their items in target.
                    var target = condition.Target;
                    if (target?.List is { } ids && ids.Contains(sr25) && !ids.Contains(Ids.ModSr25))
                    {
                        ids.Add(Ids.ModSr25);
                        added++;
                    }
                    else if (target?.Item == sr25)
                    {
                        condition.Target = new ListOrT<string>([sr25, Ids.ModSr25], null);
                        added++;
                    }

                    // Kill conditions list allowed weapons in counter.conditions[].weapon.
                    foreach (var counter in condition.Counter?.Conditions ?? [])
                    {
                        if (counter.Weapon != null && counter.Weapon.Contains(sr25) && counter.Weapon.Add(Ids.ModSr25))
                            added++;
                    }
                }
            }
            if (added > before) quests++;
        }
        logger.LogInformation("[ModWeapons] Added MOD-SR-25 to {Count} quest condition(s) across {Quests} quest(s)", added, quests);
    }

    // ---- Bots ------------------------------------------------------------------------------------

    private void AddToBots()
    {
        var pools = 0;
        var modEntries = 0;
        var bots = 0;
        foreach (var bot in botTable.Types.Values)
        {
            var inventory = bot?.BotInventory;
            if (inventory == null) continue;
            var before = pools + modEntries;

            var weightPools = new List<Dictionary<MongoId, double>?>(inventory.Equipment?.Values.AsEnumerable() ?? []);
            if (inventory.Items is { } items)
                weightPools.AddRange([items.Backpack, items.Pockets, items.SecuredContainer, items.SpecialLoot, items.TacticalVest]);
            foreach (var pool in weightPools)
            {
                if (SplitWeight(pool)) pools++;
            }

            // Mod pool: same attachments the SR-25 may spawn with.
            if (CopyModPool(inventory.Mods)) modEntries++;
            if (pools + modEntries > before) bots++;
        }
        logger.LogInformation("[ModWeapons] Added MOD-SR-25 to {Pools} bot weapon/loot pool(s) and {Mods} mod pool(s) across {Bots} bot type(s)", pools, modEntries, bots);
    }

    /// <summary>Moves 25% of the SR-25's weight to the new item; pool total stays unchanged. Weights are doubles, nothing rounds them.</summary>
    private static bool SplitWeight(Dictionary<MongoId, double>? pool)
    {
        if (pool == null || !pool.TryGetValue(Sr25, out var weight) || pool.ContainsKey(ModSr25)) return false;
        pool[Sr25] = weight * 0.75;
        pool[ModSr25] = weight * 0.25;
        return true;
    }

    private static bool CopyModPool(Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>? mods)
    {
        if (mods == null || !mods.TryGetValue(Sr25, out var sr25Mods) || mods.ContainsKey(ModSr25)) return false;
        mods[ModSr25] = sr25Mods.ToDictionary(kv => kv.Key, kv => new HashSet<MongoId>(kv.Value));
        return true;
    }

    /// <summary>
    /// Acidphantasm Progressive Bot System (soft dependency) generates bot weapons from its own tiered pools and
    /// ignores the database bot types, so it gets the same 75/25 split and mod pool copy. Reached by reflection
    /// to avoid a hard reference. Applied to its clean data too, which it re-clones into the working data when
    /// its config or preset changes at runtime.
    /// </summary>
    private void AddToApbs()
    {
        var loaderType = AppDomain.CurrentDomain.GetAssemblies()
            .Select(a => a.GetType("ProgressiveBotSystem.Helpers.DataLoader"))
            .FirstOrDefault(t => t != null);
        if (loaderType == null || services.GetService(loaderType) is not { } loader)
        {
            logger.LogInformation("[ModWeapons] Progressive Bot System not installed, skipping its bot pools");
            return;
        }

        var pools = 0;
        var modEntries = 0;
        foreach (var name in new[] { "AllTierDataClean", "AllTierDataDirty" })
        {
            var all = loaderType.GetProperty(name)?.GetValue(loader);
            if (all?.GetType().GetProperty("Tiers")?.GetValue(all) is not IDictionary tiers) continue;
            foreach (var tier in tiers.Values)
            {
                if (tier == null) continue;
                var equipmentData = tier.GetType().GetProperty("EquipmentData")?.GetValue(tier);
                var bots = equipmentData?.GetType().GetMethod("GetAllBots")?.Invoke(equipmentData, null) as IEnumerable;
                foreach (var bot in bots ?? Array.Empty<object>())
                {
                    if (bot?.GetType().GetProperty("Equipment")?.GetValue(bot) is not IDictionary slots) continue;
                    foreach (var pool in slots.Values)
                    {
                        if (SplitWeight(pool as Dictionary<MongoId, double>)) pools++;
                    }
                }
                if (CopyModPool(tier.GetType().GetProperty("ModsData")?.GetValue(tier) as Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>))
                    modEntries++;
            }
        }
        logger.LogInformation("[ModWeapons] Added MOD-SR-25 to {Pools} Progressive Bot System weapon pool(s) and {Mods} mod pool(s)", pools, modEntries);
    }

    // ---- Traders ---------------------------------------------------------------------------------

    /// <summary>Jaeger LL4, roubles: 2x Peacekeeper's SR-25 price, converted to roubles.</summary>
    private void AddJaegerOffer()
    {
        var price = TryReadOfferRoubles(Ids.Peacekeeper, "Peacekeeper") * 2;
        if (price is null or <= 0)
        {
            price = FallbackPrice();
            logger.LogWarning("[ModWeapons] Peacekeeper SR-25 price not found, Jaeger offer uses fallback {Price} RUB", price);
        }
        AddOffer(Ids.Jaeger, "Jaeger", Ids.JaegerAssort, 4, Math.Round(price.Value));
    }

    /// <summary>NATO Quartermaster (soft dependency), its only loyalty level, roubles: 2x its own SR-25 price.</summary>
    private void AddNatoOffer()
    {
        if (!tradersTable.ContainsKey(new MongoId(Ids.NatoQuartermaster)))
        {
            logger.LogInformation("[ModWeapons] NATO Quartermaster not installed, skipping its offer");
            return;
        }
        var price = TryReadOfferRoubles(Ids.NatoQuartermaster, "NATO Quartermaster") * 2;
        if (price is null or <= 0)
        {
            price = FallbackPrice();
            logger.LogWarning("[ModWeapons] NATO Quartermaster has no SR-25 offer, its MOD-SR-25 offer uses fallback {Price} RUB", price);
        }
        AddOffer(Ids.NatoQuartermaster, "NATO Quartermaster", Ids.NatoAssort, 1, Math.Round(price.Value));
    }

    /// <summary>Fallback: 2x SR-25 handbook price (the same as MOD-SR-25's own handbook price).</summary>
    private double FallbackPrice() => 2 * handbookHelper.GetTemplatePrice(Sr25);

    /// <summary>Price of the trader's SR-25 root offer in roubles, or null when absent or not paid in money.</summary>
    private double? TryReadOfferRoubles(string traderId, string name)
    {
        if (!tradersTable.TryGetValue(new MongoId(traderId), out var trader) || trader.Assort == null) return null;
        var root = trader.Assort.Items.FirstOrDefault(i => i.Template == Sr25 && i.SlotId == "hideout");
        if (root == null || !trader.Assort.BarterScheme.TryGetValue(root.Id, out var options)) return null;
        // The first option is one payment: every entry of it must be money, summed in roubles.
        var payment = options.FirstOrDefault();
        if (payment == null || payment.Count == 0) return null;

        var total = 0d;
        foreach (var entry in payment)
        {
            if (entry.Count is not { } count || !Money.GetMoneyTpls().Contains(entry.Template))
            {
                logger.LogWarning("[ModWeapons] {Trader} sells SR-25 for a non-currency barter, cannot derive a price", name);
                return null;
            }
            total += entry.Template == Money.ROUBLES ? count : handbookHelper.InRoubles(count, entry.Template);
        }
        return total;
    }

    /// <summary>
    /// Adds the full default build as one offer. The root is unlimited-stock with BuyRestrictionMax 1:
    /// SPT 4.1.6 never restores a decremented StackObjectsCount on restock (the "pristine" assort is the
    /// live one) but resets each profile's purchase counts, so this is what refills to 1 per restock.
    /// </summary>
    private void AddOffer(string traderId, string name, string[] assortIds, int loyaltyLevel, double priceRoubles)
    {
        if (!tradersTable.TryGetValue(new MongoId(traderId), out var trader) || trader.Assort == null)
        {
            logger.LogWarning("[ModWeapons] Trader {Name} not found, offer skipped", name);
            return;
        }
        if (!globalTable.ItemPresets.TryGetValue(new MongoId(Ids.ModSr25Preset), out var preset) || preset.Items.Count != assortIds.Length)
        {
            logger.LogWarning("[ModWeapons] Default preset missing or not {Count} parts, {Name} offer skipped", assortIds.Length, name);
            return;
        }
        var rootId = new MongoId(assortIds[0]);
        if (trader.Assort.Items.Any(i => i.Id == rootId)) return;

        var idByPresetItem = new Dictionary<MongoId, string>();
        for (var i = 0; i < preset.Items.Count; i++) idByPresetItem[preset.Items[i].Id] = assortIds[i];

        var offer = new List<Item>();
        for (var i = 0; i < preset.Items.Count; i++)
        {
            var src = preset.Items[i];
            var isRoot = i == 0;
            string? parentAssortId = null;
            if (!isRoot && (src.ParentId == null || !idByPresetItem.TryGetValue(new MongoId(src.ParentId), out parentAssortId)))
            {
                logger.LogWarning("[ModWeapons] Default preset is malformed, {Name} offer skipped", name);
                return;
            }
            offer.Add(new Item
            {
                Id = new MongoId(assortIds[i]),
                Template = src.Template,
                ParentId = isRoot ? "hideout" : parentAssortId,
                SlotId = isRoot ? "hideout" : src.SlotId,
                Upd = isRoot
                    ? new Upd { UnlimitedCount = true, StackObjectsCount = 999999, BuyRestrictionMax = 1, BuyRestrictionCurrent = 0, FireMode = src.Upd?.FireMode }
                    : null,
            });
        }

        trader.Assort.Items.AddRange(offer);
        trader.Assort.BarterScheme[rootId] = [[new BarterScheme { Count = priceRoubles, Template = Money.ROUBLES }]];
        trader.Assort.LoyalLevelItems[rootId] = loyaltyLevel;
        logger.LogInformation("[ModWeapons] {Name}: MOD-SR-25 at LL{Level} for {Price} RUB, 1 per restock", name, loyaltyLevel, priceRoubles);
    }
}
