# MagTopUp

**Requires:** SPT 4.1.6. No other mods. Fika-compatible (2.4.6; uses Fika's load wrapper in raid). Works alongside UI Fixes (its own top-up key keeps working).

## What
Press a key while hovering a magazine in the inventory or stash screen to fill it from loose ammo. Only examined, non-full magazines that are not in a weapon. Ammo choice:
1. Same type as the magazine's top round.
2. Empty magazine: top round of another magazine of the same model, then same caliber.
3. Else the strongest ammo (penetration, then damage) for the weapon the magazine fits.

Loading goes through the normal game load, the same as dragging ammo onto the magazine. In raid that means the timed load with Mag Drills XP. Press the key again to cancel.

## Why
Filling magazines by dragging stack after stack is tedious.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Top Up Magazine | `T` | Fill the hovered magazine. Press again to cancel. |
