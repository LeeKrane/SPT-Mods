# FleaCategoryBlock4Server

**Requires:** SPT 4.1.6 server. Server-only; Fika 2.4.6 is unaffected. Pairs with the client plugin `FleaCategoryBlock`, which sends each player's disabled categories. Players without the plugin are never filtered.

## What
Hides other players' flea offers in handbook categories a player disabled on their client, from that player's flea searches only.
- Trader offers and the player's own offers stay.
- Offers are removed before sorting and paging, so pages are full and offer and page counts are exact.
- Category counts in the browse tree are built before the filter and stay unchanged.

## Why
Lets a player restrict their own flea use per category, client-configured, without affecting anyone else.

## How
- Static route `/krane/fleacategoryblock/set`, body `{"categories":["<handbook category id>", ...]}`. Stores the list per session (PHPSESSID, the profile id); an empty list removes it. Saved to `state/blocked.json` in the mod folder, so lists survive a server restart. The deploy script never touches `state/`.
- Harmony prefix and finalizer on `RagfairController.GetOffers` mark the searching session on the thread. A prefix on `RagfairController.AddIndexValueToOffers`, called exactly once per search after the category counts and before indexing, sorting, the count and pagination, removes that session's blocked player offers from the fresh offer list (build searches included) and consumes the mark.
- An item counts as blocked when its handbook category or any category above it is in the list. Items missing from the handbook are never blocked.

If patching fails, any patch already applied is removed, a warning is logged and the server still loads, without filtering.

## Build and deploy
Build with `dotnet build -c Release` (output goes to `out/`, see `ServerMod.props` for the SDK requirements), then `scripts/deploy-server-mod.sh FleaCategoryBlock4Server`. It takes effect on the next server restart.

## Settings
None. There is no `config.json`; each player configures the client plugin.
