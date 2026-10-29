# SearchSpeed

**Requires:** SPT 4.1.6. Compatible with Fika 2.4.6 (not required). No other dependencies. Client-only.

## What
Speeds up searching containers and bodies by a configurable multiplier (default 25% faster). Both the initial "Searching" delay and each item reveal are divided by the multiplier.

The multiplier applies after the game's own skill math, so Attention (loot speed, elite instant search) and the search-speed buff keep their full effect. Only searches run on the machine with the plugin change. Other Fika players' searches are not affected, and vanilla bots use separate search code. Install it on clients only, not on the headless client.

## Why
SVM/Greed has no search-speed option; search timing is client-side.

## Settings
| Section / Key | Default | Effect |
|---|---|---|
| General / SpeedMultiplier | `1.25` | Search speed factor (0.1–10). Delays are divided by it. `1` = vanilla. Applies to the next search, no restart. |
