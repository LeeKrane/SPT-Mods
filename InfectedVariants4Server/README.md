# InfectedVariants4Server

**Requires:** SPT 4.1.6 server. Hard dependencies (the server refuses to load this mod without them): MapVariants `>=1.1.0 <2.0.0` (`com.lennoxp90.mapvariants`) and FactoryClassic `>=1.2.0 <2.0.0` (`com.lennoxp90.factoryclassic`). PastPresentEventSelector (PPES) with Halloween on, its "2026 outbreak" on and `halloween.labInfectedOnly` set to false (otherwise SPT wipes the vanilla Labs bosses at startup; a warning is logged if that happened). Optional: backport server mods such as ManimalInterchange and ManimalLighthouse (any mod that replaces a stock map's location data is detected and gets the same treatment). Pair with the client plugin `InfectedVariants`. Server-only; Fika 2.4.6 unaffected.

## What
Zombies spawn only on Factory "Halloween 2026" (FactoryClassic variant, day and night) and on an "Infected" variant of each enabled stock map: Customs (`bigmap`), Woods, Shoreline, Interchange, Lighthouse, Reserve (`rezervbase`), Streets (`tarkovstreets`), Ground Zero (`sandbox` and `sandbox_high`) and Labs (`laboratory`). Everywhere else they are removed: vanilla and classic Factory, every non-infected variant (the Manimal ones included) and every map not listed. Not covered: Terminal (ManimalTerminal), Icebreaker (ManimalIcebreaker), Labyrinth (disabled in SPT 4.1.6) and the non-raid locations.

## Why
PPES's 2026 outbreak infects every map. This keeps the event to the variants you pick per raid in the MapVariants map screen.

## How
- PostLoad: for each enabled map the mod asks MapVariants' registry whether a mod already replaced the location data.
  - Unmodded map: registers a pair with MapVariants whose two Locations share all seven fields by reference (distinct objects only; MapVariants' `RegisterProvider` does no "changed = backport" check), then declares `infected` on it exactly as for a replaced map. Nothing is cloned, so everything other mods do to the live data applies to both choices. Runs before MapVariants' discovery.
  - Replaced map ("declared", e.g. Interchange and Lighthouse with Manimal): declares the variant id `infected` to MapVariants. MapVariants discovers the map itself (backport = the mod's data); the declared variant serves that same data and this mod adds the zombies per raid. The mod's own data stays the only data, so everything other mods do to it applies to both choices. If the registry cannot be inspected, ManimalInterchange / ManimalLighthouse being loaded is the fallback test.
  - A map another provider already registered is skipped with a warning.
- Per raid: a Harmony postfix (lowest priority, so after PPES) on `LocationLifecycleService.GenerateLocationAndLoot` runs on SPT's deep-copied raid Base (`cloner.Clone(location.Base)`), so edits never reach the shared table Base; it reads MapVariants' installed variant for the map and sets the raid copy's `Events.Halloween2024.InfectionPercentage` to the configured value on infected variants, 0 elsewhere. It removes infected bosses, the event's zombie-group scav entries and infected waves from non-infected raids, and adds the zombie boss entries on infected ones. Entries come from SPT's `halloweenzombies` group for the map; a map with none borrows the Shoreline (else Woods, Customs) entries with open zones and a warning is logged (no stock map needs this today).
- PostLoad + 6000000 (after PPES): clears the table Bases the same way (except the shared Factory Base), and sets the globals `LocationInfection` badge to the configured value for Factory and every enabled map, 0 elsewhere. `SeasonActivity.InfectionHalloween` is left as PPES/SPT set it.
- `GET /infectedvariants/maps`: tells the client plugin which maps to register and how. The client registers only what the server set up, so a map missing from `EnabledMaps` has no Infected tile.
- If MapVariants cannot be reached or refuses a map, that map is not infected (fails safe) and an error is logged.

MapVariants rests on and defaults to the backport, so a raid with no choice recorded, a transit and the raid-end reset all land on Vanilla (Manimal on Interchange and Lighthouse).

## Build and deploy
`nix shell nixpkgs#dotnet-sdk_10 -c dotnet build -c Release`, then `scripts/deploy-server-mod.sh InfectedVariants4Server`. Takes effect on the next server restart. Edit `config.json` in the server's mod folder (`krane-InfectedVariants4Server/`); deploy never overwrites it; a 1.1 `config.json` has no `EnabledMaps`, which means every map, so add it there to limit the maps.

## Settings
| Key | Default | Effect |
|---|---|---|
| InfectionPercentage | `100` | Infection of the infected variants, 0-100. 0 means no zombies anywhere: infected variants get no zombie boss entries either. Also the map-screen badge value. Missing or non-number logs a warning and uses 100. |
| EnabledMaps | all nine keys | Maps that get an Infected variant: `bigmap`, `woods`, `shoreline`, `interchange`, `lighthouse`, `rezervbase`, `tarkovstreets`, `sandbox` (also covers `sandbox_high`), `laboratory`. Missing means all; `[]` means none. Unlisted maps stay vanilla (zero zombies). Factory is FactoryClassic's variant and not switchable here. |
| InfectedLabsOnlyInfected | `true` | On the Infected Labs raid copy, remove every non-infected boss (mirrors the live infected Lab). `false` keeps the vanilla bosses there too. |
