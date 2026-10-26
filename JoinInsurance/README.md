# JoinInsurance

**Requires:** SPT 4.1.6, Fika 2.4.6 (hard dependency, `com.fika.core`).

## What
Shows the vanilla insurance screen when you join a PMC raid from Fika's main-menu player list.
- **Next:** flushes pending insurance, re-checks the raid is still open, then runs Fika's normal join.
- **Back:** closes the screen, no join.

Scav raids, spectating, a join already in progress and a missing map key skip the screen.

## Why
Fika's Join button calls `LocalGameMatching` directly and skips the matchmaker flow. Joining PMCs never see the insurance screen the host gets.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Enabled | `true` | Show the insurance screen before joining. |
