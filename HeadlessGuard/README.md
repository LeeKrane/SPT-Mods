# HeadlessGuard

**Requires:** SPT 4.1.6, Fika 2.4.6 (hard dependency, `com.fika.core`).

## What
Asks for confirmation when you press Start in Fika's matchmaker with "Use headless" off.
- **Yes:** Fika hosts the raid on your machine as usual.
- **No:** nothing happens, the matchmaker screen stays open.

With "Use headless" on, Start goes straight to the headless. The window also opens when no headless is online. Extra Start clicks are ignored while the window is open. If the window cannot be shown, Start is blocked and an error is logged.

## Why
Forgetting the headless toggle silently makes you the raid host. This catches it before the raid is created.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Enabled | `true` | Ask before hosting a raid yourself. |
