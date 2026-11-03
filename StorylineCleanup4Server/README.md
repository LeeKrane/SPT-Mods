# StorylineCleanup4Server

**Requires:** SPT 4.1.6 server. Server-only; Fika 2.4.6 compatible and unaffected. Hard dependencies: VisitAPI server mod and Storyline Backport (TricolourLM7, its EFT11 content pack). Recommended: Manimal's Terminal Backport (supplies the replacement Falling Skies and The Ticket quests).

## What
After VisitAPI has registered its pack quests, removes the configured quests from the quest database (by default the 17 Falling Skies quests of Storyline Backport) and strips every quest condition of type `Quest` in the remaining quests that targets a removed quest (for example the Tour quest's finish condition on a Falling Skies quest). If `RemapQuestIds` maps a removed id to an existing quest, such conditions are retargeted to the replacement instead (status list kept; list targets get the id replaced, deduplicated). Otherwise a quest gated only on a removed quest becomes available without that prerequisite; list targets only lose the removed ids and the condition is dropped when its list empties. A warning names any quest whose `AvailableForFinish` became empty through this. Also prunes the removed ids from VisitAPI's static pack-quest set. One info line logs how many quests were removed and conditions removed / remapped.

Not touched: items, locales, quest zones and loose loot of the chapter (VisitAPI serves those from its pack files), and trader quest-assort unlocks tied to the removed quests (those items stay locked). Profiles are not edited by this mod, but SPT's profile fixer drops quest entries without a database template on login, so progress on removed quests is lost for good even if the mod is later removed. VisitAPI keeps no own quest registry; its `autoStart` / `autoFinish` / note-link flags are read from the quests in the database, so removed quests cannot auto-start.

## Why
Manimal's Terminal Backport ships its own Falling Skies and The Ticket chain, so the Storyline Backport Falling Skies chapter duplicates it.

## Settings
`config.json` (the deployed copy is never overwritten by `deploy-server-mod.sh`).

| Section / key | Default | Effect |
|---|---|---|
| `RemoveQuestIds` | the 17 Falling Skies quest ids from `FallingSkies_1.1.json` | Quest ids removed from the database. Unknown ids are ignored and counted in the log line. |
| `RemapQuestIds` | `68cc07e96a6359b02109da18` (Falling Skies chapter quest) -> `6a8f4d5e90206485fa1d2d5a` (Manimal Terminal's Falling Skies - Part 1) | Removed quest id -> replacement quest id. Conditions on the removed id are retargeted to the replacement (must exist in the database, else they are removed as usual). Keeps the Tour quest's finish condition valid. |
