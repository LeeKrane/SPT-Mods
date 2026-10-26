# RememberInsurer

**Requires:** SPT 4.1.6. Fika 2.4.6 compatible, not required. No other mods.

## What
Remembers the trader you last picked in the insurer dropdown and preselects it the next time an insurance screen opens: the pre-raid insurance screen and the single-item insure window. The choice is stored in the config file, so it survives game restarts. If that trader is no longer an available insurer, the selection is left to the game (the current in-session pick, else the first insurer) and the stored choice is kept. Picks are stored even while disabled.

## Why
The client builds a new insurance state after login and after every raid, so the selected insurer resets to Prapor each time.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Enabled | `true` | Preselect the last picked insurer. |
| State / LastInsurerId | `""` | Trader id of the last pick. Set by the plugin, hidden in the F12 menu. |
