# PrestigeTransferFilledContainers

**Requires:** SPT 4.1.6. Fika-compatible (2.4.6). Soft dependency: a server mod that loosens the prestige transfer item filter, e.g. SVM with prestige `Filter` enabled. Without one, the vanilla whitelist still decides which items can be transferred.

## What
Lets you put filled containers (keytool, keycard holder, item cases, etc.) into the prestige transfer grid, by click or by drag. Their contents are transferred with them.

Rules for a filled container:
- The container and everything inside it must pass the prestige stash filter. This includes grid contents at any depth and cartridges in magazines. Items attached in slots, such as weapon mods, are not checked, as in vanilla. Without a loosened filter, a loaded magazine is blocked if its ammo is not whitelisted.
- You cannot select a container together with an item inside it, or an item inside a selected container. The server would otherwise copy the inner item twice.
- The stash stays editable after you select a container. Both rules run again when you confirm the transfer. If an item no longer passes, the transfer is blocked with a warning.

Side effects:
- Container contents do not count against the transfer grid size. A large case carries far more than the grid's cell count.
- The server unpins and unlocks only the items you select directly. Pinned or locked items inside a container stay pinned or locked in the new profile.

## Why
The client's prestige transfer screen rejects any container that is not empty. It does this in `PrestigeTransferItemsState` (click, `UI/Prestige/NonEmptyContainerError`) and in `ArenaEftItemTransferGridView.CanAccept` (drag). The rule is hardcoded, so no server config can lift it. SVM's `Filter` option only removes the item whitelist. The server already copies each transferred item with all its children. Only the UI blocked it.

## Settings
None.
