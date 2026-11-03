# InfectedVariants4Server

**Requires:** SPT 4.1.6 server. Hard dependencies (the server refuses to load this mod without them): MapVariants `>=1.1.0 <2.0.0` (`com.lennoxp90.mapvariants`) and FactoryClassic `>=1.2.0 <2.0.0` (`com.lennoxp90.factoryclassic`). PastPresentEventSelector (PPES) with Halloween on, its "2026 outbreak" on and `halloween.labInfectedOnly` set to false (otherwise SPT wipes the vanilla Labs bosses at startup; a warning is logged if that happened). Optional: ManimalInterchange (server mod); Infected Interchange exists only when it is installed, and is skipped quietly otherwise. Pair with the client plugin `InfectedVariants`. Server-only; Fika 2.4.6 unaffected.

## What
Zombies spawn only on three maps: Factory "Halloween 2026" (FactoryClassic variant, day and night), an "Infected" variant of Labs, and an "Infected" variant of Interchange (ManimalInterchange's map plus zombies). Everywhere else they are removed: vanilla and classic Factory, vanilla Labs, and every other map (including the Manimal maps).

## Why
PPES's 2026 outbreak infects every map. This keeps the event to the maps it is meant for, selectable per raid in the MapVariants map screen.

## How
- PostLoad: hands Labs to MapVariants as a pair (backport = "Vanilla", the live Labs with infection 0 and no infected bosses; original = "Infected": same loot/extract/static data by reference, own Base copy with infection and infected bosses). Runs before MapVariants' discovery.
- PostLoad, only with ManimalInterchange installed: declares the variant id `infected` to MapVariants (`DeclareVariant`). MapVariants itself discovers Interchange (backport = Manimal's data) because ManimalInterchange replaces it; the declared variant serves that same Manimal data, and this mod adds the zombies per raid (boss entries from SPT's `halloweenzombies` group for `interchange`). Interchange is infected only while MapVariants' installed variant is `infected`; Manimal stays clean.
- Per raid: a Harmony postfix (lowest priority, so after PPES) on `LocationLifecycleService.GenerateLocationAndLoot` reads MapVariants' installed variant for the map and sets the raid copy's `Events.Halloween2024.InfectionPercentage` to the configured value on infected variants, 0 elsewhere. It removes infected bosses, the event's zombie-group scav entries and infected waves from non-infected raids, and adds the zombie boss entries on infected ones. Classic and Halloween 2026 Factory share one Base, which is why this happens on the per-raid copy.
- PostLoad + 6000000 (after PPES): clears the table Bases the same way (except the shared Factory Base), and sets the globals `LocationInfection` badge to the configured value for Factory, Labs and (when enabled) Interchange, 0 elsewhere. `SeasonActivity.InfectionHalloween` is left as PPES/SPT set it.
- If MapVariants cannot be reached or refuses the Labs pair, nothing is infected anywhere (fails safe) and an error is logged.

MapVariants rests on and defaults to the backport, so a Labs raid with no choice recorded, a transit and the raid-end reset all land on Vanilla; on Interchange they land on Manimal. Infected Labs is "original" in MapVariants terms; if Labs registration fails it is treated as not infected.

## Build and deploy
`nix shell nixpkgs#dotnet-sdk_10 -c dotnet build -c Release`, then `scripts/deploy-server-mod.sh InfectedVariants4Server`. Takes effect on the next server restart. Edit `config.json` in the server's mod folder (`krane-InfectedVariants4Server/`); deploy never overwrites it.

## Settings
| Key | Default | Effect |
|---|---|---|
| InfectedLabsOnlyInfected | `true` | On the Infected Labs raid copy, remove every non-infected boss (mirrors the live infected Lab). `false` keeps the vanilla bosses there too. |
| InfectionPercentage | `100` | Infection of the infected variants (Factory Halloween 2026, Labs Infected, Interchange Infected), 0-100. 0 means no zombies anywhere: infected variants get no zombie boss entries either. Also the map-screen badge value. Missing or non-number logs a warning and uses 100. |
