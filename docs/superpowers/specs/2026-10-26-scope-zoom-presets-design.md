# ScopeZoomPresets design

Client BepInEx plugin, `ScopeZoomPresets/out/krane.ScopeZoomPresets.dll`. Target: SPT 4.1.6, Fika 2.4.6. No hard dependencies.

## Goal

The "change scope magnification" hotkey (`EGameKey.ChangeAimScopeMagnification`) on adjustable (variable zoom) optics toggles between two user-chosen points instead of the native extremes, e.g. 15% and 70% of a 1x-6x Razor instead of 1x and 6x. Settings are per scope group. The HUD magnification label shows on every zoom change with a percentage and an approximate magnification.

## Decisions from the interview

- Groups are a fixed list, keyed by native label range. Scopes with an unlisted range get no toggle override (HUD label still applies).
- A scope's group comes from its zoom data (`Zooms`), not its name.
- Values are entered as percent of the scope's own FOV range, not as magnification.
- Scroll / Alt+wheel free zoom is untouched and keeps the full native range.
- Hotkey from an in-between zoom: jump to whichever configured point is farther from the current zoom (same rule vanilla uses with its native edges).
- At raid start, scopes in enabled groups start at the configured min. Nothing else is remembered between raids.
- Each group has an Enabled toggle, off by default. Disabled groups are fully vanilla, including no raid-start reset.
- HUD label applies to all adjustable scopes, including disabled groups and unlisted ranges.

## Vanilla mechanics (from decompiled Assembly-CSharp)

- Hotkey chain: `FirearmHandsInputTranslator.TranslateCommand` -> `ClientFirearmController.SetScopeMode` -> `Player.FirearmController.SetScopeMode` -> `Firearms.UpdateScopesMode` -> `SightModVisualControllers.ForceChangeScopeState` -> `ScopeZoomHandler.ForceChangeScopeState()`.
- `ScopeZoomHandler.ForceChangeScopeState()` compares `_filedOfView` to `MinFov` and `MaxFov` and jumps to the farther one (tie goes to `MaxFov`). It then sets `float_2`, `float_4`, `_lastEdgeSmoothScopeState`, `_sightComponent.ScopeZoomValue`, and calls `method_11()` and `method_9()`.
- Naming is inverted: `MinFov = MinMaxFov.x` is the widest FOV (lowest zoom), `MaxFov = MinMaxFov.y` is the narrowest FOV (highest zoom).
- Zoom is stored as FOV in degrees in `SightComponent.ScopeZoomValue` (serialized with the item). Any FOV in `[MaxFov, MinFov]` is valid. `ScopeZoomHandler.Init(SightComponent)` reads and clamps it.
- Scroll zoom runs in `ScopeZoomHandler.method_4()` and writes `ScopeZoomValue` each frame it changes.
- HUD: `BattleUIScreen.SmoothSightChanged(SightComponent, ESmoothScopeState)` shows `x{GetMinOpticZoom()}` or `x{GetMaxOpticZoom()}` on `_opticCratePanel` at the edges and hides it for `SmoothValue`. It fires only when the smooth state flips.
- Network: zoom FOV is never sent. `SightsModePacket` carries mode, scope index and calibration only. A local-only change is Fika-safe.

## Groups

Group key is the min and max of the selected scope's `Zooms` entry, rounded to 0.1, ordered low to high (Vudu `[[6, 1]]` is 1x-6x). Only items with `IsAdjustableOptic`.

| Group | Known scopes |
|---|---|
| 1x-4x | Burris Tac30 |
| 1x-6x | Razor HD Gen 2, Tango6T, EOTech Vudu, ISB Razor Black/FDE, ISB Tango, ISB Vudu, ISB PM II 1-8 Black (data says 1-6) |
| 1x-8x | S&B PM II 1-8, WTT DEON March-F 1-8 |
| 2.5x-8x | WTT Leupold Mark 4 MR/T |
| 3x-9x | NcSTAR ADO |
| 3x-10x | 1P69, 1P59 |
| 3x-20x | S&B PM II 3-12, ISB PM II 3-20 |
| 3x-24x | March Tactical |
| 4x-16x | Hensoldt FF |
| 5x-25x | S&B PM II 5-25, Leupold Mk5 HD, ISB PM II 5-25 |
| 6.5x-20x | Leupold Mk4 LR |
| 7x-35x | Nightforce ATACR |

## Percent mapping

`fov(p) = Lerp(MinFov, MaxFov, p / 100)` using the scope's own `MinMaxFov`. 0% is the native low-zoom edge, 100% the native high-zoom edge. Inverse: `p = InverseLerp(MinFov, MaxFov, fov) * 100`. This matches the game's own `ZoomFactor()`.

Approximate magnification for the HUD: `zoom ≈ minLabel * tan(MinFov / 2) / tan(fov / 2)`, angles in degrees converted to radians, `minLabel` = low value of the scope's `Zooms` entry. The game has no such formula, so the label is marked approximate.

