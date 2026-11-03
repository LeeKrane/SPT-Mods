# InfectedVariants

**Requires:** SPT 4.1.6, Fika 2.4.6 compatible (headless safe: registration only, no UI calls). Hard dependencies: MapVariants 1.1.0 or later (`com.lennoxp90.mapvariants`) and FactoryClassic 1.2.0 or later (Factory's Halloween 2026 variant is the other infected map). Needs the server mod `InfectedVariants4Server`, and PPES with Halloween and the 2026 outbreak on and `labInfectedOnly` off.

## What
Adds Labs to the MapVariants map screen with two choices: Vanilla and Infected. Zombies spawn only on Infected Labs and on Factory Halloween 2026.

## Why
The server mod keeps the Halloween outbreak off all other maps; this registers the Labs choice on the client so you can pick it per raid.

## Settings
None here. The choice and its default are MapVariants' own (F12, Labs section); the infection level is `InfectionPercentage` in the server mod's `config.json`. Default is Vanilla.
