# LoadSpeedFix

**Requires:** SPT 4.1.6. Fika 2.4.6 optional (soft dependency; adds hooks around Fika's raid pool loads).

## What
Speeds up raid and menu loads, and logs how long each load phase takes:
- Raises loading-thread priority and uncaps FPS during loading screens (ref-counted, 10 min watchdog).
- Caches parsed `/client/items` templates so post-raid loads skip the fetch and parse.
- Turns off AggressiveGC for most collects, and fills the shader lookup in one frame.

Missing methods log a warning and are skipped, so a game update does not break loading.

## Why
Loads are slow, mostly because the game throttles itself while loading. BepInEx's log has no timestamps, so phases could not be measured.

## Settings
All under `General`. Every change needs a game restart.

| Key | Default | Effect |
|---|---|---|
| TimingLog | `true` | Log timestamped durations of load phases. |
| LoadThrottleLifter | `true` | Raise loading thread priority and uncap FPS while the map and raid pools load. |
| ItemTemplateCache | `true` | Keep parsed item templates in memory; skip refetch post-raid. |
| RetainBatch | `2000` | Bundle-graph visits per frame during loading holds. `0` = game default (55). |
| MenuLoadLifter | `true` | Also lift the throttle during menu data loads (first menu, every post-raid return). |
| LeanGC | `true` | Skip AggressiveGC except right before a raid. |
| FastShaderLookup | `true` | Fill the shader dictionary in one frame instead of one `Shader.Find` per frame. |
