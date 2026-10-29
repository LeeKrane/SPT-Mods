# TraderTweaks

**Requires:** SPT 4.1.6, Fika 2.4.6 (no dependency on Fika, works with or without). Soft dependency: the Hideout Harry server mod (`acidphantasm-harryhideout`, trader id `67419e9d0d4541ce671543bb`). Without it the plugin does nothing.

## What
Client-side trader options. Currently one: lock Hideout Harry for you only.
- Harry shows as a locked trader (like one you have not unlocked), cannot be opened, and is skipped as insurer, repairer or medic.
- Trader cards update live from the F12 menu. A trader screen already open on Harry's page, and insurer or repairer lists already shown, keep him until reopened.

## Why
Harry is added by a server mod for everyone. This lets a single player opt out without changing the server or other players.

## Settings
| Section / Key | Default | Effect |
|---|---|---|
| Hideout Harry / Disable | `false` | Lock Hideout Harry on this client. Server profile is untouched. |
