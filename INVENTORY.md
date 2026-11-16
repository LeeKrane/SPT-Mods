# SPT mod inventory

Generated: 2026-11-16 (generated file, do not edit). Source: installed files only (assembly metadata, no logs, nothing loaded).

Snapshots: Server 2026-11-15, Headless 1 2026-11-15, Headless 2 2026-11-15, tarmantria 2026-11-15, tariognatha 2026-11-16.

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
| ACCESS GRANTED | - | - | - | ! | 1.0.1 |
| Anvil-WebOverlay | - | - | - | ! | 1.11.0 |
| BackpackGunSlot | 1.1.1 | - | - | ! | 1.1.1 |
| Briefing | - | - | - | ! | 1.1.0 |
| Cabinet Looter | - | - | - | ! | 1.0.4 |
| CraftQueue | 2.4.2 | - | - | ! | 2.4.2 |
| Hideout We Deserve - UI Improvements | - | - | - | ! | 1.1.0 |
| IncreaseLookDirection | - | - | - | ! | 1.3.0 |
| LMSP | - | - | - | ! | 1.0.1 |
| Magazine Check Interrupt | - | 1.1.1 | 1.1.1 | ! | 1.1.1 |
| Manimal's Quick Cancel | - | - | - | ! | 2.0.0 |
| ManimalTerminal | 0.2.20 | 0.2.20 | 0.2.20 | ! | 0.2.20 |
| maschine-ModProfiler | - | - | - | ! | 1.4.0 |
| maschine-RaidMenuCleanupFix | - | - | - | ! | 2.0.0 |
| maschine-WeaponBuilderSearch | - | - | - | ! | 2.0.0 |
| More Tag Colours | - | - | - | ! | 1.5.0 |
| No Barrel Shadow | - | - | - | ! | 2.0.1 |
| No Scav Select | - | - | - | ! | 2.0.0 |
| Plate Durability | - | - | - | ! | 1.0.0 |
| PressurePads | - | - | - | ! | 1.1.1 |
| ReceiveAllChats | 2.0.1 | - | - | ! | 2.0.1 |
| RUAFComeHome | 1.2.1 | 1.2.1 | 1.2.1 | ! | 1.2.1 |
| Self Breath Volume | - | - | - | ! | 1.2.0 |
| Sicherheitsbehaelter ohne Gewicht | - | - | - | ! | 1.0.0 |
| Smoothie | - | - | - | ! | 1.1.0 |
| SmoothTalker | - | - | - | ! | 1.3.0 |
| StanceSync | - | - | - | ! | 1.1.1 |
| StatTrack™ | 2.1.1 | - | - | ! | 2.1.1 |
| VisorEffectManager | - | - | - | ! | 1.1.0 |
| 7Bpencil.BrighterInteriors | - | - | - | 1.1.1 | 1.1.1 |
| Acid's Bot Placement System | 2.1.0 | 2.1.0 | 2.1.0 | 2.1.0 | 2.1.0 |
| Acid's Progressive Bot System | 2.4.1 | - | - | - | - |
| AcksBerg-DisableVersionLabelInRaid | - | - | - | 1.0.0 | 1.0.0 |
| AdsCameraDistance | - | - | - | 1.0.0 @2000e48 | 1.0.0 @2000e48 |
| AishiInsuranceDiscount | 1.0.0 | - | - | - | - |
| All Quests Checkmarks | 1.4.0 | - | - | 1.4.0 | 1.4.0 |
| Artem | 3.0.2 | - | - | - | - |
| Auto Keycard | - | - | - | 1.0.1 | 1.0.1 |
| BALL - BetterAmmoLoadingList | - | - | - | 1.1.1 | 1.1.1 |
| BetterBepInEx | - | - | - | 1.0.2 | 1.0.2 |
| BetterKeysNG | 1.8.0 | - | - | - | - |
| BetterSuppressors | 1.0.0 | - | - | - | - |
| BlackDiv | 1.3.1 | 1.3.1 | 1.3.1 | 1.3.1 | 1.3.1 |
| BlackHawk-QuickMeds | - | - | - | 1.2.0 | 1.2.0 |
| Bosses Have GP Coins | 1.1.0 | - | - | - | - |
| Bosses Have Lega Medals | 2.1.0 | - | - | - | - |
| Bot Callsigns | 2.0.5 | - | - | - | - |
| BtrInteriorVolume | - | - | - | 1.0.0 @30dd1eb | 1.0.0 @30dd1eb |
| ColorConverterAPI | - | 1.2.0 | 1.2.0 | 1.2.0 | 1.2.0 |
| Content Backport - Prestiges | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 |
| Continuous Load Ammo | - | - | - | 1.2.0 | 1.2.0 |
| ContinuousHealing | - | - | - | 1.6.3 | 1.6.3 |
| CSGas | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 |
| DecalLockFix | - | - | - | 1.0.0 @2b0f28c | 1.0.0 @2b0f28c |
| Default Firemode Fixer | 2.1.0 | - | - | - | - |
| DrakiaXYZ-BigBrain | - | 1.5.0 | 1.5.0 | 1.5.0 | 1.5.0 |
| DrakiaXYZ-QuestTracker | - | - | - | 1.7.0 | 1.7.0 |
| DrakiaXYZ-TaskListFixes | - | - | - | 1.8.0 | 1.8.0 |
| DrakiaXYZ-Waypoints | - | 1.9.0 | 1.9.0 | 1.9.0 | 1.9.0 |
| Dynamic Maps | 1.2.1 | - | - | 1.2.1 | 1.2.1 |
| Expanded Task Text | 2.1.0 | - | - | - | - |
| ExtraSpecialSlots | 1.1.0 | - | - | 1.1.0 | 1.1.1 |
| FactoryClassic | 1.2.0 | 1.2.0 | 1.2.0 | 1.2.0 | 1.2.0 |
| Fast Grenade Throw | - | - | - | 2.0.0 | 2.0.0 |
| Fika | 2.4.2 | 2.4.6 | 2.4.6 | 2.4.6 | 2.4.6 |
| Fika.Headless | - | 1.5.3 | 1.5.3 | - | - |
| FixPluginTypesSerialization | - | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 |
| FleaCategoryBlock | 1.0.0 | - | - | 1.1.0 @9233aa6 | 1.1.0 @9233aa6 |
| FleaOfferGuard | 1.0.0 | - | - | 1.0.0 @cb87b6a | 1.0.0 @cb87b6a |
| Gilded Key Storage | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 |
| Gunsmith Trader | 1.0.2 | - | - | - | - |
| Halloween2026 backport | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 | 1.0.0 |
| HandsAreNotBusy | - | - | - | 1.7.1 | 1.7.1 |
| Harry Hideout | 3.1.2 | - | - | - | - |
| HeadlessGuard | - | - | - | 1.0.0 @a885272 | 1.0.0 @a885272 |
| Hideout In Progress | 3.0.0 | - | - | 3.0.0 | 3.0.0 |
| Hideout We Deserve - TimeRP | 1.4.0 | - | - | - | - |
| Increase Climb Height | 2.1.1 | - | - | - | - |
| InfectedVariants | 1.3.0 | 1.3.0 | 1.3.0 | 1.3.0 @e5637cf | 1.3.0 @e5637cf |
| Instant Insurance | 1.1.2 | - | - | - | - |
| InstantTraderDialogs | - | - | - | 1.0.0 @c524d12 | 1.0.0 @c524d12 |
| IOF: Inventory Organizing Feature | - | - | - | 1.10.1 | 1.10.1 |
| ISB Aishi | 2.0.4 | 2.0.4 | 2.0.4 | 2.0.4 | 2.0.4 |
| ISB Special Forces | 2.0.1 | 2.0.1 | 2.0.1 | 2.0.1 | 2.0.1 |
| Item Limits Begone | 1.1.0 | - | - | - | - |
| Item Valuation | 2.1.0 | - | - | - | - |
| ItemTweaks | 1.0.0 | - | - | 1.0.0 @77d43c8 | 1.0.0 @77d43c8 |
| JoinInsurance | - | - | - | 1.0.0 @2b0f28c | 1.0.0 @2b0f28c |
| Kaeno-TraderScrolling | - | - | - | 4.0.0 | 4.0.0 |
| KeepMeAlive | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 |
| Lacyway's PvE Tweaks | 1.3.2 | - | - | - | - |
| Lawnmower | - | - | - | 1.1.0 | 1.1.0 |
| Ledger | 1.1.0 | - | - | - | - |
| Live Flea Prices | 2.1.0 | - | - | - | - |
| LoadSpeedFix | - | - | - | 1.0.0 @2b0f28c | 1.0.0 @2b0f28c |
| Lotus | 1.8.1 | - | - | - | - |
| MagTopUp | - | - | - | 1.1.0 @b6f8784 | 1.1.0 @b6f8784 |
| ManimalIcebreaker | 1.1.3 | 1.1.3 | 1.1.3 | 1.1.3 | 1.1.3 |
| ManimalInterchangeRework | 1.0.8 | 1.0.8 | 1.0.8 | 1.0.8 | 1.0.8 |
| ManimalLabsBoiler | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 | 2.0.0 |
| ManimalLighthouseRework | 1.0.7 | 1.0.7 | 1.0.7 | 1.0.7 | 1.0.7 |
| MapVariants | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 | 1.1.0 |
| maschine-TraderSearch | - | - | - | 2.1.0 | 2.1.0 |
| MoreBots | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 | 2.1.1 |
| MoreCases | 3.0.0 | - | - | - | - |
| NATO Quartermaster | 1.1.3 | - | - | - | - |
| ORBIT | 2.1.1 | 2.1.1/1.1.0 | 2.1.1/1.1.0 | 2.1.1/1.1.0 | 2.1.1/1.1.0 |
| PastPresentEventSelector | 1.7.7 | 1.7.7 | 1.7.7 | 1.7.7 | 1.7.7 |
| PlayerEncumbranceBar | - | - | - | 1.3.0 | 1.3.0 |
| PrestigeDogtagFix | 1.0.0 | - | - | - | - |
| PrestigeTransferFilledContainers | - | - | - | 1.0.0 @67a3e11 | 1.0.0 @67a3e11 |
| PrestigeTweaks | 1.0.0 | - | - | - | - |
| QuestCodex | 1.11.1 | - | - | - | - |
| QuickSellFlea | - | - | - | 1.4.5 | 1.4.5 |
| QuietWeather | - | - | - | 1.0.1 | 1.0.1 |
| Raid Review | 1.8.0 | - | - | - | - |
| Ref Extra Quests | 1.9.0 | - | - | - | - |
| Ref Friendly Quests | 2.1.0 | - | - | - | - |
| RememberInsurer | - | - | - | 1.0.0 @7ddbd79 | 1.0.0 @7ddbd79 |
| RoguesVRaiders | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 | 1.7.0 |
| SAIN | 4.5.1 | 4.5.1 | 4.5.1 | 4.5.1 | 4.5.1 |
| ScopeZoomPresets | - | - | - | 1.0.0 @9b1fce8 | 1.0.0 @9b1fce8 |
| Scorpion | 1.1.1 | - | - | - | - |
| SearchSpeed | - | - | - | 1.0.0 @0858d00 | 1.0.0 @0858d00 |
| sgtlaggy's Quest Tweaks | 4.9.0 | - | - | - | - |
| Shinni's 1.0 QuestsBackport | 0.3.2 | - | - | - | - |
| SitOnMyFace | 0.1.2 | - | - | - | - |
| SmajlecStamina | - | - | - | 1.4.0 | 1.4.0 |
| SPT core plugins | - | 18.4/4.1.6 | 18.4/4.1.6 | 18.4/4.1.6 | 18.4/4.1.6 |
| StorylineCleanup | 1.0.0 | - | - | - | - |
| SVM | 2.2.3 | - | - | - | - |
| tarkin ladders | - | 1.0.4/1.0.1 | 1.0.4/1.0.1 | 1.0.4/1.0.1 | 1.0.4/1.0.1 |
| Terkoiz.FlareEventNotifier | - | - | - | 1.0.6 | 1.0.6 |
| Terkoiz.Skipper | - | - | - | 1.1.5 | 1.1.5 |
| TerminalCommander | 1.2.0 | - | - | - | - |
| The Blacklist | 3.1.1 | - | - | - | - |
| TraderTweaks | - | - | - | 1.0.0 @a8e18ad | 1.0.0 @a8e18ad |
| Tyfon.AutoDeposit | - | - | - | 6.0.0 | 6.0.0 |
| UI Fixes | 6.0.4 | 6.0.4 | 6.0.4 | 6.0.4 | 6.0.4 |
| UnityToolkit | - | 2.0.2/6.1.2 | 2.0.2/6.1.2 | 2.0.2/6.1.2 | 2.0.2/6.1.2 |
| UNTARGH | 3.2.1 | 3.2.1 | 3.2.1 | 3.2.1 | 3.2.1 |
| Use Items Anywhere | - | 2.1.6/1.0.0 | 2.1.6/1.0.0 | 2.1.6/1.0.0 | 2.1.6/1.0.0 |
| UseItemsAnywhereTweaks | - | - | - | 5.1.0 @0486e42 | 5.1.0 @0486e42 |
| VisitAPI | 1.3.7 | 1.3.7 | 1.3.7 | 1.3.7 | 1.3.7 |
| VisitButtonFix | - | - | - | 1.0.0 @ab58b37 | 1.0.0 @ab58b37 |
| Wolfiks Heavy Troopers | 1.2.0 | - | - | - | - |
| WTT CommonLib | 3.0.8 | 3.0.8 | 3.0.8 | 3.0.8 | 3.0.8 |
| WTT-Armory | 2.0.5 | 2.0.5 | 2.0.5 | 2.0.5 | 2.0.5 |
| WTT-ContentBackport | 2.0.7 | 2.0.7 | 2.0.7 | 2.0.7 | 2.0.7 |

