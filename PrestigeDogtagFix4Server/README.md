# PrestigeDogtagFix4Server

**Requires:** SPT 4.1.6 server. Server-only; Fika 2.4.6 compatible and unaffected. Soft dependency on WTT-ContentBackport + archangelwtf-contentbackportprestiges, only for prestige 5-6 (without them those levels do not exist and are simply not added).

## What
At server load, adds every prestige dogtag to each container grid filter that already allows one: the SICC case, the dogtag case, and clones of them (for example WTT's twitch dogtag case). Dogtags are found by template (`DogTagQualities` and a name containing `item_barter_dogtags_(bear|usec)_prestige_N`, which also matches WTT's renamed clones like `[WTT-ContentBackport]_(item_barter_dogtags_bear_prestige_05)`), so prestige 1-6 and any future level are covered without hardcoded IDs. Dogtags a filter excludes, by template or by their parent class, are skipped. One info line logs how many entries were added and to which containers.

## Why
Vanilla SPT whitelists only prestige 1-2 dogtags in those containers. Prestige 3-4 (vanilla SPT) and 5-6 (added by WTT-ContentBackport) could not be placed in them.

## Settings
None. There is no `config.json`.
