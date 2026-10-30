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

namespace ItemTweaks4Server;

/// <summary>
/// Quests, bots (database and Progressive Bot System pools), slot filters and trader offers of every created clone. TypePriority sits after
/// NATO Quartermaster (TraderRegistration + 2), which builds its assort in its own OnLoad, and before TraderCallbacks,
/// RagfairCallbacks and PresetCallbacks so flea trader offers and presets pick everything up.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 10)]
public sealed class ItemIntegrations(
    ILogger<ItemIntegrations> logger,
    TemplateTable templateTable,
    GlobalTable globalTable,
    BotTable botTable,
    TradersTable tradersTable,
    HandbookHelper handbookHelper,
    IServiceProvider services) : IOnLoad
{
    // Looked up once: scanning every loaded assembly per clone is wasted work.
    private readonly Lazy<Type?> apbsLoaderType = new(() => AppDomain.CurrentDomain.GetAssemblies()
        .Select(a => a.GetType("ProgressiveBotSystem.Helpers.DataLoader"))
        .FirstOrDefault(t => t != null));

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var def in Clones.All)
        {
            if (!templateTable.Items.ContainsKey(new MongoId(def.CloneTpl)))
            {
                // Items step already logged why (missing optional source or a WTT error).
                logger.LogInformation("[ItemTweaks4Server] {Name} not created, quests, bots, filters and traders skipped", def.Name);
                continue;
            }

            if (def.IsVariant)
            {
                Guard($"{def.Name} quests", () => AddToQuests(def));
                Guard($"{def.Name} bots", () => AddToBots(def));
                Guard($"{def.Name} Progressive Bot System", () => AddToApbs(def));
                if (def.ReplicateFilters) Guard($"{def.Name} slot filters", () => AddToFilters(def));
            }
            if (def.AddToSpecialSlots) Guard($"{def.Name} special slots", () => AddToSpecialSlots(def));
            if (def.AddToSecureContainers) Guard($"{def.Name} secure containers", () => AddToSecureContainers(def));
            foreach (var offer in def.Offers)
                Guard($"{def.Name} {offer.TraderName} offer", () => AddOffer(def, offer));
        }
        return Task.CompletedTask;
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { logger.LogError("[ItemTweaks4Server] {What} failed: {Error}", what, e); }
    }

    // ---- Slot filters ----------------------------------------------------------------------------

    /// <summary>The clone fits wherever the source fits: every slot, chamber, cartridge and grid filter holding the source gets it too.</summary>
    private void AddToFilters(CloneDef def)
    {
        var source = new MongoId(def.SourceTpl);
        var (filters, items) = AddToSets(def, props =>
        {
            // Grid exclusions and conflicts too, so the clone is kept out wherever the source is.
            var sets = SlotFilterSets(AllSlots(props)).ToList();
            foreach (var grid in props.Grids ?? [])
                sets.AddRange((grid.Properties?.Filters ?? []).SelectMany(f => new[] { f.Filter, f.ExcludedFilter }).OfType<HashSet<MongoId>>());
            if (props.ConflictingItems != null) sets.Add(props.ConflictingItems);
            return sets;
        }, set => set.Contains(source));
        logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Filters} slot/cartridge/grid filter, exclusion or conflict list(s) of {Items} item(s)", def.Name, filters, items);
    }

    /// <summary>
    /// Special slots (pockets) list their allowed items one by one, so a new item has to be added to each slot named SpecialSlot*.
    /// Done here, after every mod has registered its items, so pockets added by other mods are covered too (WTT's own
    /// addtoSpecialSlots only knows two fixed pockets and adds to every slot of them, see README).
    /// </summary>
    private void AddToSpecialSlots(CloneDef def)
    {
        var (filters, items) = AddToSets(def,
            props => SlotFilterSets((props.Slots ?? []).Where(s => s.Name != null && s.Name.StartsWith("SpecialSlot", StringComparison.Ordinal))),
            _ => true);
        logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Filters} special slot filter(s) of {Items} item(s)", def.Name, filters, items);
    }

    /// <summary>Secured containers list their allowed items per grid filter; the ones that take the key tool (the key containers) also get the new case.</summary>
    private void AddToSecureContainers(CloneDef def)
    {
        var keyTool = new MongoId(Ids.KeyTool);
        var secured = new MongoId(Ids.SecuredContainerParent);
        var (filters, items) = AddToSets(def,
            props => (props.Grids ?? []).SelectMany(g => g.Properties?.Filters ?? []).Select(f => f.Filter).OfType<HashSet<MongoId>>(),
            set => set.Contains(keyTool),
            item => item.Parent == secured);
        if (filters == 0)
            logger.LogWarning("[ItemTweaks4Server] {Name} was not added to any secure container grid filter listing the key tool", def.Name);
        else
            logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Filters} grid filter(s) of {Items} secure container(s)", def.Name, filters, items);
    }

    private static IEnumerable<Slot> AllSlots(TemplateItemProperties props) =>
        (props.Slots ?? []).Concat(props.Chambers ?? []).Concat(props.Cartridges ?? []);

    private static IEnumerable<HashSet<MongoId>> SlotFilterSets(IEnumerable<Slot> slots) =>
        slots.SelectMany(s => s.Properties?.Filters ?? []).Select(f => f.Filter).OfType<HashSet<MongoId>>();

    /// <summary>Shared walk over every template: adds the clone to each set picked by <paramref name="pick"/> that passes <paramref name="match"/>, limited to templates passing <paramref name="template"/> when given.</summary>
    private (int Filters, int Items) AddToSets(CloneDef def, Func<TemplateItemProperties, IEnumerable<HashSet<MongoId>>> pick, Func<HashSet<MongoId>, bool> match,
        Func<TemplateItem, bool>? template = null)
    {
        var clone = new MongoId(def.CloneTpl);
        var filters = 0;
        var items = 0;
        foreach (var item in templateTable.Items.Values)
        {
            if (item.Properties == null || template?.Invoke(item) == false) continue;
            var before = filters;
            foreach (var set in pick(item.Properties))
            {
                if (match(set) && set.Add(clone)) filters++;
            }
            if (filters > before) items++;
        }
        return (filters, items);
    }

    // ---- Quests ----------------------------------------------------------------------------------

    private void AddToQuests(CloneDef def)
    {
        var source = def.SourceTpl;
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
                    if (target?.List is { } ids && ids.Contains(source) && !ids.Contains(def.CloneTpl))
                    {
                        ids.Add(def.CloneTpl);
                        added++;
                    }
                    else if (target?.Item == source)
                    {
                        condition.Target = new ListOrT<string>([source, def.CloneTpl], null);
                        added++;
                    }

                    // Kill conditions list allowed weapons in counter.conditions[].weapon.
                    foreach (var counter in condition.Counter?.Conditions ?? [])
                    {
                        if (counter.Weapon != null && counter.Weapon.Contains(source) && counter.Weapon.Add(def.CloneTpl))
                            added++;
                    }
                }
            }
            if (added > before) quests++;
        }
        logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Count} quest condition(s) across {Quests} quest(s)", def.Name, added, quests);
    }

    // ---- Bots ------------------------------------------------------------------------------------

    private void AddToBots(CloneDef def)
    {
        var pools = 0;
        var modEntries = 0;
        var modSets = 0;
        var bots = 0;
        foreach (var bot in botTable.Types.Values)
        {
            var inventory = bot?.BotInventory;
            if (inventory == null) continue;
            var before = pools + modEntries + modSets;

            var weightPools = new List<Dictionary<MongoId, double>?>(inventory.Equipment?.Values.AsEnumerable() ?? []);
            if (inventory.Items is { } items)
                weightPools.AddRange([items.Backpack, items.Pockets, items.SecuredContainer, items.SpecialLoot, items.TacticalVest]);
            foreach (var pool in weightPools)
            {
                if (SplitWeight(pool, def)) pools++;
            }

            // Mod pool: same attachments the source may spawn with, and the clone beside the source wherever it is a mod choice.
            if (CopyModPool(inventory.Mods, def)) modEntries++;
            modSets += AddToModSets(inventory.Mods, def);
            if (pools + modEntries + modSets > before) bots++;
        }
        logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Pools} bot weapon/loot pool(s), {Mods} mod pool(s) and {Sets} mod slot choice(s) across {Bots} bot type(s)",
            def.Name, pools, modEntries, modSets, bots);
    }

    /// <summary>Moves 25% of the source's weight to the clone; pool total stays unchanged. Weights are doubles, nothing rounds them.</summary>
    private static bool SplitWeight(Dictionary<MongoId, double>? pool, CloneDef def)
    {
        var source = new MongoId(def.SourceTpl);
        var clone = new MongoId(def.CloneTpl);
        if (pool == null || !pool.TryGetValue(source, out var weight) || pool.ContainsKey(clone)) return false;
        pool[source] = weight * 0.75;
        pool[clone] = weight * 0.25;
        return true;
    }

    private static bool CopyModPool(Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>? mods, CloneDef def)
    {
        var clone = new MongoId(def.CloneTpl);
        if (mods == null || !mods.TryGetValue(new MongoId(def.SourceTpl), out var sourceMods) || mods.ContainsKey(clone)) return false;
        mods[clone] = sourceMods.ToDictionary(kv => kv.Key, kv => new HashSet<MongoId>(kv.Value));
        return true;
    }

    /// <summary>
    /// Slot choices (weapon -> slot -> allowed items, e.g. magazines) are plain sets picked uniformly, with no weights to
    /// split: the clone simply joins every set holding the source and then competes on equal terms.
    /// </summary>
    private static int AddToModSets(Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>? mods, CloneDef def)
    {
        if (mods == null) return 0;
        var source = new MongoId(def.SourceTpl);
        var clone = new MongoId(def.CloneTpl);
        var added = 0;
        foreach (var slots in mods.Values)
        {
            foreach (var set in slots.Values)
            {
                if (set.Contains(source) && set.Add(clone)) added++;
            }
        }
        return added;
    }

    /// <summary>
    /// Acidphantasm Progressive Bot System (soft dependency) generates bot weapons from its own tiered pools and
    /// ignores the database bot types, so it gets the same 75/25 split, mod pool copy and mod set additions. Reached by reflection
    /// to avoid a hard reference. Applied to its clean data too, which it re-clones into the working data when
    /// its config or preset changes at runtime.
    /// </summary>
    private void AddToApbs(CloneDef def)
    {
        var loaderType = apbsLoaderType.Value;
        if (loaderType == null || services.GetService(loaderType) is not { } loader)
        {
            logger.LogInformation("[ItemTweaks4Server] Progressive Bot System not installed, skipping its bot pools for {Name}", def.Name);
            return;
        }

        var pools = 0;
        var modEntries = 0;
        var modSets = 0;
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
                        if (SplitWeight(pool as Dictionary<MongoId, double>, def)) pools++;
                    }
                }
                var mods = tier.GetType().GetProperty("ModsData")?.GetValue(tier) as Dictionary<MongoId, Dictionary<string, HashSet<MongoId>>>;
                if (CopyModPool(mods, def)) modEntries++;
                modSets += AddToModSets(mods, def);
            }
        }
        logger.LogInformation("[ItemTweaks4Server] Added {Name} to {Pools} Progressive Bot System weapon pool(s), {Mods} mod pool(s) and {Sets} mod slot choice(s)",
            def.Name, pools, modEntries, modSets);
    }

    // ---- Traders ---------------------------------------------------------------------------------

    /// <summary>Price in roubles: fixed, else 2x the source's offer at the offer's price trader, else the fallback (see <see cref="OfferDef"/>).</summary>
    private double ResolvePrice(CloneDef def, OfferDef offer)
    {
        if (offer.FixedPrice is { } fixedPrice) return fixedPrice;
        if (offer.CurrencyTpl != Ids.Roubles) throw new InvalidOperationException($"{def.Name}: a non-rouble offer needs a FixedPrice");

        if (offer.OwnPriceTrader != null && TryReadOfferRoubles(def, offer.OwnPriceTrader) * 2 is > 0 and var own)
            return Math.Round(own);

        var factor = 1d;
        if (offer.FallbackPriceTrader != null)
        {
            factor = offer.FallbackFactor;
            if (TryReadOfferRoubles(def, offer.FallbackPriceTrader) * 2 is > 0 and var other)
            {
                var price = Math.Round(other * factor);
                logger.LogInformation("[ItemTweaks4Server] {Trader} has no {Source} offer, its offer uses 2x the {Other} price times {Factor}: {Price} RUB",
                    offer.TraderName, def.SourceName, Ids.TraderNames[offer.FallbackPriceTrader], factor, price);
                return price;
            }
        }

        // Fallback: 2x source handbook price (the same as the clone's own handbook price for 2x clones).
        var fallback = Math.Round(2 * handbookHelper.GetTemplatePrice(new MongoId(def.SourceTpl)) * factor);
        logger.LogWarning("[ItemTweaks4Server] {Source} price at {PriceTrader} not found, {Trader} offer for {Name} uses fallback {Price} RUB",
            def.SourceName, Ids.TraderNames[offer.FallbackPriceTrader ?? offer.OwnPriceTrader ?? offer.TraderId], offer.TraderName, def.Name, fallback);
        return fallback;
    }

    /// <summary>Price of the trader's source root offer in roubles, or null when absent or not paid in money.</summary>
    private double? TryReadOfferRoubles(CloneDef def, string traderId)
    {
        var source = new MongoId(def.SourceTpl);
        if (!tradersTable.TryGetValue(new MongoId(traderId), out var trader) || trader.Assort == null) return null;
        var root = trader.Assort.Items.FirstOrDefault(i => i.Template == source && i.SlotId == "hideout");
        if (root == null || !trader.Assort.BarterScheme.TryGetValue(root.Id, out var options)) return null;
        // The first option is one payment: every entry of it must be money, summed in roubles.
        var payment = options.FirstOrDefault();
        if (payment == null || payment.Count == 0) return null;

        var total = 0d;
        foreach (var entry in payment)
        {
            if (entry.Count is not { } count || !Money.GetMoneyTpls().Contains(entry.Template))
            {
                logger.LogWarning("[ItemTweaks4Server] {Trader} sells {Source} for a non-currency barter, cannot derive a price", Ids.TraderNames[traderId], def.SourceName);
                return null;
            }
            total += entry.Template == Money.ROUBLES ? count : handbookHelper.InRoubles(count, entry.Template);
        }
        return total;
    }

    /// <summary>
    /// Adds the full default build (or the single item) as one offer. The root is unlimited-stock with BuyRestrictionMax = limit:
    /// SPT 4.1.6 never restores a decremented StackObjectsCount on restock (the "pristine" assort is the
    /// live one) but resets each profile's purchase counts, so this is what refills to the limit per restock.
    /// </summary>
    private void AddOffer(CloneDef def, OfferDef offer)
    {
        if (!tradersTable.TryGetValue(new MongoId(offer.TraderId), out var trader) || trader.Assort == null)
        {
            if (offer.Optional)
                logger.LogInformation("[ItemTweaks4Server] {Trader} not installed, skipping its {Name} offer", offer.TraderName, def.Name);
            else
                logger.LogWarning("[ItemTweaks4Server] Trader {Trader} not found, {Name} offer skipped", offer.TraderName, def.Name);
            return;
        }

        List<Item> parts;
        if (def.PresetId == null)
            parts = [new Item { Id = new MongoId(offer.AssortIds[0]), Template = def.CloneTpl }];
        else if (globalTable.ItemPresets.TryGetValue(new MongoId(def.PresetId), out var preset) && preset.Items.Count == offer.AssortIds.Length)
            parts = preset.Items;
        else
        {
            logger.LogWarning("[ItemTweaks4Server] Default preset of {Name} missing or not {Count} parts, {Trader} offer skipped", def.Name, offer.AssortIds.Length, offer.TraderName);
            return;
        }

        var assortIds = offer.AssortIds;
        var rootId = new MongoId(assortIds[0]);
        if (trader.Assort.Items.Any(i => i.Id == rootId)) return;

        var idByPresetItem = new Dictionary<MongoId, string>();
        for (var i = 0; i < parts.Count; i++) idByPresetItem[parts[i].Id] = assortIds[i];

        var items = new List<Item>();
        for (var i = 0; i < parts.Count; i++)
        {
            var src = parts[i];
            var isRoot = i == 0;
            string? parentAssortId = null;
            if (!isRoot && (src.ParentId == null || !idByPresetItem.TryGetValue(new MongoId(src.ParentId), out parentAssortId)))
            {
                logger.LogWarning("[ItemTweaks4Server] Default preset of {Name} is malformed, {Trader} offer skipped", def.Name, offer.TraderName);
                return;
            }
            items.Add(new Item
            {
                Id = new MongoId(assortIds[i]),
                Template = src.Template,
                ParentId = isRoot ? "hideout" : parentAssortId,
                SlotId = isRoot ? "hideout" : src.SlotId,
                Upd = isRoot
                    ? new Upd { UnlimitedCount = true, StackObjectsCount = 999999, BuyRestrictionMax = offer.Limit, BuyRestrictionCurrent = 0, FireMode = src.Upd?.FireMode }
                    : null,
            });
        }

        var price = ResolvePrice(def, offer);
        trader.Assort.Items.AddRange(items);
        trader.Assort.BarterScheme[rootId] = [[new BarterScheme { Count = price, Template = offer.CurrencyTpl }]];
        trader.Assort.LoyalLevelItems[rootId] = offer.LoyaltyLevel;
        logger.LogInformation("[ItemTweaks4Server] {Trader}: {Name} at LL{Level} for {Price} {Currency}, {Limit} per restock", offer.TraderName, def.Name, offer.LoyaltyLevel, price, offer.CurrencyName, offer.Limit);
    }
}
