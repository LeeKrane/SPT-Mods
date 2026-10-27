# MagTopUp

**Requires:** SPT 4.1.6. No other mods. Fika-compatible (2.4.6; uses Fika's load wrapper in raid). Works alongside UI Fixes (its own top-up key keeps working).

## What
Press a key while hovering a magazine in the inventory or stash screen to fill it from loose ammo. Only examined, non-full magazines that are not in a weapon. Ammo choice:
1. Same type as the magazine's top round.
2. Empty magazine: top round of another magazine of the same model, then same caliber.
3. Else the strongest ammo (penetration, then damage) for the weapon the magazine fits.

Fill under (default on): if there is not enough of the picked ammo to fill the magazine, the gap is filled with other ammo of the same caliber. Weaker types (and types of equal strength) go first, closest in strength first. If still short, stronger types follow, closest first. Load order, bottom to top: weaker ammo (weakest lowest), picked ammo, stronger ammo. Result: a full magazine with the picked ammo above all weaker rounds. Rounds already in the magazine stay at the bottom. If total ammo cannot fill the magazine, everything available is loaded in that order. The pick is remembered per magazine until the game restarts. Pressing the key again keeps it while the magazine's top round is a type from that run, so a cancelled or partly fired magazine does not switch its pick to the filler.

Loading goes through the normal game load, the same as dragging ammo onto the magazine. In raid that means the timed load with Mag Drills XP. Press the key again to cancel.

## Why
Filling magazines by dragging stack after stack is tedious.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Top Up Magazine | `T` | Fill the hovered magazine. Press again to cancel. |
| General / Fill Under Picked Ammo | `true` | Fill the gap with weaker ammo under the picked ammo (then stronger above it), so the magazine ends full. Off: load the picked ammo, then pick again until full. |
