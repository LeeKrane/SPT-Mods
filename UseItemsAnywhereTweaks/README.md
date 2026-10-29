# UseItemsAnywhereTweaks

**Requires:** SPT 4.1.6, Use Items Anywhere (`com.cj.useFromAnywhere`). Without it the plugin does nothing. Fika-compatible (2.4.6).

## What
Extends Use Items Anywhere's quick-use wheel:
- **Move while the wheel is open.** Movement (and optionally sprint) keys pass through. Mouse look and other commands stay blocked. Held actions started before opening still end.
- **No helmet-camera bob** when the wheel opens.
- **Pinned main wheel.** Replaces the category page with 1-3 pages of pinned item slots. Pin with the Pin Key, clear with the Clear Key.
- **Category fallback.** If a pinned med is not carried, the slot shows another carried item that treats the same thing (e.g. another medkit).
- **Use items while busy.** The wheel stays usable while a med is in use or hands are switching. Selections made during a med/food use or a hands switch queue up (up to 8) and run in pick order, each after the previous use ends (Continuous Healing repeats included). A weapon or quick-slot key, interrupting or cancelling a use drops the queue. An item no longer carried is skipped; if hands stay stuck for 5 s the queue is dropped. Needs Show Items During Use on and UIA's access delays off; with delays on, UIA's own Pending Item Use Behavior applies.

## Why
Use Items Anywhere (UIA) blocks all input while the wheel is open, so you stand still while healing. Its category pages also make you hunt for the same items every time.

## Settings
| Key | Default | Effect |
|---|---|---|
| General / Enabled | `true` | Let movement keep working while the wheel is open. |
| General / Allow Sprint | `true` | Also pass sprint through. |
| General / Show Items During Use | `true` | Keep items selectable while using a med or switching hands (queued). |
| General / Queue Selection During Use | `true` | Selections during a med/food use or hands switch queue up and run in order. Needs Show Items During Use and UIA delays off. Off = it cancels the running use (vanilla). |
| Main Wheel / Main Pages | `2` | Pages of pinned slots (1-3). Scroll to switch. |
| Main Wheel / Slots Per Page | `8` | Slots per page (4-8). |
| Wheel Pins / Pin Key | `Mouse2` (middle) | On a slot: open the item list to pick a pin. In the list: pin the hovered item. |
| Wheel Pins / Clear Key | `Ctrl+Mouse2` | Remove the pin from the hovered slot. |
| Wheel Pins / Category Fallback | `Auto` | `Auto` = substitute a carried item of the same category; `Never` = show greyed NONE CARRIED. |
| Debug / Log Wheel Key | `false` | Log wheel key and open diagnostics (needs restart). |

Pins are stored in hidden config entries and only set in the wheel.
