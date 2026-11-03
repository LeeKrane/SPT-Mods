# InfectedVariants

**Requires:** SPT 4.1.6, Fika 2.4.6 compatible (headless safe: registration plus a prefix on MapVariants' prompt window, which never opens on a headless client). Hard dependencies: MapVariants 1.1.0 or later (`com.lennoxp90.mapvariants`) and FactoryClassic 1.2.0 or later (Factory's Halloween 2026 variant is another infected map). Soft dependency: ManimalInterchange (`com.manimal.interchange`); Interchange is offered only when it is installed (the Labs choice does not need it). MapVariants' `maps.json` must have no `interchange` entry. Needs the server mod `InfectedVariants4Server`, and PPES with Halloween and the 2026 outbreak on and `labInfectedOnly` off.

## What
Adds Labs to the MapVariants map screen with two choices: Vanilla and Infected. With ManimalInterchange installed it also adds Interchange with Manimal (default) and Infected (Manimal's Interchange plus zombies); there is no Vanilla Interchange tile. Zombies spawn only on Infected Labs, Infected Interchange and Factory Halloween 2026.

## Why
The server mod keeps the Halloween outbreak off all other maps; this registers the Labs and Interchange choices on the client so you can pick them per raid. Interchange is registered as a MapVariants provider, so ManimalInterchange's own patches stay active for both choices; the Vanilla tile is removed from MapVariants' prompt by a small Harmony prefix on its window (if that fails, a warning is logged and the tile shows).

## Settings
None here. The choice and its default are MapVariants' own (F12, Labs section); the infection level is `InfectionPercentage` in the server mod's `config.json`. Default is Vanilla for Labs and Manimal for Interchange (F12, Interchange section).
