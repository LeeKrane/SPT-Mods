# ScopeZoomPresets

## Requirements

- SPT 4.1.6, Fika 2.4.6 (optional, works in single player too)
- No other mods required

## What it does

- The scope magnification hotkey on variable zoom scopes toggles between two points you choose per scope group, e.g. 15% and 70% of a 1x-6x scope instead of 1x and 6x. From an in-between zoom, it jumps to the farther point.
- At raid start, scopes in an enabled group start at the group's Min %.
- The optic HUD label shows `N% · ~Zx` on every zoom change (hotkey and scroll) for all variable zoom scopes.
- Scroll zoom is unchanged and covers the full native range. Only your own scopes are affected; nothing is sent over the network.

Percent is linear in the scope's field of view: 0% is its lowest native zoom, 100% its highest. The `~Zx` value is an approximation; the game has no exact magnification for in-between zooms.

## Why

Vanilla only toggles between the extremes of a variable scope (1x/6x). Often a closer pair like 1.5x/4x is more useful.

## Groups

1x-4x, 1x-6x, 1x-8x, 2.5x-8x, 3x-9x, 3x-10x, 3x-20x, 3x-24x, 4x-16x, 5x-25x, 6.5x-20x, 7x-35x. A scope belongs to the group of the label range in its item data, so modded variants (black/tan Razors) share their base scope's group. Scopes with another range keep the vanilla toggle. The BepInEx log lists every variable scope and its group at the first raid start.

## Settings

| Section / Key | Default | Effect |
|---|---|---|
| `<group>` / Enabled | false | Use this group's toggle points and raid-start reset |
| `<group>` / Min % | 0 | Low-zoom toggle point, 0-100 |
| `<group>` / Max % | 100 | High-zoom toggle point, 0-100. Swapped with Min % if lower |
| HUD / Show zoom label | true | Show `N% · ~Zx` on every zoom change. Off: vanilla label |
