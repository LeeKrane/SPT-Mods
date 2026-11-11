namespace ItemTweaks4Server;

/// <summary>One trader offer of a clone. Prices are roubles unless Currency says otherwise; the offer is 1 root item (the full default build when the clone has a preset).</summary>
/// <param name="AssortIds">Trader assort ids, in preset item order (root first); one id for a clone without preset.</param>
/// <param name="Limit">Purchases per profile per restock (BuyRestrictionMax); 0 for no limit.</param>
/// <param name="FixedPrice">Price as is, in Currency. Otherwise 2x the price of the source item at OwnPriceTrader.</param>
/// <param name="FallbackPriceTrader">Used when OwnPriceTrader has no source offer: 2x that trader's source price times FallbackFactor.</param>
/// <param name="Optional">The trader is a soft dependency: skipped with a log line when absent.</param>
/// <param name="Barter">Paid with Count items of Tpl instead of money; no price is resolved.</param>
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
    string? Currency = null,
    (string Tpl, int Count)? Barter = null)
{
    /// <summary>Payment currency tpl, roubles when null. Only FixedPrice supports another currency.</summary>
    public string CurrencyTpl => Currency ?? Ids.Roubles;
    public string CurrencyName => CurrencyTpl == Ids.Usd ? "USD" : CurrencyTpl == Ids.Eur ? "EUR" : "RUB";
    public string TraderName => Ids.TraderNames[TraderId];
}

/// <summary>A clone of an existing item. Ids must match the item JSON in ConfigDir.</summary>
/// <param name="ConfigDir">Directory (relative to the mod folder) with the WTT item JSON. WTT loads a directory recursively and one bad item must not
/// block the others, so an optional source gets a directory of its own, loaded only when the source exists.</param>
/// <param name="SourceMod">Mod that adds the source item (or that RequiredType belongs to), null for vanilla. Its absence skips the clone.</param>
/// <param name="PresetId">Default preset of a weapon clone, null for an item sold on its own.</param>
/// <param name="ShareMastery">Add the clone to every weapon mastery entry holding the source.</param>
/// <param name="ReplicateFilters">Add the clone to every slot, cartridge and grid filter (and exclusion / conflict list) holding the source, so it fits
/// wherever the source fits. Independent of IsVariant.</param>
/// <param name="IsVariant">A stronger variant of the source: joins its quests and bot pools. False for an unrelated new item that merely reuses the
/// source's model, or for an item that only shares the source's filters (ReplicateFilters).</param>
/// <param name="AddToSpecialSlots">Add the clone to the filter of every slot named SpecialSlot*.</param>
/// <param name="AddToSecureContainers">Add the clone to the grid filter of every secured container whose filter lists the key tool.</param>
/// <param name="RequiredType">Full name of a type of SourceMod. The clone needs that mod (e.g. its bundles) although the source is vanilla: skipped
/// when no loaded assembly has the type.</param>
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
    bool AddToSpecialSlots = false,
    bool AddToSecureContainers = false,
    string? RequiredType = null);

