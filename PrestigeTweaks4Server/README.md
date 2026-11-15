# PrestigeTweaks4Server

**Requires:** SPT 4.1.6 server. Server-only; Fika 2.4.6 compatible and unaffected. Soft dependencies: SVM (Server Value Modifier), whose prestige height/skills/mastery/filter settings are what gets copied (without it the vanilla prestige 4 values are copied), and archangelwtf-contentbackportprestiges (without it there is no prestige 5-6, so nothing to do).

## What
At server load, copies prestige 4's transfer settings to every later prestige element (prestige 5 and 6 from the backport): stash width (`XCellCount`), the item filter (included and excluded items, as independent copies), and the skill and mastery transfer multipliers. Stash height (`YCellCount`), rewards, conditions and everything else of those elements stay untouched. If fewer than 5 prestige elements exist, nothing happens. One info line logs the copied element indices, width, filter size and multipliers.

## Why
SVM applies its prestige settings only to prestige elements 1-4. The prestige backport adds prestige 5 and 6 with vanilla values, so SVM's settings never reached them.

## Load order
The backport runs at priority 100003 and SVM at 100005 / 100040 (`SVMPostLoad`). This mod runs at `OnLoadOrder.PostLoad + 100` (1000100), after both.

## Settings
`config.json` next to the DLL.

| Section / key | Default | Effect |
|---|---|---|
| `Enabled` | `true` | `false` skips the copy entirely. |
