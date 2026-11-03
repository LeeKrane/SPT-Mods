# InfectedVariants

**Requires:** SPT 4.1.6, Fika 2.4.6 compatible (headless safe: nothing registers on a client that never opens the map screen, and nothing here swaps on the client). Hard dependencies: MapVariants 1.1.0 or later (`com.lennoxp90.mapvariants`) and FactoryClassic 1.2.0 or later (Factory's Halloween 2026 variant is another infected map). Needs the server mod `InfectedVariants4Server` (it decides which maps are offered), and PPES with Halloween and the 2026 outbreak on and `labInfectedOnly` off. MapVariants' `maps.json` must have no entry for any map offered here (the plugin logs an error and skips such a map). ManimalInterchange / ManimalLighthouse (`com.manimal.interchange` / `com.manimal.lighthouse`) are needed on the client only if the server has them: their patches stay active for both choices.

## What
Adds an Infected choice to the MapVariants map prompt on every stock raidable map the server enables: Customs, Woods, Shoreline, Interchange, Lighthouse, Reserve, Streets of Tarkov, Ground Zero (Sandbox and Sandbox_high are one choice) and Labs. Unmodded maps offer Vanilla (default) / Infected (no original tile either). Maps with a backport mod (Interchange and Lighthouse with Manimal) offer Manimal (default) / Infected, with no Vanilla tile. Zombies spawn only on an Infected choice and on Factory's Infected choice. FactoryClassic's "Halloween 2026" variant is renamed to "Infected" everywhere players see it (prompt tile, loading screen label, F12 `Factory` > `DefaultSelection`; a stored "Halloween 2026" default becomes "Infected").

## Why
The server mod keeps the Halloween outbreak off every map by default; this registers the choices on the client so you can pick them per raid.

## How
The plugin asks the server (`GET /infectedvariants/maps`) which maps to offer, when the map screen is shown (retried on each showing, up to 10 times, until the server answers; one synchronous request per try), and registers them with MapVariants then, before its Next-button prompt (logged as `Registered with MapVariants: ...`). A backport-based map is registered as a MapVariants provider, so the backport mod's own patches stay live for both choices; its "original" tile is removed from MapVariants' prompt by two small Harmony prefixes (matched by the map's location id, not by title; if they fail a warning is logged and the tile shows). Ground Zero's two ids are one choice; a mixed-level group that the game moves from Sandbox to Sandbox_high after the prompt gets the server's answer for Sandbox_high (Vanilla unless someone chose it there).

The rename is done once at startup by reflection on MapVariants' registered Factory entry (FactoryClassic's files are untouched); if that layout ever changes a warning is logged and the old name stays.

## Settings
None here. The choice and its default are MapVariants' own (F12, one section per map); the infection level and the list of enabled maps are in the server mod's `config.json`.
