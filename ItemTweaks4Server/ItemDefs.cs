namespace ItemTweaks4Server;

/// <summary>One trader offer of a clone. Prices are roubles unless Currency says otherwise; the offer is 1 root item (the full default build when the clone has a preset).</summary>
/// <param name="AssortIds">Trader assort ids, in preset item order (root first); one id for a clone without preset.</param>
/// <param name="Limit">Purchases per profile per restock (BuyRestrictionMax).</param>
/// <param name="FixedPrice">Price as is, in Currency. Otherwise 2x the price of the source item at OwnPriceTrader.</param>
/// <param name="FallbackPriceTrader">Used when OwnPriceTrader has no source offer: 2x that trader's source price times FallbackFactor.</param>
/// <param name="Optional">The trader is a soft dependency: skipped with a log line when absent.</param>
internal sealed record OfferDef(
    string TraderId,
    int LoyaltyLevel,
    string[] AssortIds,
    int Limit,
    double? FixedPrice = null,
    string? OwnPriceTrader = null,
    string? FallbackPriceTrader = null,
    double FallbackFactor = 1,
    bool Optional = false,
    string? Currency = null)
{
    /// <summary>Payment currency tpl, roubles when null. Only FixedPrice supports another currency.</summary>
    public string CurrencyTpl => Currency ?? Ids.Roubles;
    public string CurrencyName => CurrencyTpl == Ids.Usd ? "USD" : CurrencyTpl == Ids.Eur ? "EUR" : "RUB";
    public string TraderName => Ids.TraderNames[TraderId];
}

/// <summary>A clone of an existing item. Ids must match the item JSON in ConfigDir.</summary>
/// <param name="ConfigDir">Directory (relative to the mod folder) with the WTT item JSON. WTT loads a directory recursively and one bad item must not
/// block the others, so an optional source gets a directory of its own, loaded only when the source exists.</param>
/// <param name="SourceMod">Mod that adds the source item, null for vanilla. Its absence skips the clone.</param>
/// <param name="PresetId">Default preset of a weapon clone, null for an item sold on its own.</param>
/// <param name="ShareMastery">Add the clone to every weapon mastery entry holding the source.</param>
/// <param name="ReplicateFilters">Add the clone to every slot, cartridge and grid filter holding the source.</param>
/// <param name="IsVariant">A stronger variant of the source: joins its quests, bot pools and filters (when ReplicateFilters). False for an unrelated new item
/// that merely reuses the source's model.</param>
/// <param name="AddToSpecialSlots">Add the clone to the filter of every slot named SpecialSlot*.</param>
internal sealed record CloneDef(
    string Name,
    string SourceName,
    string SourceTpl,
    string CloneTpl,
    string ConfigDir,
    string? SourceMod,
    string? PresetId,
    bool ShareMastery,
    bool ReplicateFilters,
    OfferDef[] Offers,
    bool IsVariant = true,
    bool AddToSpecialSlots = false);

/// <summary>Fixed ids and the clone list. Item and preset ids must match the JSON under db/.</summary>
internal static class Ids
{
    public const string Sr25 = "5df8ce05b11454561e39243b";
    public const string ModSr25 = "6adb4485f8053e5cbd898d23";
    public const string ModSr25Preset = "6af31c1282f53f3fe42b025a";

    public const string Roubles = "5449016a4bdc2d6f028b456f";
    public const string Usd = "5696686a4bdc2da3298b456a";
    public const string Eur = "569668774bdc2da2298b4568";

    public const string Peacekeeper = "5935c25fb3acc3127c3d8cd9";
    public const string Jaeger = "5c0647fdd443bc2504c2d371";
    public const string Mechanic = "5a7c2eca46aef81a7ca2145d";
    public const string NatoQuartermaster = "bb371fed0e82b224f2d82c0d";

    public static readonly IReadOnlyDictionary<string, string> TraderNames = new Dictionary<string, string>
    {
        [Peacekeeper] = "Peacekeeper",
        [Jaeger] = "Jaeger",
        [Mechanic] = "Mechanic",
        [NatoQuartermaster] = "NATO Quartermaster",
    };

    public const string Aek973 = "686093d590c3dce07984c38a";
    public const string ModAek973 = "6ab089a2446b86a2c9cc48be";
    public const string ModAek973Preset = "6ad47e1cea3cb9cfe08ba9d3";

    public const string Aek971 = "0cb4a36dd2e587b46e813dbe";
    public const string ModAek971 = "6aa84f678601f2f04729f7c7";
    public const string ModAek971Preset = "6a856eb8fd13a0324b3486f6";

    public const string X47 = "5cfe8010d7ad1a59283b14c6";
    public const string ModX47 = "6ab1ba9faeb1d2b546b88b50";

    public const string DogtagCase = "5c093e3486f77430cb02e593";
    public const string SpecOpsCase = "f39e1020a6eb538f07653b87";
}

internal static class Clones
{
    private const string Armory = "WTT-Armory";

    // Trader assort item ids for the default builds, in preset item order (root first).
    private static readonly string[] JaegerAssort =
    [
        "6a6a63318824fe3e1798218c", "6af0bd0e03a6ecb97097ba83", "6aa93c9f9ffabe038a40c939", "6a23b9ed07d55be269ac1e5e",
        "6aa5ef25e408a86af979bba5", "6abb4e80fc2e013abc861288", "6a630b15d63da6f7bca4c06f", "6abb26482071d095b051d091",
        "6a2046c554b451bccd09bab8", "6ae9c38902e7544d0cee9be6", "6ac996a8a903981cb7651a7c", "6ab18bf74fe2391f44ee5e97",
        "6a2c701411929abfda42550c",
    ];

