# ModWeapons

**Requires:** SPT 4.1.6 server, WTT-ServerCommonLib `^3.0.0` (hard dependency: the server refuses to load this mod without it). Soft dependencies: WTT-Armory (source of the AEK-971 / AEK-973; without it only those two clones are skipped with a log line, no `ModDependencies` entry), Michael-NATOQuartermaster (its offers are skipped with a log line when absent), acidphantasm-progressivebotsystem (its tier pools are skipped when absent). Server-only; Fika 2.4.6 is unaffected (clients need no files).

## What
Adds modified clones of existing items. Every clone keeps the source's model, icon, description and slots, costs 2x the source in the handbook and on the flea, and is added to every quest condition, bot pool and mastery entry that lists the source (scanned at runtime, not hard-coded).

| Clone | Source | Changes vs source |
|---|---|---|
| "Knight's Armament Company MOD-SR-25 7.62x51 marksman rifle" | SR-25 | recoil up -5 (150), back -10 (346), ergonomics +16 (63), durability burn x0.5 (0.575) |
| "KMZ MOD-AEK-973 7.62x39 assault rifle" | WTT-Armory AEK-973 | recoil up 89, back 278, ergonomics 54, durability burn 0.575 (same deltas), flea-sellable |
| "KMZ MOD-AEK-971 5.45x39 assault rifle" | WTT-Armory AEK-971 | recoil up 77, back 237, ergonomics 54, durability burn 0.575 (same deltas) |
| "AK 7.62x39 X Products MOD-X-47 50-round drum magazine" | X-47 drum | ergonomics -3 (source -15), flea-sellable |

- Prices: handbook / flea. SR-25 49210 / 214746. AEK-973 198000 / 420000. AEK-971 172000 / 378000. X-47 59400 / 59400 (the source has no flea price of its own, SPT falls back to its 29700 handbook price). All four are eligible for dynamic flea offers (weapons as full default build). The AEK-973 and X-47 sources have CanSellOnRagfair false, so their clones override it to true.
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

  Prices are read at startup. If a source offer is missing a warning is logged and 2x the source handbook price (x1.25 for the NATO X-47) is used.
- Bots: wherever a bot weighted pool (equipment slots, backpack, pockets, vest, secure container, special loot) lists the source, 25% of its weight moves to the clone (source keeps 75%), so pool totals are unchanged. The source's mod pool is copied to the clone, and the clone joins every weapon slot choice (for example magazine sets) that lists the source. Those slot choices are plain sets without weights, so the clone competes there on equal terms instead of at 25%. Progressive Bot System builds bot weapons from its own tier pools, so every tier / bot / slot of it gets the same treatment (reflection, no hard reference).
- The X-47 clone is added to every slot, chamber, cartridge and grid filter, grid exclusion and conflicting-items list that lists the X-47, so it fits (and is kept out) exactly where the X-47 is (scanned at startup, count logged).
- Weapons share the source's weapon mastery.

## Why
Stronger, rarer variants that still count for the source's quests and mastery.

## How
Items, presets and prices via WTT item JSON (clones of the sources): `db/CustomItems` (SR-25, X-47), `db/Aek973`, `db/Aek971`. WTT loads a directory recursively and logs an error per failing item, so each optional-source clone has its own directory, passed to WTT only when its source item exists. Clone definitions (ids, prices, traders, assort ids) are one list in `ModWeaponDefs.cs`. Two `IOnLoad` classes: items (Preload + 10, after WTT-Armory at Preload + 2) and quests / bots / filters / traders (TraderRegistration + 10, after NATO Quartermaster built its assort at +2, before trader/flea/preset callbacks).
Trader offers use an unlimited-stock root with a purchase limit: SPT 4.1.6 does not restore decremented stock on restock but does reset each profile's purchase counts, so the limit refills every restock.

## Build and deploy
Build with `nix shell nixpkgs#dotnet-sdk_10 -c dotnet build -c Release` in `ModWeapons/`, then `scripts/deploy-server-mod.sh ModWeapons` and `scripts/restart-all.sh`.

## Settings
None. Prices and stats are in `db/**/*.json` (flea/handbook/stats) and `ModWeaponDefs.cs` (trader prices, limits, levels).
