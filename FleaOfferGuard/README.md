# FleaOfferGuard

**Requires:** SPT 4.1.6, Fika 2.4.6 (not used, unaffected). Soft dependency on QuickSellFlea 1.4.5 (`com.lacyway.csf`): without it the plugin does nothing. Pair it with the server mod `FleaOfferGuardServer`.

## What
Stops QuickSellFlea from posting offers with an invalid price, before any item leaves the stash.
- **Multi-select sell:** items whose market average is NaN, Infinity, zero or negative are dropped. The rest post normally and the button total only counts them. A warning notification says how many items were skipped. If none are left, the button reads "NO VALID PRICE" and does nothing.
- **Single item (and Shift select-all):** same check. QuickSellFlea only rejected prices `<= 0`, which NaN slips past. The button reads "NO VALID PRICE" and a warning shows.

A price also counts as invalid when the amount that would be posted rounds to 0 in the chosen posting currency (for example a 50 RUB item in USD) or to 0.00, the same bar the server applies. The check runs again when the button is clicked: if the posting currency was changed in between and an item is now invalid, the click is aborted with a warning and nothing changes (reopen the menu). In multi-select it waits until every item's price has arrived.

## Why
Modded items (Black Division, ISB, WTT gear) without player offers can get a NaN, Infinity or -1 average. QuickSellFlea multi-sell posted it as is, the server stored it in the profile, and every later profile save failed until the server restarted.

## How
Harmony prefixes on QuickSellFlea's private `SetPrices` and `ClickQuickOffer` in `MultiSell` and `ItemUiContext_GetItemContextInteractions_Patch`, plus `MultiSell.PriceData.SetPrice` (to know a price arrived), via reflection (no compile-time reference). If QuickSellFlea changes those members, the affected path logs a warning and runs unguarded.

## Settings
None. The guard is always on; remove the plugin to disable it.
