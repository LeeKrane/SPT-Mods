# FleaOfferGuard4Server

**Requires:** SPT 4.1.6 server. No other mods. Server-only; Fika 2.4.6 is irrelevant and unaffected. Pairs with the client plugin `FleaOfferGuard` (optional, stops the bad request at the source).

## What
Rejects a player flea listing with a normal client error ("Unable to add offer: invalid price"). The item stays in the inventory and nothing in the profile changes. Rejected when:
- the requirement list is missing or empty;
- any requirement count is NaN, Infinity, zero, negative, or rounds to 0.00 (offers store counts to 2 decimals);
- the listing tax computed by the server comes out NaN/Infinity/negative, when listing an offer or when renewing one (the tax is paid into the profile, so it is checked before it is charged).

Also ignores a missing, non-finite or negative listing fee reported by the client and clears any older cached fee for that item, so the server computes the tax itself. A warning with session id, item template and bad value is logged for every rejection. Any other failed listing (for example the fee cannot be paid) gets the same cleanup. Valid listings run unchanged.

## Why
QuickSellFlea's multi-sell posted offers with a price of NaN, Infinity or less than 1 for modded items without a flea price. The server accepted them, the NaN ended up in the profile, and every profile save then threw `ArgumentException` (System.Text.Json cannot write NaN) until the server was restarted. SPT also hands out an average of `-1` for items with no player offers, which clients can post as a negative price.

## How
Harmony patches (same mechanism as AishiInsuranceDiscount4Server):
- Prefix on `RagfairController.AddPlayerOffer` checks requirement counts, before anything is read or changed, and snapshots the stack counts of the listed items. A postfix on `RagfairOfferGenerator.CreateAndAddFleaOffer` notes the id of each flea offer created during the call.
- Postfix on `RagfairTaxService.CalculateTax` aborts the call, only while `AddPlayerOffer` or `ExtendOffer` runs. Finalizers on both turn the abort into the same error response. They run before the profile is modified. The `AddPlayerOffer` finalizer also cleans up after any failed listing (its own abort, or an error response such as an unpayable fee): it restores the stack counts that multi and pack listings had already rewritten, and removes, by id, the in-memory flea offers the listing had created.
- Prefix on `RagfairTaxService.StoreClientOfferTaxValue` skips and clears a bad client fee.

If patching fails, any patch already applied is removed, a warning is logged and the server still loads, without protection.

## Build and deploy
The server runs on taragarmr. The net10/SDK 8 fallback and the SPT references live in the shared `ServerMod.props` at the repo root, imported by each server mod's csproj. Build with `-c Release` (needs the .NET 10 SDK, or SDK 8 plus a dotnet root holding the .NET 10 shared runtimes via `DOTNET_ROOT` or `-p:DotnetRoot=...`), then `scripts/deploy-server-mod.sh FleaOfferGuard4Server`. It takes effect on the next server restart.

## Settings
None. There is no `config.json`: rejecting invalid prices is always on.
