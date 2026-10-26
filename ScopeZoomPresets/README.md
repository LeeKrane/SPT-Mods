# ScopeZoomPresets

## Requirements

- SPT 4.1.6, Fika 2.4.6 (optional, works in single player too)
- No other mods required

## What it does

- The scope magnification hotkey on variable zoom scopes toggles between two points you choose per scope group, e.g. 1.5x and 4.0x on a 1x-6x scope instead of 1x and 6x. From an in-between zoom, it jumps to the farther point. Applies only to groups with Enabled on; disabled groups, unlisted ranges and scopes with an unusual zoom curve (not a monotonic 0..1 map) keep the vanilla toggle.
- At raid start, all your scopes in an enabled group, including loose ones in your bags, start at the group's lower toggle point, i.e. the lower of Min zoom and Max zoom.
- The optic HUD label shows the zoom as `N.Nx` (e.g. `4.2x`) on each hotkey press and while scrolling whenever the zoom, rounded to 0.1x, changes. It appears only for the sight currently selected on your weapon, and replaces the vanilla `xN` label for variable zoom scopes, including when the scope reaches a native edge. While a fixed sub-scope is selected (e.g. the 1x backup of an NcSTAR ADO), the vanilla label stays. The label is drawn only on a hotkey press or a zoom change, not when you draw the weapon. Scopes whose native range is inverted (MinMaxFov x <= y) are unsupported and stay fully vanilla.
- Scroll zoom is unchanged and covers the full native range; after a toggle or a weapon redraw it continues from the current zoom without a jump. This holds for scopes with a normal zoom curve; scopes with an unusual curve (see above) behave exactly like vanilla, including vanilla's own jump when you scroll again from an in-between zoom. Only your own scopes are affected: bot and other players' handlers run vanilla code, and the plugin's hooks exit immediately for them (one flag or reference check, no changes). The toggle, the label and the scroll correction apply only to the weapon in your hands. The zoom value is ordinary item state that Fika syncs like vanilla, and it always stays within the scope's native range.

Toggle points and the label are in x magnification, in 0.1 steps. The native edges are exact: the group's lowest and highest label are the scope's native lowest and highest zoom. The game has no magnification formula for in-between zooms, so those are approximate optical magnification (interpolated from the field of view between the two edges). The settings default to the group's edges, which is the vanilla toggle. Old `Min %` / `Max %` entries left in the config file are ignored and harmless.

## Why

Vanilla only toggles between the extremes of a variable scope (1x/6x). Often a closer pair like 1.5x/4x is more useful.

## Groups

1x-4x, 1x-6x, 1x-8x, 2.5x-8x, 3x-9x, 3x-10x, 3x-20x, 3x-24x, 4x-16x, 5x-25x, 6.5x-20x, 7x-35x. A scope belongs to the group of the label range of its variable zoom entry in its item data (the selected sub-scope's entry when it is variable, otherwise the first variable entry, so an NcSTAR ADO with its 1x backup selected still belongs to 3x-9x), so modded variants (black/tan Razors) share their base scope's group. Scopes with another range, and scopes in disabled groups, keep the vanilla toggle. The BepInEx log lists every variable scope and its group at the first raid start.

## Settings

| Section / Key | Default | Effect |
|---|---|---|
| `<group>` / Enabled | false | Use this group's toggle points and raid-start reset |
| `<group>` / Min zoom | group's low label (e.g. 1.0) | Low-zoom toggle point in x magnification, within the group's range, 0.1 steps |
| `<group>` / Max zoom | group's high label (e.g. 6.0) | High-zoom toggle point in x magnification, 0.1 steps. Swapped with Min zoom if lower |
| HUD / Show zoom label | true | Show `N.Nx` on each hotkey press and on scroll when the zoom, rounded to 0.1x, changes. Off: vanilla label |
