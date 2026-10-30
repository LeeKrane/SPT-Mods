# ItemTweaks4Server

**Requires:** SPT 4.1.6 server, WTT-ServerCommonLib `^3.0.0` (hard dependency: the server refuses to load this mod without it). Soft dependencies: WTT-Armory (source of the AEK-971 / AEK-973; without it only those two clones are skipped with a log line, no `ModDependencies` entry), Michael-NATOQuartermaster (its offers are skipped with a log line when absent), the client plugin ItemTweaks (applies the weight reduction of the two cases and the PS-321 view changes; without it the cases work but their contents keep their weight, and the PS-321 looks like a PS-320), acidphantasm-progressivebotsystem (its tier pools are skipped when absent). Fika 2.4.6 is unaffected. Clients need the ItemTweaks plugin (soft: only for the case weight cut, the case looks and the PS-321 view; without it the items still work).

## What
Adds modified clones of existing items, plus two new items (the spec-ops kit case and the spec-ops key case, below). Every clone keeps the source's model, icon, description and slots, costs 2x the source in the handbook and on the flea, and is added to every quest condition, bot pool and mastery entry that lists the source (scanned at runtime, not hard-coded).

| Clone | Source | Changes vs source |
|---|---|---|
| "Knight's Armament Company MOD-SR-25 7.62x51 marksman rifle" | SR-25 | recoil up -5 (150), back -10 (346), ergonomics +16 (63), durability burn x0.5 (0.575) |
| "KMZ MOD-AEK-973 7.62x39 assault rifle" | WTT-Armory AEK-973 | recoil up 89, back 278, ergonomics 54, durability burn 0.575 (same deltas), flea-sellable |
| "KMZ MOD-AEK-971 5.45x39 assault rifle" | WTT-Armory AEK-971 | recoil up 77, back 237, ergonomics 54, durability burn 0.575 (same deltas) |
| "AK 7.62x39 X Products MOD-X-47 50-round drum magazine" | X-47 drum | ergonomics -3 (source -15), flea-sellable |
| "Valday PS-321 1/5x scope" (`41a2f314a21e64a4862d6bd1`) | Valday PS-320 1/6x | ergonomics -2 (source -5), Zooms 5/1 and aim sensitivity 0.091 (5x) instead of 6x. The real 5x zoom, a closer camera and a smaller lens shadow are done by the client plugin ItemTweaks; without it the scope still shows 6x |

- **Spec-ops kit case** ("Spec-ops kit case", short name SpecOps, `f39e1020a6eb538f07653b87`): a grey 1x1 clone of the dogtag case (same model) with one 3x6 grid that only takes special items (whole category), body armor repair kits, weapon repair kits, CMS and Surv12. It is added to every slot named SpecialSlot* (the 1x4 pockets with special slots, count logged), so it fits there. Its contents weigh less (90% by default), done by the client plugin ItemTweaks. Not flea-sellable. Handbook and flea price 8349000 (69000 USD at the handbook rate of 121). It is not a variant of the dogtag case: it joins none of its quests, bot pools or filters. Sold only by NATO Quartermaster, LL1, 69000 USD, unlimited stock, 1 per profile per restock (skipped with a log line when the trader is absent). Container filters that list the dogtag case explicitly (secure containers, purse, track case, thicc items case) do not list it, so it does not fit them; backpacks and the like accept it by category.
- **Spec-ops key case** ("Spec-ops key case", short name SOKey, `c40d4da0b8d5434d00fffb4c`): a 1x1 clone of the Gingy keychain (same model, darkened by the client plugin) with one 4x4 grid that only takes keys and keycards (whole category), the contraband key (`67449b6c89d5e1ddc603f504`, item_barter_other_contraband_key: a barter item outside the Key category that the key tool accepts), keycard holders, the key tool and the Gingy keychain; it does not take itself. Like the spec-ops kit case it is added to every SpecialSlot* slot, and additionally to the grid filter of every secured container (parent 5448bf274bdc2dfc2f8b456a) whose filter lists the key tool (scanned at runtime, count logged), so it fits the key containers. Contents weigh less (90% by default), done by the client plugin. Not flea-sellable. Handbook and flea price 8349000, sold by NATO Quartermaster, LL1, 69000 USD, 1 per profile per restock (skipped with a log line when the trader is absent). Not a variant of the keychain.
- Prices: handbook / flea. PS-321 76240 / 85484. SR-25 49210 / 214746. AEK-973 198000 / 420000. AEK-971 172000 / 378000. X-47 59400 / 59400 (the source has no flea price of its own, SPT falls back to its 29700 handbook price). The weapon, magazine and scope clones are eligible for dynamic flea offers (weapons as full default build). The AEK-973 and X-47 sources have CanSellOnRagfair false, so their clones override it to true.
- Weapons have the source's default build as default preset (SR-25: 13 parts, AEKs: 7 parts), with fresh ids.
- Trader offers, roubles, unlimited stock with a per-profile purchase limit per restock:

