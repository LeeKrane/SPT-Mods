# ItemTweaks

**Requires:** SPT 4.1.6, Fika 2.4.6. Soft dependency on the server mod ItemTweaks4Server (adds the light case item); without it only the repair-in-raid feature does anything. Install on every client and on the headless client.

## What
Client side of the light case (template `f39e1020a6eb538f07653b87`, a Dogtag case clone, 1x1, one 3x6 grid):
- Everything stored in the case is lighter (90% by default, see settings) in all weight calculations (player weight, overweight, stamina, tooltips). The case's own weight is unchanged.
- The case model and its inventory icon are light gray. The original Dogtag case is unaffected.

Independent of the case (works without ItemTweaks4Server):
- Armor and weapon repair kits carried in raid equipment (rig, backpack, pockets, containers, any depth) can be used in raid: drag a kit onto an item, or use the item context menu Repair (shown only when a suitable kit is carried). Repair is instant, no window and no trader entries: the item (armor: plates and inserts too) is repaired as far as the kits allow, a dragged kit is spent first, then the smallest kits.

## Why
The case is a lightweight carry container. Weight and look are client-side, so the server mod only adds the item.
Vanilla disables repair in raid. Kits are consumable and the Spec-ops kit case makes carrying them practical.

## How
Weight: postfix on `ContainerExtensions.TotalWeight(IContainer)`, the one place container contents are summed; scaled when the container belongs to the case. Fika needs no changes, weight is local.
Look: the model comes from the Dogtag case bundle, so after `ObjectsFactory.CreateItemAsync` it is tinted per renderer with a property block (shared materials untouched, pooled models reset when reused). Icons are rendered from that model.

Repair in raid: vanilla hides Repair in raid (`ItemContextInteractionsSwitcher.IsActive`) and its repair stack is bound to the menu profile. Patches (all inactive outside a raid or with the setting off) re-allow Repair, keep it a plain action instead of a trader submenu (UIFixes adds one; `SubInteractions` postfix runs last), and replace `ItemUiContext.OpenRepairWindow` (the context menu and a kit drag both end there) with an instant local repair, so the repair window never opens in raid. A `RepairController` bound to the raid profile is built lazily, only for its formulas; `RepairController` itself is never patched. The amount repaired is what the repair window would offer (max durability minus the minimum kit wear, minus current durability). Formula follows SPT 4.1.6 `RepairService.RepairItemByKit` / `RepairHelper.UpdateItemDurability` / `AddBuffToItem`: kit points divided by the client repair cost formula (skills, armor material, class) give the durability gain, max durability then drops by a random material/weapon kit degradation, buffs roll as in `repair.json`. Not done: skill XP and the elite Charisma no-wear roll. A fully used kit stays in the inventory with 0 points. Durability, max durability and kit points are saved with the profile at the end of the raid.
Fika: no network changes; repair is local. Known limitation: other players (and the host) keep the old durability and kit points until the next raid, so a kit you spent or dropped still looks full to them, and a kit dropped after use can be picked up with its old points (duplication). Not fixable without network sync.

## Settings
| Key | Default | Effect |
|---|---|---|
| Weight / Content Weight Reduction | `90` | Percent of weight removed from items inside the case (0 to 95). Applies live. |
| Look / Case Color | very light gray (0.93) | Color of the case model and icon. |
| Look / Flat Color | `true` | Replace the albedo texture with a flat color. Off: multiply the texture by the color only (stays dark). Icons already cached keep the old look. |
| Repair / Allow Repair Kits In Raid | `true` | Allow using armor/weapon repair kits in raid (drag onto item or context menu Repair, instant). Off: vanilla, no repair in raid. |
