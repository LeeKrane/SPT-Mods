// The prestige transfer screen (PrestigeTransferItemsState) rejects every CompoundItem that is
// not empty, in two hardcoded places: ISelectionContext.IsActive (click to select) and
// ArenaEftItemTransferGridView.CanAccept (drag into the grid). No server config can lift that.
// The server's PrestigeHelper copies each transferred item with all its children.
//
// Allowing filled containers opens two holes that vanilla never had, so the plugin closes them:
// - Contents skip the stash filter, which only checks the selected item's own template. Contents
//   are checked here too, except items attached in slots (weapon mods), as in vanilla.
// - A container and an item inside it could both be selected, and the server would copy the
//   inner item twice. Items above or below a selected item in the hierarchy are rejected.
//   The transfer grid is a ReferenceGrid, so selected items keep their real parents.
// Both rules run on select, on drop, and again on accept, since the stash stays editable and
// items can be moved into a selected container afterwards.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using System.Threading.Tasks;
using BepInEx.Logging;
using Diz.LanguageExtensions;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using ItemFilter = EFT.Prestige.PrestigeTemplate.ItemsTransferConfig.TransferStashConfig.ItemFilter;

namespace Krane.PrestigeTransferFilledContainers
{
    [BepInPlugin("krane.prestigetransferfilledcontainers", "PrestigeTransferFilledContainers", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            new Harmony("krane.prestigetransferfilledcontainers").PatchAll(typeof(Plugin).Assembly);
        }
    }

    internal static class TransferRules
    {
        // Last opened prestige transfer screen, or null. CanAccept has no link back to the state,
        // so callers match the grid view against Current._transferView to rule out a stale state.
        internal static PrestigeTransferItemsState Current;

        internal static PrestigeTransferItemsState StateFor(ArenaEftItemTransferGridView view)
        {
            var state = Current;
            return state != null && state._transferView == view ? state : null;
        }

        // Called by the transpiled CanAccept in place of CompoundItem.IsEmpty.
        internal static bool IsEmptyOrPrestige(CompoundItem item, ArenaEftItemTransferGridView view) =>
            item.IsEmpty || StateFor(view) != null;

        internal static bool Allows(PrestigeTransferItemsState state, Item item)
        {
            ItemFilter filters = state._stashConfig.Filters;
            if (!filters.Contains(item.Template)) return false;
            if (!ContentsAllowed(item, filters)) return false;
            return !OverlapsSelection(state._selectedItems, item);
        }

        // Grid and stack slot contents (e.g. cartridges in a magazine), at any depth.
        private static bool ContentsAllowed(Item item, ItemFilter filters) =>
            item.GetAllItems().All(child =>
                child == item || child.CurrentAddress is SlotItemAddress || filters.Contains(child.Template));

        private static bool OverlapsSelection(HashSet<Item> selected, Item item)
        {
            if (selected.Count == 0) return false;
            if (item.GetAllParentItems().Any(selected.Contains)) return true;
            return selected.Any(s => s != item && s.GetAllParentItems().Contains(item));
        }
    }

    [HarmonyPatch(typeof(PrestigeTransferItemsState), nameof(PrestigeTransferItemsState.Init))]
    internal static class InitPatch
    {
        private static void Postfix(PrestigeTransferItemsState __instance) => TransferRules.Current = __instance;
    }

    [HarmonyPatch(typeof(PrestigeTransferItemsState), nameof(PrestigeTransferItemsState.Close))]
    internal static class ClosePatch
    {
        private static void Prefix(PrestigeTransferItemsState __instance)
        {
            if (TransferRules.Current == __instance) TransferRules.Current = null;
        }
    }

    [HarmonyPatch]
    internal static class IsActivePatch
    {
        // Explicit interface implementation: the IL name is prefixed with the interface name.
        private const string MethodName = "EFT.InventoryLogic.SelectableItemContext.ISelectionContext.IsActive";
        private static MethodInfo _target;

        private static bool Prepare()
        {
            _target = AccessTools.Method(typeof(PrestigeTransferItemsState), MethodName);
            if (_target == null) Plugin.Log.LogError($"{nameof(PrestigeTransferItemsState)}.{MethodName} not found, click selection stays vanilla");
            return _target != null;
        }

        private static MethodBase TargetMethod() => _target;

        // Vanilla rejects only on the filter or a filled container, so the rules replace its result.
        private static void Postfix(PrestigeTransferItemsState __instance, ItemContext context, ref bool __result, ref string tooltip)
        {
            __result = TransferRules.Allows(__instance, context.Item);
            tooltip = __result ? null : ItemTransferError.TRANSFER_LOCKED_KEY;
        }
    }

    [HarmonyPatch(typeof(PrestigeTransferItemsState), nameof(PrestigeTransferItemsState.SendItemsAccept))]
    internal static class SendItemsAcceptPatch
    {
        private static bool Prefix(PrestigeTransferItemsState __instance, ref Task<bool> __result)
        {
            if (__instance._selectedItems.All(item => TransferRules.Allows(__instance, item))) return true;
            NotificationManager.DisplaySingletonWarningNotification(
                "Transfer blocked: a selected container now holds a filtered or already selected item.");
            __result = Task.FromResult(false);
            return false;
        }
    }

    [HarmonyPatch(typeof(ArenaEftItemTransferGridView), nameof(ArenaEftItemTransferGridView.CanAccept))]
    internal static class CanAcceptPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions)
                .MatchStartForward(new CodeMatch(i => i.Calls(AccessTools.PropertyGetter(typeof(CompoundItem), nameof(CompoundItem.IsEmpty)))));
            if (matcher.IsInvalid)
            {
                Plugin.Log.LogError($"{nameof(CompoundItem)}.{nameof(CompoundItem.IsEmpty)} check not found in {nameof(ArenaEftItemTransferGridView)}.CanAccept, dragging filled containers stays blocked");
                return matcher.InstructionEnumeration();
            }
            // Push the view as a second argument. Labels move to the inserted ldarg.0 so branches
            // that targeted the IsEmpty call still land before both arguments are complete.
            var ldView = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(matcher.Instruction);
            return matcher
                .SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(TransferRules), nameof(TransferRules.IsEmptyOrPrestige)))
                .Advance(-1)
                .Insert(ldView)
                .InstructionEnumeration();
        }

        // Drags from the player stash view skip IsActive, so the rules run here as well.
        private static void Postfix(ArenaEftItemTransferGridView __instance, DragItemContext itemContext, ref bool __result, ref OperationResult operation)
        {
            var state = TransferRules.StateFor(__instance);
            if (!__result || state == null || TransferRules.Allows(state, itemContext.Item)) return;
            __result = false;
            operation = new ItemTransferError();
        }
    }
}
