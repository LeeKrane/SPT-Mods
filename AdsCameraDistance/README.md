# AdsCameraDistance

## Requirements

- SPT 4.1.6, Fika 2.4.6 (optional, works in single player too)
- No other mods required

## What it does

- While aiming down sights in first person, hold Left Ctrl + Left Shift and scroll the mouse wheel to move your eye closer to (scroll up) or farther from (scroll down) the sight. Left Ctrl + Left Shift + middle mouse button resets the current sight to vanilla.
- The offset is saved per weapon and sight pair (and per selected sub-scope of that sight; weapons using built-in iron sights are keyed per aim index), so each combination keeps its own distance.
- A label such as `Eye dist -0.035` (or `Eye dist vanilla` after a reset) is shown on the optic HUD panel on each adjustment.
- The main camera FOV never changes. Applies only to your own weapon while aiming in first person: not while blind-firing, mounted, on stationary weapons or for bots and other players. The camera glides to the new distance with the game's own smoothing.

- For magnified optics the scope image is a fixed square texture. A closer eye makes the lens cover more of the screen than that texture, which would leave black edges. The plugin widens the optic camera's field of view by the same ratio, so the lens is filled again, and enlarges the lens image (tuned in-game), so the target stays about the same size as vanilla while the lens and reticle get larger. The same pixels then cover a larger area, so the scope image texture is raised to 2048 by default to keep it sharp.

## Why

Some scopes look small on screen at the vanilla eye distance. Moving the eye closer makes the sight fill more of the view without changing the field of view.

## Storage

Offsets are stored in `BepInEx/config/krane.AdsCameraDistance.json` (flat map of `<weaponTemplateId>|<sightTemplateId>|<subScopeIndex>` to metres). Saved about 1 s after the last change and on quit, via a `.tmp` file that is loaded instead if a save was interrupted. A corrupt file is renamed to `.bak` and the plugin starts empty. A file that exists but cannot be read (locked, no permission) is left untouched: offsets are then neither applied nor saved for that session. Stored values outside Min / Max are applied clamped but not rewritten, so widening the range again restores them.

The offset is in metres: **negative = closer to the sight**, positive = farther. Very close values can clip the camera into the weapon or the scope body; lower Min if you want more range, raise it if clipping bothers you.

## Settings

| Section / Key | Default | Effect |
|---|---|---|
| General / Enabled | true | Master switch. Off: vanilla camera, no input handling |
| Input / Modifier 1 | LeftControl | First key to hold while scrolling |
| Input / Modifier 2 | LeftShift | Second key to hold while scrolling |
| Adjust / Step | 0.005 | Metres per scroll notch |
| Adjust / Min | -0.03 | Closest offset in metres (negative = closer) |
| Adjust / Max | 0 | Farthest offset in metres |
| Adjust / Invert direction | false | Flip the sign if negative moves the camera away from the sight |
| Adjust / Compensate lens FOV | true | Widen the optic camera's view by the ratio the lens grew on screen, so the scope has no black edges. Target stays vanilla size, lens and reticle get larger. Off: closer eye shows black lens edges |
| Adjust / Optic resolution | 2048 | Scope image texture size while an offset applies to the scope aimed through (vanilla 1024 otherwise). Higher keeps the widened scope image sharp; costs GPU time while scoped |
| Adjust / Reticle growth | 0 | How much the reticle grows with the lens: 0 = vanilla on-screen size, 1 = with the lens. Below 1 the reticle texture is redrawn shrunk and centred while the lens is scaled. Only shrinks: with the eye farther than vanilla the reticle stays original |
| Adjust / Reticle edge fill | Extend | Margin around a shrunk reticle. Extend: lines reaching the texture edge continue to the lens edge (edge rings or tints smear). Transparent: nothing smears, but such lines end early. Reticles without an alpha channel always extend |
| HUD / Show label | true | Show the offset label on each adjustment |