## Config

One section per group, named like `1x-6x`:

| Key | Default | Effect |
|---|---|---|
| Enabled | false | Applies this group's toggle points and raid-start reset |
| Min % | 0 | Low-zoom toggle point, 0-100, step 1 |
| Max % | 100 | High-zoom toggle point, 0-100, step 1 |

If Min % is above Max %, the two are swapped when used.

Section `HUD`:

| Key | Default | Effect |
|---|---|---|
| Show zoom label | true | Show `N% · ~Zx` on every zoom change for all adjustable scopes. Off: vanilla label |

## Components

- `Plugin` - BepInEx entry, binds config, applies Harmony patches.
- `ScopeGroups` - fixed group list, group lookup from a `SightComponent`, config entries per group.
- `ZoomMath` - percent to FOV, FOV to percent, approximate magnification.
- `TogglePatch` - prefix on `ScopeZoomHandler.ForceChangeScopeState()`.
- `RaidStartPatch` - postfix on `GameWorld.OnGameStarted()`.
- `HudPatch` - patches on `BattleUIScreen` and `ScopeZoomHandler` for the label.

## Behaviour

### Toggle

Prefix on `ScopeZoomHandler.ForceChangeScopeState()`. Runs custom logic only if the handler's sight belongs to the local player and its group is enabled; otherwise returns true (vanilla).

Ownership check: the `_sightComponent.Item` must be in `GameWorld.MainPlayer`'s inventory. Confirm the exact API during implementation; observed players also reach this method via `UpdateScopes()`.

Custom logic, with `lo = fov(Min %)`, `hi = fov(Max %)`, `f = _filedOfView`:
- If `|f - lo| > |f - hi|`, target `lo`, else target `hi` (tie goes to `hi`, like vanilla).
- Set `_filedOfView`, `float_2`, `float_4` and `_sightComponent.ScopeZoomValue` to the target.
- Set `_lastEdgeSmoothScopeState` as vanilla does for the matching side (target `lo` -> `ESmoothScopeState.Max`, target `hi` -> `ESmoothScopeState.Min`).
- Call `method_11()` and `method_9()`, return false.

Private fields are accessed via Harmony `AccessTools` field refs.

### Raid start

Postfix on `GameWorld.OnGameStarted()`. If `MainPlayer` is null, do nothing. For every `SightComponent` in the local player's equipment with `IsAdjustableOptic` and an enabled group, set `ScopeZoomValue = fov(Min %)`. `ScopeZoomHandler.Init` applies it when the weapon visuals initialise. If the weapon in hands has already initialised its handler, set the handler's `_filedOfView`, `float_2` and `float_4` too. Verify in game which case occurs.

### HUD label

Active when `HUD / Show zoom label` is true.
- Capture the `BattleUIScreen` instance (postfix on its show/init, or from the `SmoothSightChanged` call).
- Prefix on `BattleUIScreen.SmoothSightChanged`: if the sight is adjustable, skip vanilla (return false). Null sight and non-adjustable sights stay vanilla.
- Postfix on `ScopeZoomHandler.method_4()` (scroll) and `ForceChangeScopeState()` (hotkey), local player only: compute rounded percent from `_filedOfView`. If it differs from the last shown value for that handler, call `_opticCratePanel.Show($"{p}% · ~{zoom:0.0}x")` and `HideRaidTime()`, as vanilla does. The panel fades on its own timer.

### Startup log

At the first `GameWorld.OnGameStarted`, log every adjustable optic template once: id, name, `Zooms`, `MinMaxFov`, group (or "unlisted"). Used to confirm modded scopes, e.g. whether the ISB PM II 1-8 Black has 1-8 optics despite its 1-6 label.

## Error handling

- Missing private fields or methods at patch time: log an error and skip that patch, leave the game vanilla.
- Any exception inside a prefix: log it, return true (vanilla runs).
- `MinFov == MaxFov`: treat as non-adjustable, skip.

## Testing

No automated tests; BepInEx plugin against a game assembly. Manual checks in raid:
- Enabled 1x-6x group at 15%/70%: hotkey alternates between the two; after scrolling to the middle it jumps to the farther point.
- Disabled group: vanilla toggle between native edges, no raid-start reset.
- Raid start with an enabled group: scope starts at Min %.
- Scroll still reaches the native 0% and 100%.
- HUD label updates on scroll and hotkey for enabled, disabled and unlisted adjustable scopes; non-adjustable scopes show the vanilla label.
- Fika: a teammate sees no errors; observed players are unaffected.
- Startup log lists modded scopes with plausible groups.

## Deliverables

- `ScopeZoomPresets/` project matching the existing plugins (`Directory.Build.props`, csproj, `Plugin.cs`).
- `ScopeZoomPresets/README.md` with requirements, what it does, why, and settings table.
- Symlink into `~/Games/spt/drive_c/SPT/BepInEx/plugins`.
