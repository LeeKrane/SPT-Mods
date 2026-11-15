# SPT mod inventory

Generated: 2026-11-15 (generated file, do not edit). Source: installed files only (assembly metadata, no logs, nothing loaded).

Snapshots: Server 2026-11-15, Headless 1 2026-11-15, Headless 2 2026-11-15, tarmantria 2026-11-15, tariognatha none.

Regenerate: `scripts/inventory.sh all` (read-only rsync from taragarmr, this machine's snapshot, then the matrix). `scripts/inventory.sh matrix` re-renders from the stored snapshots (`inventory/*.json`) and the intent manifest `scripts/inventory/mods.json`. tariognatha adds its own snapshot: on that machine `git pull`, `scripts/inventory.sh all` (or `scripts/inventory.sh local --json > inventory/tariognatha.json`), then commit `inventory/tariognatha.json` and `INVENTORY.md`. Needs `dotnet` 10 or `nix` (see `scripts/inventory/README.md`).

## Legend

Server column: version of the server part. Device columns: version of the client plugin(s) and patchers. Differing versions among a mod's parts on one device show as `a/b` (often companion plugins or bundled libraries with their own version). Symlinked krane plugins add `@<commit>` (last repo commit touching the plugin).

| Cell | Meaning |
|---|---|
| `1.2.3` | intended and installed |
| `!` | intended but missing |
| `-` | not intended (e.g. server only or client only) and absent |
| `1.2.3 ⚠` | installed but not intended on that device |
| `?` | no snapshot for that device yet |

Intent comes from `scripts/inventory/mods.json` (targets `server`, `headless` for both headless clients, `clients` for tarmantria and tariognatha). Mods not in the manifest are flagged _(not in manifest)_; classify them there.

## Matrix

Mods needing attention (`!`, `⚠`, not in manifest) come first, then the rest alphabetically.

| Mod | Server | Headless 1 | Headless 2 | tarmantria | tariognatha |
|---|---|---|---|---|---|
| ManimalTerminal | 0.2.20 | 0.2.20 | 0.2.20 | ! | ? |
| RUAFComeHome | 1.2.1 | 1.2.1 | 1.2.1 | ! | ? |
| 7Bpencil.BrighterInteriors | - | - | - | 1.1.1 | ? |
| Acid's Bot Placement System | 2.1.0 | 2.1.0 | 2.1.0 | 2.1.0 | ? |
| Acid's Progressive Bot System | 2.4.1 | - | - | - | ? |
| AcksBerg-DisableVersionLabelInRaid | - | - | - | 1.0.0 | ? |
| AdsCameraDistance | - | - | - | 1.0.0 @2000e48 | ? |
| AishiInsuranceDiscount | 1.0.0 | - | - | - | ? |
| All Quests Checkmarks | 1.4.0 | - | - | 1.4.0 | ? |
| Artem | 3.0.2 | - | - | - | ? |
| Auto Keycard | - | - | - | 1.0.1 | ? |
| BackpackGunSlot | 1.1.1 | - | - | - | ? |
| BALL - BetterAmmoLoadingList | - | - | - | 1.1.1 | ? |
| BetterBepInEx | - | - | - | 1.0.2 | ? |
| BetterKeysNG | 1.8.0 | - | - | - | ? |
| BetterSuppressors | 1.0.0 | - | - | - | ? |
| BlackDiv | 1.3.1 | 1.3.1 | 1.3.1 | 1.3.1 | ? |
| BlackHawk-QuickMeds | - | - | - | 1.2.0 | ? |
| Bosses Have GP Coins | 1.1.0 | - | - | - | ? |
| Bosses Have Lega Medals | 2.1.0 | - | - | - | ? |
| Bot Callsigns | 2.0.5 | - | - | - | ? |
| BtrInteriorVolume | - | - | - | 1.0.0 @30dd1eb | ? |
| ColorConverterAPI | - | 1.2.0 | 1.2.0 | 1.2.0 | ? |
| Content Backport - Prestiges | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | ? |
| Continuous Load Ammo | - | - | - | 1.2.0 | ? |
| ContinuousHealing | - | - | - | 1.6.3 | ? |
| CraftQueue | 2.4.2 | - | - | - | ? |
| CSGas | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | ? |
| DecalLockFix | - | - | - | 1.0.0 @2b0f28c | ? |
| Default Firemode Fixer | 2.1.0 | - | - | - | ? |
| DrakiaXYZ-BigBrain | - | 1.5.0 | 1.5.0 | 1.5.0 | ? |
| DrakiaXYZ-QuestTracker | - | - | - | 1.7.0 | ? |
| DrakiaXYZ-TaskListFixes | - | - | - | 1.8.0 | ? |
| DrakiaXYZ-Waypoints | - | 1.9.0 | 1.9.0 | 1.9.0 | ? |
| Dynamic Maps | 1.2.1 | - | - | 1.2.1 | ? |
| Expanded Task Text | 2.1.0 | - | - | - | ? |
| ExtraSpecialSlots | 1.1.0 | - | - | 1.1.0 | ? |
| FactoryClassic | 1.2.0 | 1.2.0 | 1.2.0 | 1.2.0 | ? |
| Fast Grenade Throw | - | - | - | 2.0.0 | ? |
| Fika | 2.4.2 | 2.4.6 | 2.4.6 | 2.4.6 | ? |
| Fika.Headless | - | 1.5.3 | 1.5.3 | - | ? |
| FixPluginTypesSerialization | - | 1.0.0 | 1.0.0 | 1.0.0 | ? |
| FleaCategoryBlock | 1.0.0 | - | - | 1.1.0 @9233aa6 | ? |
| FleaOfferGuard | 1.0.0 | - | - | 1.0.0 @cb87b6a | ? |
| Gilded Key Storage | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 | ? |
| Gunsmith Trader | 1.0.2 | - | - | - | ? |
| Halloween2026 backport | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 | ? |
| HandsAreNotBusy | - | - | - | 1.7.1 | ? |
| Harry Hideout | 3.1.2 | - | - | - | ? |
| HeadlessGuard | - | - | - | 1.0.0 @a885272 | ? |
| Hideout In Progress | 3.0.0 | - | - | 3.0.0 | ? |
| Hideout We Deserve - TimeRP | 1.4.0 | - | - | - | ? |
| Increase Climb Height | 2.1.1 | - | - | - | ? |
| InfectedVariants | 1.3.0 | 1.3.0 | 1.3.0 | 1.3.0 @e5637cf | ? |
| Instant Insurance | 1.1.2 | - | - | - | ? |
| InstantTraderDialogs | - | - | - | 1.0.0 @c524d12 | ? |
| IOF: Inventory Organizing Feature | - | - | - | 1.10.1 | ? |
| ISB Aishi | 2.0.4 | 2.0.4 | 2.0.4 | 2.0.4 | ? |
| ISB Special Forces | 2.0.1 | 2.0.1 | 2.0.1 | 2.0.1 | ? |
| Item Limits Begone | 1.1.0 | - | - | - | ? |
| Item Valuation | 2.1.0 | - | - | - | ? |
| ItemTweaks | 1.0.0 | - | - | 1.0.0 @77d43c8 | ? |
| JoinInsurance | - | - | - | 1.0.0 @2b0f28c | ? |
| Kaeno-TraderScrolling | - | - | - | 4.0.0 | ? |
| KeepMeAlive | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | ? |
| Lacyway's PvE Tweaks | 1.3.2 | - | - | - | ? |
| Lawnmower | - | - | - | 1.1.0 | ? |
| Ledger | 1.1.0 | - | - | - | ? |
| Live Flea Prices | 2.1.0 | - | - | - | ? |
| LoadSpeedFix | - | - | - | 1.0.0 @2b0f28c | ? |
| Lotus | 1.8.1 | - | - | - | ? |
| Magazine Check Interrupt | - | 1.1.1 | 1.1.1 | - | ? |
| MagTopUp | - | - | - | 1.1.0 @b6f8784 | ? |
| ManimalIcebreaker | 1.1.3 | 1.1.3 | 1.1.3 | 1.1.3 | ? |
| ManimalInterchangeRework | 1.0.8 | 1.0.8 | 1.0.8 | 1.0.8 | ? |
| ManimalLabsBoiler | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | ? |
| ManimalLighthouseRework | 1.0.7 | 1.0.7 | 1.0.7 | 1.0.7 | ? |
| MapVariants | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | ? |
| maschine-TraderSearch | - | - | - | 2.1.0 | ? |
| MoreBots | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 | ? |
| MoreCases | 3.0.0 | - | - | - | ? |
| NATO Quartermaster | 1.1.3 | - | - | - | ? |
| ORBIT | 2.1.1 | 2.1.1/1.1.0 | 2.1.1/1.1.0 | 2.1.1/1.1.0 | ? |
| PastPresentEventSelector | 1.7.7 | 1.7.7 | 1.7.7 | 1.7.7 | ? |
| PlayerEncumbranceBar | - | - | - | 1.3.0 | ? |
| PrestigeDogtagFix | 1.0.0 | - | - | - | ? |
| PrestigeTransferFilledContainers | - | - | - | 1.0.0 @67a3e11 | ? |
| QuestCodex | 1.11.1 | - | - | - | ? |
| QuickSellFlea | - | - | - | 1.4.5 | ? |
| QuietWeather | - | - | - | 1.0.1 | ? |
| Raid Review | 1.8.0 | - | - | - | ? |
| ReceiveAllChats | 2.0.1 | - | - | - | ? |
| Ref Extra Quests | 1.9.0 | - | - | - | ? |
| Ref Friendly Quests | 2.1.0 | - | - | - | ? |
| RememberInsurer | - | - | - | 1.0.0 @7ddbd79 | ? |
| RoguesVRaiders | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 | ? |
| SAIN | 4.5.1 | 4.5.1 | 4.5.1 | 4.5.1 | ? |
| ScopeZoomPresets | - | - | - | 1.0.0 @9b1fce8 | ? |
| Scorpion | 1.1.1 | - | - | - | ? |
| SearchSpeed | - | - | - | 1.0.0 @0858d00 | ? |
| sgtlaggy's Quest Tweaks | 4.9.0 | - | - | - | ? |
| Shinni's 1.0 QuestsBackport | 0.3.2 | - | - | - | ? |
| SitOnMyFace | 0.1.2 | - | - | - | ? |
| SmajlecStamina | - | - | - | 1.4.0 | ? |
| SPT core plugins | - | 18.4/4.1.6 | 18.4/4.1.6 | 18.4/4.1.6 | ? |
| StatTrack™ | 2.1.1 | - | - | - | ? |
| StorylineCleanup | 1.0.0 | - | - | - | ? |
| SVM | 2.2.3 | - | - | - | ? |
| tarkin ladders | - | 1.0.4/1.0.1 | 1.0.4/1.0.1 | 1.0.4/1.0.1 | ? |
| Terkoiz.FlareEventNotifier | - | - | - | 1.0.6 | ? |
| Terkoiz.Skipper | - | - | - | 1.1.5 | ? |
| TerminalCommander | 1.2.0 | - | - | - | ? |
| The Blacklist | 3.1.1 | - | - | - | ? |
| TraderTweaks | - | - | - | 1.0.0 @a8e18ad | ? |
| Tyfon.AutoDeposit | - | - | - | 6.0.0 | ? |
| UI Fixes | 6.0.4 | 6.0.4 | 6.0.4 | 6.0.4 | ? |
| UnityToolkit | - | 2.0.2/6.1.2 | 2.0.2/6.1.2 | 2.0.2/6.1.2 | ? |
| UNTARGH | 3.2.1 | 3.2.1 | 3.2.1 | 3.2.1 | ? |
| Use Items Anywhere | - | 2.1.6/1.0.0 | 2.1.6/1.0.0 | 2.1.6/1.0.0 | ? |
| UseItemsAnywhereTweaks | - | - | - | 5.1.0 @0486e42 | ? |
| VisitAPI | 1.3.7 | 1.3.7 | 1.3.7 | 1.3.7 | ? |
| VisitButtonFix | - | - | - | 1.0.0 @ab58b37 | ? |
| Wolfiks Heavy Troopers | 1.2.0 | - | - | - | ? |
| WTT CommonLib | 3.0.8 | 3.0.8 | 3.0.8 | 3.0.8 | ? |
| WTT-Armory | 2.0.5 | 2.0.5 | 2.0.5 | 2.0.5 | ? |
| WTT-ContentBackport | 2.0.7 | 2.0.7 | 2.0.7 | 2.0.7 | ? |

## Attention

- `!` missing: ManimalTerminal on tarmantria
- `!` missing: RUAFComeHome on tarmantria

## Pending archives and backups

| Where | Path |
|---|---|
| Install archives, tarmantria | `~/Games/spt/pending-server-mods` |
| Install archives, taragarmr | `~/spt-pending-mods` |
| Backups, taragarmr | `~/spt-mod-backups/2026-11-15` |
| Backups, tarmantria | `~/Games/spt/backups` |
