# VisitButtonFix

**Requires:** SPT 4.1.6, Fika 2.4.6 compatible. Soft dependencies: VisitAPI 1.3.7 (the button this fixes) and Kaeno-TraderScrolling (the mod that breaks it). Does nothing without VisitAPI, and patches nothing when TraderScrolling is not loaded.

## What
Places VisitAPI's VISIT button (trader screen, top center, opens the 3D trader room) above the trader cards again while Kaeno-TraderScrolling is installed. The button is positioned from the top edge of a visible trader card after both mods have run on `TraderScreensGroup.Show` and `SelectTrader`.

## Why
VisitAPI positions the button from the top edge of `_traderCardsContainer`. TraderScrolling turns that container into scroll content and re-anchors it and its parent, so VisitAPI's measurement puts the button off screen and the storyline trader visits cannot be opened.

## Settings
None.
