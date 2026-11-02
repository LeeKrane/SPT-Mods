# FleaCategoryBlock

**Requires:** SPT 4.1.6, Fika 2.4.6 (not used, unaffected). No other mods. Client-only; each player sets it for themselves.

## What
Lets you stop yourself from buying player offers on the flea market, per handbook subcategory (for example Ammo / Rounds, Medication / Medkits, Barter items / Building materials).
- Player offers of a disabled subcategory are hidden from the offer list. Trader offers and your own offers stay.
- The browse tree is unchanged, so trader offers of every subcategory stay reachable. Category counts come from the server and still include the hidden player offers.
- Buying a disabled player offer (money or barter, single or mass purchase) is refused with a warning, as a fallback.

Disabling a subcategory also covers every subcategory and item below it.

## Why
Self-imposed flea restrictions (for example no player-bought ammo or meds) without changing the server for everyone.

## How
- Settings are created once after login, when both the handbook (postfix on `RagFair.UpdateSettings`) and the full locale with category names (postfix on `LocalizationManager.UpdateLocales`) are loaded. They appear in the config file and Configuration Manager after the first launch into the main menu.
- Pages stay full (prefix and postfix on `EftClientBackendSession.GetOffers`): a selection that is in or contains a disabled category (also all offers, linked or needed search) is requested as one block of up to `Max offers per request` offers. Disabled player offers are dropped and the requested page is cut from the rest, with the total count set to what is left. Every page change fetches the block again. The server sorts the whole block, so sorting stays correct. Owner filter and tree counts are untouched.
- Prefix on `RagFair.ProceedWithOffers` removes disabled player offers from every page before it is cached or shown, as a safety net.
- Prefix on `RagfairScreen.OfferPurchaseHandler` refuses the purchase window.

Changing a setting clears the offer cache. Offers already on screen stay until the list reloads (change category or reopen the flea).

## Settings
Generated at runtime: one section per top-level handbook category, one entry per subcategory below it (key = path below the top level, e.g. `Functional mods / Sights`).

| Section / Key | Default | Effect |
|---|---|---|
| General / Max offers per request | `1000` | Offers fetched at once when a selection contains a disabled category (100 to 5000). If the server has more, the rest is not shown and a warning is logged. Changing it clears the offer cache. |
| `<top-level category>` / `<subcategory path>` | `false` | Block buying player flea offers in this subcategory and everything below it. |
