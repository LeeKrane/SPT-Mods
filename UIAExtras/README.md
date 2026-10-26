# UIAExtras

**Requires:** SPT 4.1.6, Use Items Anywhere (`com.cj.useFromAnywhere`). Without it the plugin does nothing. Fika-compatible (2.4.6).

## What
Extends Use Items Anywhere's quick-use wheel:
- **Move while the wheel is open.** Movement (and optionally sprint) keys pass through. Mouse look and other commands stay blocked. Held actions started before opening still end.
- **No helmet-camera bob** when the wheel opens.
- **Pinned main wheel.** Replaces the category page with 1-3 pages of pinned item slots. Pin with the Pin Key, clear with the Clear Key.
- **Category fallback.** If a pinned med is not carried, the slot shows another carried item that treats the same thing (e.g. another medkit).
- **Use items while busy.** The wheel stays usable while a med is in use or hands are switching. The selection is queued.

## Why
UIA blocks all input while the wheel is open, so you stand still while healing. Its category pages also make you hunt for the same items every time.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Enabled | `true` | Let movement keep working while the wheel is open. |
| General / Allow Sprint | `true` | Also pass sprint through. |
| General / Show Items During Use | `true` | Keep items selectable while using a med or switching hands (queued). |
| Main Wheel / Main Pages | `2` | Pages of pinned slots (1-3). Scroll to switch. |
| Main Wheel / Slots Per Page | `8` | Slots per page (4-8). |
| Wheel Pins / Pin Key | `Mouse2` (middle) | On a slot: open the item list to pick a pin. In the list: pin the hovered item. |
| Wheel Pins / Clear Key | `Ctrl+Mouse2` | Remove the pin from the hovered slot. |
| Wheel Pins / Category Fallback | `Auto` | `Auto` = substitute a carried item of the same category; `Never` = show greyed NONE CARRIED. |
| Debug / Log Wheel Key | `false` | Log wheel key and open diagnostics (needs restart). |

Pins are stored in hidden config entries and only set in the wheel.
