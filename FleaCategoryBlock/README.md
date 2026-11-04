# FleaCategoryBlock

**Requires:** SPT 4.1.6, Fika 2.4.6 (not used, unaffected). Hard dependency on the server mod `FleaCategoryBlock4Server`: the server does the filtering, without it nothing is hidden. Each player sets it for themselves.

## What
Hides other players' flea offers per handbook subcategory (for example Ammo / Rounds, Medication / Medkits, Barter items / Building materials), so you cannot buy them.
- Only your own searches are filtered. Trader offers and your own offers stay.
- The server filters before paging, so pages are full and offer and page counts are exact.
- The category tree and its counts are unchanged (counts still include the hidden offers).
- Fallback: buying a disabled player offer that is still on screen (loaded before a change, or the server mod is missing) is refused with a warning.

Disabling a subcategory also covers every subcategory and item below it. A switch in `0. Global` blocks a whole top-level category; the master switch `Enabled` turns all blocking off without losing the per-category choices.

## Why
Self-imposed flea restrictions (for example no player-bought ammo or meds) without changing the flea for anyone else.

## How
- Settings are created once after login, when both the handbook (postfix on `RagFair.UpdateSettings`) and the full locale with category names (postfix on `LocalizationManager.UpdateLocales`) are loaded. They appear in the config file and Configuration Manager after the first login.
- The ids of the disabled subcategories are posted to `/krane/fleacategoryblock/set` once per login and on every change. The server saves them per profile, so a server restart keeps them. After a change, the cached offer pages are dropped once the server has the new list; offers already on screen stay until the list reloads.
- Prefix on `RagfairScreen.OfferPurchaseHandler` refuses the purchase window for a disabled player offer.

## Settings
Generated at runtime: section `0. Global` (named to sort first), then one section per top-level handbook category with one entry per subcategory below it (key = path below the top level, e.g. `Functional mods / Sights`).

| Section / Key | Default | Effect |
|---|---|---|
| `0. Global` / `Enabled` | `true` | Master switch. Off: no offers are hidden, all other settings are ignored. |
| `0. Global` / `<top-level category>` | `false` | Hide other players' flea offers in this whole top-level category (e.g. all Barter items). |
| `<top-level category>` / `<subcategory path>` | `false` | Hide other players' flea offers in this subcategory and everything below it. |