    private static readonly string[] NatoAssort =
    [
        "6ab01c4539e64fe13189ff75", "6a8cf31aacfd26a834f6000a", "6abb1bf47829e60b42122a3e", "6aac3cd6f2c45c706511487b",
        "6a6c1a9de78c7c5a4b954b5c", "6a49963d9154349edc3ffaa7", "6a6016ce020bb79712f27c67", "6ad2f67389db63116e1013bb",
        "6a3a005b52550a5fbf1ee22e", "6abc8766192681194300f289", "6a958629996fd37192c700b5", "6a50644d50001bbc286fa66b",
        "6a44af7b77f3112f6e3ab712",
    ];

    private static readonly string[] Aek973Mechanic = [
        "6a9e943ad8331eaef77ba86e", "6aac4fba7fe4797a95aa7a39", "6a0117dd04ea4e43119b2695", "6ade18e4c4a4a19fe2f9d070",
        "6a959400029e135f658abdc3", "6af40592f45c3960c8ef5902", "6ab0b658c1067c98e16a7efb",
    ];

    private static readonly string[] Aek973Nato = [
        "6a4f889d03c642f9331f1477", "6a5a7a2428e8526a8d0acd5d", "6ad664183d35ae240a0d7013", "6a1b66f181d1f65060074043",
        "6a93e458fbd23fa0f38c7aac", "6a98dd8e15f793c429fbd6bb", "6a4d3c105f2e9d4c64563e78",
    ];

    private static readonly string[] Aek971Mechanic = [
        "6a7cf7aba3d7613e3331e809", "6a4fe1e636fe5c51cd5c3e89", "6a0900c6c90107516e83416b", "6afed64df21ef9cf7e0dad02",
        "6afd2e7e5a5b8d74ed79a6a0", "6a059c8a4aff0b364ead6c73", "6ac7b5db93ba99b6d7ec1c18",
    ];

    private static readonly string[] Aek971Nato = [
        "6ad4ebe4a00e5ce46266c48d", "6aec7660229d307131579e16", "6a38861f63298d2a1dca6336", "6acf3579df0e71151613cfdb",
        "6abfa3e7ff976d9f45e62280", "6aa9f64d760dad375ebf8d4e", "6aa846270c912ebeb9594c96",
    ];

    public static readonly CloneDef[] All =
    [
        new("MOD-SR-25", "SR-25", Ids.Sr25, Ids.ModSr25, "db/CustomItems", null, Ids.ModSr25Preset, ShareMastery: true, ReplicateFilters: false,
        [
            // Jaeger LL4: 2x Peacekeeper's SR-25 price. NATO: 2x its own SR-25 price.
            new(Ids.Jaeger, 4, JaegerAssort, 1, OwnPriceTrader: Ids.Peacekeeper),
            new(Ids.NatoQuartermaster, 1, NatoAssort, 1, OwnPriceTrader: Ids.NatoQuartermaster, Optional: true),
        ]),
        new("MOD-AEK-973", "AEK-973", Ids.Aek973, Ids.ModAek973, "db/Aek973", Armory, Ids.ModAek973Preset, ShareMastery: true, ReplicateFilters: false,
        [
            // 2x the source flea price (210000).
            new(Ids.Mechanic, 4, Aek973Mechanic, 1, FixedPrice: 420000),
            new(Ids.NatoQuartermaster, 1, Aek973Nato, 1, FixedPrice: 420000, Optional: true),
        ]),
        new("MOD-AEK-971", "AEK-971", Ids.Aek971, Ids.ModAek971, "db/Aek971", Armory, Ids.ModAek971Preset, ShareMastery: true, ReplicateFilters: false,
        [
            // 2x the source flea price (189000).
            new(Ids.Mechanic, 4, Aek971Mechanic, 1, FixedPrice: 378000),
            new(Ids.NatoQuartermaster, 1, Aek971Nato, 1, FixedPrice: 378000, Optional: true),
        ]),
        new("MOD-X-47", "X-47", Ids.X47, Ids.ModX47, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            // Mechanic LL4: 2x its own X-47 price. NATO: 2x its own offer, else 2x Mechanic's price plus 25%.
            new(Ids.Mechanic, 4, ["6a759e292d6cc46c49c03b36"], 6, OwnPriceTrader: Ids.Mechanic),
            new(Ids.NatoQuartermaster, 1, ["6a5f7d23e728fd472d2171be"], 6, OwnPriceTrader: Ids.NatoQuartermaster,
                FallbackPriceTrader: Ids.Mechanic, FallbackFactor: 1.25, Optional: true),
        ]),
        // New item reusing the dogtag case model: no quests, bots, filters or mastery of the source. Placed in special slots.
        new("Spec-ops kit case", "Dogtag case", Ids.DogtagCase, Ids.SpecOpsCase, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: false,
        [
            new(Ids.NatoQuartermaster, 1, ["2ca535e7b954eb436c4d78a6"], 1, FixedPrice: 69000, Optional: true, Currency: Ids.Usd),
        ], IsVariant: false, AddToSpecialSlots: true),
    ];
}