/// <summary>Extra trader offers of an existing item (not a clone). SourceMod adds the item; its absence skips it.</summary>
/// <param name="Flea">Put the item on the flea (dynamic offers by simulated players) although its mod made it unsellable or blacklisted it.</param>
/// <param name="FleaPrice">Flea price in roubles replacing the mod's own (only with Flea); null keeps it.</param>
internal sealed record ItemOfferDef(string Name, string ItemTpl, string? SourceMod, OfferDef[] Offers, bool Flea = false, double? FleaPrice = null)
{
    /// <summary>The item as a "clone" of itself, so the clone offer code can add it. Only FixedPrice or Barter offers: there is no source to price from.</summary>
    public CloneDef AsCloneDef => new(Name, Name, ItemTpl, ItemTpl, "", SourceMod, null, ShareMastery: false, ReplicateFilters: false, Offers, IsVariant: false);
}

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
    public const string Ref = "6617beeaa9cfa777ca915b7c";
    public const string LegaMedal = "6656560053eaaa7a23349c86";
    public const string Therapist = "54cb57776803fa99248b456e";

    public static readonly IReadOnlyDictionary<string, string> TraderNames = new Dictionary<string, string>
    {
        [Peacekeeper] = "Peacekeeper",
        [Prapor] = "Prapor",
        [Jaeger] = "Jaeger",
        [Mechanic] = "Mechanic",
        [Therapist] = "Therapist",
        [NatoQuartermaster] = "NATO Quartermaster",
        [Ref] = "Ref",
    };

    public const string Aek973 = "686093d590c3dce07984c38a";
    public const string ModAek973 = "6ab089a2446b86a2c9cc48be";
    public const string ModAek973Preset = "6ad47e1cea3cb9cfe08ba9d3";
    public const string Msr = "684e32eaec9f5eb3cacc7ca7";
    public const string MsrX = "99ce2a2593ed0ae82737cec7";
    public const string MsrXPreset = "954c85dbf6349292db05abea";
    public const string Sabre5 = "684e49af525c602815316f8d";
    public const string Sabre5_X = "b62a92a241bba6be2d500f80";
    public const string Fmj300Wm = "684e2ded3b64f7187cf14fae";
    public const string Cbj300Wm = "5efdb8528bd5573c2e4faf1d";

    public const string Aek971 = "0cb4a36dd2e587b46e813dbe";
    public const string ModAek971 = "6aa84f678601f2f04729f7c7";
    public const string ModAek971Preset = "6a856eb8fd13a0324b3486f6";

    public const string X47 = "5cfe8010d7ad1a59283b14c6";
    public const string ModX47 = "6ab1ba9faeb1d2b546b88b50";

    public const string DogtagCase = "5c093e3486f77430cb02e593";
    public const string SpecOpsCase = "f39e1020a6eb538f07653b87";

    public const string GingyKeychain = "62a09d3bcf4a99369e262447";
    public const string KeyCase = "c40d4da0b8d5434d00fffb4c";
    public const string KeyTool = "59fafd4b86f7745ca07e1232";
    public const string SecuredContainerParent = "5448bf274bdc2dfc2f8b456a";

    public const string Prapor = "54cb50c76803fa8b248b4571";
    public const string Ps320 = "5c0517910db83400232ffee5";
    public const string Ps321 = "41a2f314a21e64a4862d6bd1";
    public const string TarkovskayaVodka = "5d40407c86f774318526545a";

    public const string Propital = "5c0e530286f7747fa1419862";
    public const string PropitalX = "3d8e6f5ae7ea46f17f0fa1a5";
    public const string Adrenaline = "5c10c8fd86f7743d7d706df3";
    public const string AdrenalineX = "746f40f23c943aa94650ab0b";
    public const string Zagustin = "5c0e533786f7747fa23f4d47";
    public const string ZagustinX = "3f5582d3b2ae5f2af6083599";
    public const string Surv12 = "5d02797c86f774203f38e30a";
    public const string Nightfall = "6190dcad54cbff2ac9b7b8f4";
    public const string Sj6 = "5c0e531d86f7747fa23f4d42";
    public const string Sj6X = "99bdf12eeb8cff157a1ad9cf";
    public const string Mule = "5ed51652f6c34d2cc26336a1";
    public const string Keta = "4f3445dcc79d0c4d911c6144";

    public const string ModSpearUpperBlack = "6892180ee9f15149b0677260";
    public const string Xm1184Ap = "699b54a97d3c1c9f69f616b6";
    public const string ModifiedLbt1961a = "693873a5b94c659818e18a1c";

    // WTT-Artem items it makes unsellable (explicitly, or inherited from an unsellable source).
    public const string ArtemRaidBackpackBlack = "6673b1ac5cae0610f1079d71";
    public const string ArtemRaidBackpackAlpine = "668bc5cd834c88e06b08b928";
    public const string ArtemBumpHelmetBlack = "66326bfd46817c660d01512d";
    public const string ArtemGpnvgWhite = "66326bfd46817c660d01512e";
    public const string ArtemGpnvgOd = "66326bfd46817c660d015148";
    public const string ArtemGpnvgBlack = "66326bfd46817c660d015146";
    public const string ArtemRoninAlpine = "668bc5cd834c88e06b08b6a9";
    public const string ArtemRoninBlack = "668bc5cd834c88e06b08b6aa";
    public const string ArtemItemCase = "66326bfd46817c660d015123";
    public const string ArtemCpcMulticam1 = "6673b1ac5cae0610f1079d7a";
    public const string ArtemCpcAlpine = "6673b1ac5cae0610f1079d7b";
    public const string ArtemCpcMulticam2 = "6673b1ac5cae0610f1079d7c";
    public const string ArtemAirliteSpc = "66bf757f27d0b097db0ace36";
    public const string ArtemLv119 = "66bf757f27d0b097db0ace38";
    // More Cases Updated items that inherit an unsellable source (THICC weapon case, junk box).
    public const string MoreCasesWeaponRack = "a3c2c001eb04d2cc82bd8983";
    public const string MoreCasesToolbox = "9356c871d08f21db38e4c6ec";

    public const string ExfilWhiteTusk = "a321239209cb583baea69ca1";
    public const string ExfilBdBlackCamo = "bf1a3157a6706c4a2e0d7c9e";
    public const string ExfilBdBlackCamoPreset = "1965bf04400dbefd19d2786d";

    public const string Sicc = "5d235bb686f77443f4331278";
    public const string RedlineDossier = "34c6e81189d757ca98a05adb";

    public const string Slx68 = "652911e650dc782999054b9d";
    public const string SpecialSlx68 = "6984367b0aa5465355c4ded3";

    public const string KappaDesecrated = "676008db84e242067d0dc4c9";
    public const string SecureContainerOmega = "22776b1c216596a5e40a8e2c";

    public const string MedCase = "5aafbcd986f7745e590fff23";
    public const string MediPouch = "8fd4a50de9a76655273833d8";
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
        // Flea only, at 2x the source flea price.
        new("MSR-X", "MSR", Ids.Msr, Ids.MsrX, "db/MsrX", Armory, Ids.MsrXPreset, ShareMastery: true, ReplicateFilters: false, []),
        new("Sabre-5-X", "Sabre-5", Ids.Sabre5, Ids.Sabre5_X, "db/MsrX", Armory, null, ShareMastery: false, ReplicateFilters: true, []),
        // Not a variant: no quests or bot pools of the FMJ, but fits every magazine and chamber that takes it.
        new(".300 WM CBJ", ".300 WM FMJ", Ids.Fmj300Wm, Ids.Cbj300Wm, "db/MsrX", Armory, null, ShareMastery: false, ReplicateFilters: true, [], IsVariant: false),
        new("MOD-X-47", "X-47", Ids.X47, Ids.ModX47, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            // Mechanic LL4: 2x its own X-47 price. NATO: 2x its own offer, else 2x Mechanic's price plus 25%.
            new(Ids.Mechanic, 4, ["6a759e292d6cc46c49c03b36"], 6, OwnPriceTrader: Ids.Mechanic),
            new(Ids.NatoQuartermaster, 1, ["6a5f7d23e728fd472d2171be"], 6, OwnPriceTrader: Ids.NatoQuartermaster,
                FallbackPriceTrader: Ids.Mechanic, FallbackFactor: 1.25, Optional: true),
        ]),
        new("PS-321", "PS-320", Ids.Ps320, Ids.Ps321, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            // Prapor LL4: the PS-320 barter (2 vodka) doubled. NATO: 2x the source flea price (42742).
            new(Ids.Prapor, 4, ["1c46de07e12aa6b66b127889"], 1, Barter: (Ids.TarkovskayaVodka, 4)),
            new(Ids.NatoQuartermaster, 1, ["a37b5e4f4daa2c1460d6361e"], 3, FixedPrice: 85484, Optional: true),
        ]),
        // New item reusing the dogtag case model: no quests, bots, filters or mastery of the source. Placed in special slots.
        new("Spec-ops kit case", "Dogtag case", Ids.DogtagCase, Ids.SpecOpsCase, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: false,
        [
            new(Ids.NatoQuartermaster, 1, ["2ca535e7b954eb436c4d78a6"], 1, FixedPrice: 69000, Optional: true, Currency: Ids.Usd),
            // Ref LL1: 10% of NATO Quartermaster's price (6900 USD, ~835k RUB), as 1 Lega medal (900k RUB handbook).
            new(Ids.Ref, 1, ["c7f6db482042ca55b44d83bd"], 1, Barter: (Ids.LegaMedal, 1)),
        ], IsVariant: false, AddToSpecialSlots: true),
        // New item reusing the Gingy keychain model. Placed in special slots and in every secure container that takes the key tool.
        new("Spec-ops key case", "Gingy keychain", Ids.GingyKeychain, Ids.KeyCase, "db/KeyCase", null, null, ShareMastery: false, ReplicateFilters: false,
        [
            new(Ids.NatoQuartermaster, 1, ["1dc853e420eb2b1fbe1d0e5d"], 1, FixedPrice: 69000, Optional: true, Currency: Ids.Usd),
            new(Ids.Ref, 1, ["48385b39a0679eb0ac0136f1"], 1, Barter: (Ids.LegaMedal, 1)),
        ], IsVariant: false, AddToSpecialSlots: true, AddToSecureContainers: true),
        // Improved meds: fit wherever the source fits, but join none of its quests or bot pools. Therapist LL4 at 2x the source flea price.
        new("Propital-X", "Propital", Ids.Propital, Ids.PropitalX, "db/Meds", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 4, ["bf51445db5f2de0327519663"], 3, FixedPrice: 79456),
        ], IsVariant: false),
        new("Adrenaline-X", "Adrenaline", Ids.Adrenaline, Ids.AdrenalineX, "db/Meds", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 4, ["3b2b0117138b12bb04d0c6ac"], 3, FixedPrice: 39812),
        ], IsVariant: false),
        new("Zagustin-X", "Zagustin", Ids.Zagustin, Ids.ZagustinX, "db/Meds", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 4, ["25bf892de7b0aa2d41571fe3"], 3, FixedPrice: 92504),
        ], IsVariant: false),
        new("Nightfall", "Surv12", Ids.Surv12, Ids.Nightfall, "db/Meds", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 4, ["5354074733ed474a48c13c8a"], 2, FixedPrice: 132140),
        ], IsVariant: false),
        new("SJ6-X", "SJ6", Ids.Sj6, Ids.Sj6X, "db/Meds", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 4, ["608769b2b3816eac3739ba28"], 3, FixedPrice: 208484),
        ], IsVariant: false),
        // New anomalous stim with the ISB-Aishi KTE stim model (its bundle): a M.U.L.E. clone, created only with ISB-Aishi. 2x the SJ6 + M.U.L.E. flea prices, minus 35%.
        new("K.E.T.A.", "M.U.L.E.", Ids.Mule, Ids.Keta, "db/Keta", "ISB-Aishi", null, ShareMastery: false, ReplicateFilters: false,
        [
            new(Ids.Therapist, 4, ["8ddee9eda2fec1723cdb5fd0"], 1, FixedPrice: 375982),
        ], IsVariant: false, RequiredType: "Aishi.ModMetadata"),
        // New case reusing the S I C C model (darkened by the client plugin): a 5x6 grid taking everything the SICC and the documents case take.
        // Fits wherever the SICC fits, joins none of its quests or bot pools. Flea only (2x the SICC flea price), no trader.
        new("Redline dossier", "S I C C organizational pouch", Ids.Sicc, Ids.RedlineDossier, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
            [], IsVariant: false),
        // ISB-Aishi's White Tusk Arctic EXFIL helmet (modified class 5 inserts, its own slot filters, e.g. the heavy trooper masks) with the Black Camo
        // model. Aishi creates its items at Preload + 3, before this. Fits wherever the White Tusk helmet fits. Flea only (the White Tusk price).
        new("EXFIL helmet (BD Black Camo)", "Team Wendy EXFIL Ballistic Helmet (White Tusk Arctic)", Ids.ExfilWhiteTusk, Ids.ExfilBdBlackCamo, "db/ExfilHelmet", "ISB-Aishi",
            Ids.ExfilBdBlackCamoPreset, ShareMastery: false, ReplicateFilters: true, [], IsVariant: false, RequiredType: "Aishi.ModMetadata"),
        // Secure container: Desecrated Kappa look, 1 kg, Theta's grids (4x2, 1x2) plus a 5x1 and Theta's item whitelists. Fits wherever the Desecrated Kappa fits.
        // Only obtainable as a prestige 4+ reward (PrestigeRewards): no trader, flea, loot or bots.
        new("Secure container Omega", "Secure container Kappa (Desecrated)", Ids.KappaDesecrated, Ids.SecureContainerOmega, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
            [], IsVariant: false),
        // Documents bag look, 2x1, one 3x3 grid with the Med case's filters. Fits wherever the Med case fits. Flea (dynamic offers) and Therapist LL2 at the handbook price.
        new("MediPouch", "Med case", Ids.MedCase, Ids.MediPouch, "db/CustomItems", null, null, ShareMastery: false, ReplicateFilters: true,
        [
            new(Ids.Therapist, 2, ["317dfd4174fc69d964f3ff36"], 1, FixedPrice: 67250),
        ], IsVariant: false),
    ];
}

