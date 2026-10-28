# AishiInsuranceDiscount

**Requires:** SPT 4.1.6 server, ISB-Aishi `~2.0.4` (>=2.0.4 <2.1.0; hard dependency: the server refuses to load this mod without it). Server-only; Fika 2.4.6 is irrelevant and unaffected.

## What
Makes Aishi insurance cheaper by a configurable percentage, for both normal and restricted items. Repairs and all other traders are untouched.

## Why
ISB-Aishi prices insurance in dollars at 1.4x (4x for restricted items) of the native price, which is expensive.

## How
Postfix on `Aishi.DollarServicePolicy.InsurancePrice`, the single function that produces Aishi insurance prices for both the Cost quote and the Insure payment. Result becomes `ceil(price * (1 - DiscountPercent / 100))`. If patching fails, a warning is logged and the discount is skipped; the server still loads.

## Build and deploy
The server runs on taragarmr. The net10/SDK 8 fallback and the SPT references live in the shared `ServerMod.props` at the repo root, imported by the csproj. Build with `-c Release` (needs the .NET 10 SDK, or SDK 8 plus a dotnet root holding the .NET 10 shared runtimes via `DOTNET_ROOT` or `-p:DotnetRoot=...`), then `scripts/deploy-server-mod.sh AishiInsuranceDiscount` and `scripts/restart-all.sh`.

## Settings
Edit `config.json` in the server's mod folder (`spt-server/SPT_Runtime/user/mods/krane-AishiInsuranceDiscount/` on taragarmr), then restart the server. Deploy never overwrites it; the copy in `out/` is only the default template, copied once if the remote has none.

| Key | Default | Effect |
|---|---|---|
| DiscountPercent | `75` | Percent taken off Aishi insurance prices. Clamped to 0-99 (a free price is not allowed). 75 means price x 0.25. A missing or non-number value logs "invalid DiscountPercent" and uses 75. |