| Item | Trader | LL | Price | Limit |
|---|---|---|---|---|
| MOD-SR-25 | Jaeger | 4 | 2x Peacekeeper's SR-25 price (money entries summed, in roubles) | 1, full build |
| MOD-SR-25 | NATO Quartermaster | 1 | 2x its own SR-25 price | 1, full build |
| MOD-AEK-973 | Mechanic, NATO Quartermaster | 4, 1 | 420000 | 1, full build |
| MOD-AEK-971 | Mechanic, NATO Quartermaster | 4, 1 | 378000 | 1, full build |
| MOD-X-47 | Mechanic | 4 | 2x Mechanic's own X-47 price (59400) | 6 |
| MOD-X-47 | NATO Quartermaster | 1 | 2x its own X-47 offer, else 2x Mechanic's price x1.25 (74250) | 6 |
| PS-321 | Prapor | 4 | 4x Bottle of vodka Tarkovskaya (the PS-320 barter, 2x, doubled) | 1 |
| PS-321 | NATO Quartermaster | 1 | 85484 (2x the PS-320 flea price) | 3 |
| Spec-ops kit case | NATO Quartermaster | 1 | 69000 USD | 1 |
| Spec-ops key case | NATO Quartermaster | 1 | 69000 USD | 1 |

  Prices are read at startup. If a source offer is missing a warning is logged and 2x the source handbook price (x1.25 for the NATO X-47) is used.
- Bots: wherever a bot weighted pool (equipment slots, backpack, pockets, vest, secure container, special loot) lists the source, 25% of its weight moves to the clone (source keeps 75%), so pool totals are unchanged. The source's mod pool is copied to the clone, and the clone joins every weapon slot choice (for example magazine sets) that lists the source. Those slot choices are plain sets without weights, so the clone competes there on equal terms instead of at 25%. Progressive Bot System builds bot weapons from its own tier pools, so every tier / bot / slot of it gets the same treatment (reflection, no hard reference).
- The X-47 and PS-321 clones are added to every slot, chamber, cartridge and grid filter, grid exclusion and conflicting-items list that lists their source, so they fit (and are kept out) exactly where the source is (scanned at startup, count logged).
- Weapons share the source's weapon mastery.

## Why
Stronger, rarer variants that still count for the source's quests and mastery.

## How
Items, presets and prices via WTT item JSON (clones of the sources): `db/CustomItems` (SR-25, X-47, PS-321, spec-ops kit case; names and descriptions are in each file's `locales`), `db/KeyCase` (spec-ops key case, own directory so a missing Gingy keychain cannot block the shared one), `db/Aek973`, `db/Aek971`. WTT loads a directory recursively and logs an error per failing item, so each optional-source clone has its own directory, passed to WTT only when its source item exists. Clone definitions (ids, prices, traders, assort ids, barters) are one list in `ItemDefs.cs`. Two `IOnLoad` classes: items (Preload + 10, after WTT-Armory at Preload + 2) and quests / bots / filters / special slots / secure containers / traders (TraderRegistration + 10, after NATO Quartermaster built its assort at +2, before trader/flea/preset callbacks).
Special slots are filled by our own code, not WTT's `addtoSpecialSlots`: that option only knows two fixed pockets and adds to every slot of them (not just SpecialSlot*), at Preload + 10 before other mods' pockets exist.
Trader offers use an unlimited-stock root with a purchase limit: SPT 4.1.6 does not restore decremented stock on restock but does reset each profile's purchase counts, so the limit refills every restock.

## Build and deploy
Build with `nix shell nixpkgs#dotnet-sdk_10 -c dotnet build -c Release` in `ItemTweaks4Server/`, then `scripts/deploy-server-mod.sh ItemTweaks4Server` and `scripts/restart-all.sh`. This project was renamed from ModWeapons: remove the old remote directory `krane-ModWeapons` (the server would otherwise load both).

## Settings
None. Prices and stats are in `db/**/*.json` (flea/handbook/stats) and `ItemDefs.cs` (trader prices, limits, levels).