internal static class ItemOffers
{
    public static readonly ItemOfferDef[] All =
    [
        // Ref LL1 sells money like Skier (EUR) and Peacekeeper (USD), at their LL1 prices, without a buy limit.
        new("Euros", Ids.Eur, null, [new(Ids.Ref, 1, ["7b16ddb3b9dbc2e1998aedb3"], 0, FixedPrice: 158)]),
        new("Dollars", Ids.Usd, null, [new(Ids.Ref, 1, ["781139ef0c83861c64b46655"], 0, FixedPrice: 136)]),
        // Same price as Aishi's own offer (LL4, 51 USD, 1 per restock), 3 per restock.
        new("Modified MCX-SPEAR 6.8x51 upper receiver (Black)", Ids.ModSpearUpperBlack, "ISB-Aishi",
        [
            new(Ids.NatoQuartermaster, 1, ["0dc14be2b961e488099a0b60"], 3, FixedPrice: 51, Optional: true, Currency: Ids.Usd),
        ]),
        // ISB-Aishi marks it unsellable and puts it on the item blacklist (WTT addtoItemBlacklist). Flea only, no trader.
        new("6.8x51mm SIG XM1184 AP", Ids.Xm1184Ap, "ISB-Aishi", [], Flea: true),
        // Same for the first Modified LBT-1961A rig (not V2). Flea only, at ISB-Aishi's flea price.
        new("Modified LBT-1961A Load Bearing Chest Rig", Ids.ModifiedLbt1961a, "ISB-Aishi", [], Flea: true),

        // WTT-Artem marks these unsellable. Flea only, at Artem's flea prices.
        new("6Sh118 raid backpack (Black)", Ids.ArtemRaidBackpackBlack, "WTT-Artem", [], Flea: true),
        new("6Sh118 raid backpack (Alpine)", Ids.ArtemRaidBackpackAlpine, "WTT-Artem", [], Flea: true),
        new("Tactical Bump Helmet (Black)", Ids.ArtemBumpHelmetBlack, "WTT-Artem", [], Flea: true),
        new("GPNVG-18 Night Vision goggles (White)", Ids.ArtemGpnvgWhite, "WTT-Artem", [], Flea: true),
        new("GPNVG-18 Night Vision goggles (OD)", Ids.ArtemGpnvgOd, "WTT-Artem", [], Flea: true),
        new("GPNVG-18 Night Vision goggles (Black)", Ids.ArtemGpnvgBlack, "WTT-Artem", [], Flea: true),
        new("DevTac Ronin ballistic helmet (Alpine)", Ids.ArtemRoninAlpine, "WTT-Artem", [], Flea: true),
        new("DevTac Ronin ballistic helmet (Black)", Ids.ArtemRoninBlack, "WTT-Artem", [], Flea: true),
        new("Modernized Item Case", Ids.ArtemItemCase, "WTT-Artem", [], Flea: true),
        new("AirLite SPC (Multicam)", Ids.ArtemAirliteSpc, "WTT-Artem", [], Flea: true),
        new("LV-119 (Multicam)", Ids.ArtemLv119, "WTT-Artem", [], Flea: true),
        // Black Knight rig clones: Artem's flea price is 300, so they use the source rig's flea price (119930).
        new("Crye Precision CPC plate carrier (Multicam, 7a)", Ids.ArtemCpcMulticam1, "WTT-Artem", [], Flea: true, FleaPrice: 119930),
        new("Crye Precision CPC plate carrier (Alpine)", Ids.ArtemCpcAlpine, "WTT-Artem", [], Flea: true, FleaPrice: 119930),
        new("Crye Precision CPC plate carrier (Multicam, 7c)", Ids.ArtemCpcMulticam2, "WTT-Artem", [], Flea: true, FleaPrice: 119930),

        // More Cases Updated: unsellable through their sources. Flea only, at the mod's flea prices.
        new("Weapon Rack", Ids.MoreCasesWeaponRack, "More Cases Updated", [], Flea: true),
        new("Toolbox", Ids.MoreCasesToolbox, "More Cases Updated", [], Flea: true),
    ];
}
