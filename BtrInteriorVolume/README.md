# BtrInteriorVolume

**Requires:** SPT 4.1.6. Compatible with Fika 2.4.6 (not required). No other dependencies. Client-only.

## What
Adds a volume slider for the BTR as heard while you sit inside it. Scales the interior engine and movement loops, engine start and acceleration sounds, turret rotation and suspension impacts. The slider is active only while your own player boards, sits in or leaves the BTR. Outside it, sound stays vanilla, also when a Fika teammate boards. Changes apply at once, also mid-ride.

Not scaled: driver voice, departure horn, doors and radio.

## Why
The BTR interior is very loud, and the game has no setting for it.

## Settings
| Section / Key | Default | Effect |
|---|---|---|
| General / InteriorVolume | `0.5` | Interior BTR volume (0–1). `1` = vanilla, `0` = silent. |