## Attention

- `!` missing: ACCESS GRANTED on tarmantria
- `!` missing: Anvil-WebOverlay on tarmantria
- `!` missing: BackpackGunSlot on tarmantria
- `!` missing: Briefing on tarmantria
- `!` missing: Cabinet Looter on tarmantria
- `!` missing: CraftQueue on tarmantria
- `!` missing: Hideout We Deserve - UI Improvements on tarmantria
- `!` missing: IncreaseLookDirection on tarmantria
- `!` missing: LMSP on tarmantria
- `!` missing: Magazine Check Interrupt on tarmantria
- `!` missing: Manimal's Quick Cancel on tarmantria
- `!` missing: ManimalTerminal on tarmantria
- `!` missing: maschine-ModProfiler on tarmantria
- `!` missing: maschine-RaidMenuCleanupFix on tarmantria
- `!` missing: maschine-WeaponBuilderSearch on tarmantria
- `!` missing: More Tag Colours on tarmantria
- `!` missing: No Barrel Shadow on tarmantria
- `!` missing: No Scav Select on tarmantria
- `!` missing: Plate Durability on tarmantria
- `!` missing: PressurePads on tarmantria
- `!` missing: ReceiveAllChats on tarmantria
- `!` missing: RUAFComeHome on tarmantria
- `!` missing: Self Breath Volume on tarmantria
- `!` missing: Sicherheitsbehaelter ohne Gewicht on tarmantria
- `!` missing: Smoothie on tarmantria
- `!` missing: SmoothTalker on tarmantria
- `!` missing: StanceSync on tarmantria
- `!` missing: StatTrack™ on tarmantria
- `!` missing: VisorEffectManager on tarmantria

## Pending archives and backups

| Where | Path |
|---|---|
| Install archives, tarmantria | `~/Games/spt/pending-server-mods` |
| Install archives, taragarmr | `~/spt-pending-mods` |
| Backups, taragarmr | `~/spt-mod-backups/2026-11-15` |
| Backups, tarmantria | `~/Games/spt/backups` |
