# ModWeapons

**Requires:** SPT 4.1.6 server, WTT-ServerCommonLib `^3.0.0` (hard dependency: the server refuses to load this mod without it). Soft dependencies: Michael-NATOQuartermaster (its offer is skipped with a log line when absent), acidphantasm-progressivebotsystem (its tier pools are skipped when absent). Server-only; Fika 2.4.6 is unaffected (clients need no files).

## What
Adds custom weapons. Currently the "Knight's Armament Company MOD-SR-25 7.62x51 marksman rifle", a modified SR-25.
- Same model, icon, description and attachment slots as the SR-25. Recoil up 150 (-5), recoil back 346 (-10), ergonomics 63 (+16).
- Handbook price 49210, flea price 214746 (2x SR-25). Eligible for dynamic flea offers, which use the full default build (same 13 parts as the SR-25 default preset).
- Trader offers, full default build, 1 per restock: Jaeger at LL4 and NATO Quartermaster at its only level, both in roubles, price 2x the SR-25 trader price (Jaeger: Peacekeeper's SR-25 price, all money entries summed and converted to roubles; NATO: its own SR-25 offer). Prices are read at startup; if the source offer is missing a warning is logged and 2x the SR-25 handbook price is used.
- Added to every quest condition that lists the SR-25 (scanned at runtime, not hard-coded).
- Bots that can spawn with the SR-25 (weapon slots and loot pools) can spawn with it too: 25% of the SR-25's weight moves to it (SR-25 keeps 75%), so pool totals are unchanged; its mod pool is copied from the SR-25's. Progressive Bot System builds bot weapons from its own tier pools, so every one of its tiers/bots/slots containing the SR-25 gets the same split and mod pool copy (reflection, no hard reference).
- Shares the SR-25 weapon mastery.

## Why
A stronger, rarer SR-25 variant that still counts for SR-25 quests and mastery.

## How
Item, preset and prices via WTT `db/CustomItems/ModSR25.json` (clone of the SR-25). Two `IOnLoad` classes: items (Preload + 10), and quests/bots/traders (TraderRegistration + 10, after NATO Quartermaster built its assort at +2, before trader/flea/preset callbacks).
Trader offers use an unlimited-stock root with a purchase limit of 1: SPT 4.1.6 does not restore decremented stock on restock but does reset each profile's purchase counts, so the limit refills every restock.

## Build and deploy
Build with `nix shell nixpkgs#dotnet-sdk_10 -c dotnet build -c Release` in `ModWeapons/`, then `scripts/deploy-server-mod.sh ModWeapons` and `scripts/restart-all.sh`.

## Settings
None. Prices and stats are in `db/CustomItems/ModSR25.json` (flea/handbook/stats) and `ModSR25Integrations.cs` (trader price factor).
