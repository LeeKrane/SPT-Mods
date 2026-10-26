// Use Items Anywhere (com.cj.useFromAnywhere) blocks every command and all input axes while its
// quick-use wheel is open: QuickUseWheelController.InputBlocked is read by its prefixes on
// GamePlayerOwner.TranslateCommand / TranslateAxes, which return BlockAll / skip the original.
// HarmonyX runs ALL prefixes (a false return does not skip the others), so those prefixes cannot be
// bypassed by ordering. Instead, while UIA's own block decision is true (InputBlocked and
// CoopRuntime.IsLocalPlayer), a prefix on InputNode.TranslateInput for the player's owner:
//   - removes every non-whitelisted command from the shared list itself (what BlockAll's
//     commands.Clear() would have done, minus the whitelisted ones), so Tab/Esc/etc. never reach
//     the owner, later nodes or the menu;
//   - hands the owner a reusable buffer copy of the axes with the look axes (2-5) zeroed, the
//     shared array that later nodes see is restored in a finalizer;
//   - flips InputBlocked to false for exactly this call so UIA's prefixes pass through and the
//     real GamePlayerOwner.TranslateCommand/TranslateAxes run; the finalizer flips it back.
// Whitelist: movement commands plus every release/end command (so hold actions started before the
// wheel opened still end). Also loosens UIA's wheel-open shortcut check (BepInEx's
// KeyboardShortcut.IsDown fails when any other key is held) and keeps MovementContext.RotationAction
// when the wheel opens (UIA nulls it, which unpins the camera from the hands rotation and gives the
// head-bob "helmet camera"); mouse look stays blocked through the zeroed axes instead.
// Extras.cs replaces the wheel's category page with up to 3 pages of pinned item slots.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using Diz.LanguageExtensions;
using EFT;
using EFT.InventoryLogic;
using EFT.InputSystem;
using HarmonyLib;
using UnityEngine;

namespace Krane.UIAExtras
{
    [BepInPlugin("krane.UIAExtras", "Krane's UIA Extras", "5.1.0")]
    [BepInDependency("com.cj.useFromAnywhere", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal const string HarmonyId = "krane.UIAExtras";
        internal const string ExtrasHarmonyId = HarmonyId + ".slots";
        private static readonly List<string> _patchErrors = new List<string>();
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> AllowSprint;
        internal static ConfigEntry<bool> ShowItemsDuringUse;
        internal static ConfigEntry<bool> DebugLogWheelKey;
        internal const string DebugHarmonyId = HarmonyId + ".debug";

        // UIA internals, bound in Awake.
        internal static Func<bool> IsBlocked;
        internal static Action<bool> SetBlocked;
        internal static Func<Player, bool> IsLocalPlayer;

        // UIA wheel shortcut entries with the matching "wheel enabled" entry (null = assume enabled).
        private sealed class WheelShortcut
        {
            public FieldInfo Key;
            public FieldInfo Enable;
        }
        private static WheelShortcut[] _shortcuts = new WheelShortcut[0];

        private static readonly HashSet<ECommand> Movement = new HashSet<ECommand>
        {
            ECommand.ToggleDuck, ECommand.ToggleProne, ECommand.RestorePose, ECommand.ToggleWalk,
            ECommand.ToggleLeanLeft, ECommand.ToggleLeanRight, ECommand.LeanLeftSlow,
            ECommand.ToggleStepLeft, ECommand.ToggleStepRight,
            ECommand.Jump, ECommand.Vaulting,
        };

        // Release / end commands always pass so hold actions that started before the wheel opened
        // (shoot, interact, tactical, grenade, breath, blind fire, lean, step, walk, sprint, voice)
        // can finish.
        private static readonly HashSet<ECommand> Release = new HashSet<ECommand>
        {
            ECommand.EndShooting, ECommand.EndAlternativeShooting,
            ECommand.EndInteracting, ECommand.EndSpecialInteracting,
            ECommand.ReleaseTactical, ECommand.FinishHighThrow, ECommand.FinishLowThrow,
            ECommand.EndBreathing, ECommand.BlindShootEnd,
            ECommand.EndAnimBlindFireAbove, ECommand.EndAnimBlindFireRight,
            ECommand.EndWalk, ECommand.EndSprinting, ECommand.EndLeanLeft, ECommand.EndLeanRight,
            ECommand.ReturnFromLeftStep, ECommand.ReturnFromRightStep, ECommand.VaultingEnd,
            ECommand.MumbleEnd, ECommand.StopTalk,
        };

        internal static bool IsAllowed(ECommand c) =>
            Release.Contains(c) || Movement.Contains(c) || (c == ECommand.ToggleSprinting && AllowSprint.Value);

        // UIA's own decision to block this owner's input right now.
        internal static bool Active(GamePlayerOwner owner)
        {
            if (!IsBlocked() || !Enabled.Value) return false;
            return IsLocalPlayer(owner.Player);
        }

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Let movement keep working while the Use Items Anywhere wheel is open.");
            AllowSprint = Config.Bind("General", "Allow Sprint", true,
                "Also pass the sprint key through while the wheel is open.");
            ShowItemsDuringUse = Config.Bind("General", "Show Items During Use", true,
                "Keep items available in the wheel while a med/food item is being used or hands are switching. Vanilla locks the inventory meanwhile, so UIA marked everything UNAVAILABLE; its pending-use logic (replace/queue) then handles the selection.");

            DebugLogWheelKey = Config.Bind("Debug", "Log Wheel Key", false,
                "Log UIA wheel key presses, controller state, Open bail conditions and gesture begin/reset. For diagnosing a wheel that will not open. Changing this needs a game restart (the debug hooks are only installed at startup when it is on).");

            // Soft dependency: BepInEx sorts plugins so the other mod loads first when present.
            if (!Chainloader.PluginInfos.ContainsKey("com.cj.useFromAnywhere"))
            {
                Log.LogInfo("Use Items Anywhere not installed; doing nothing.");
                return;
            }

            var controller = AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelController");
            var getter = controller == null ? null : AccessTools.PropertyGetter(controller, "InputBlocked");
            var setter = controller == null ? null : AccessTools.PropertySetter(controller, "InputBlocked");
            var coop = AccessTools.TypeByName("UseItemsAnywhere.Integration.CoopRuntime");
            var isLocal = coop == null ? null : AccessTools.Method(coop, "IsLocalPlayer", new[] { typeof(Player) });
            if (getter == null || setter == null || !getter.IsStatic || !setter.IsStatic || getter.ReturnType != typeof(bool) || isLocal == null || !isLocal.IsStatic || isLocal.ReturnType != typeof(bool))
            {
                Log.LogWarning("Could not bind QuickUseWheelController.InputBlocked / CoopRuntime.IsLocalPlayer (mod version changed?); doing nothing.");
                return;
            }
            IsBlocked = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), getter);
            SetBlocked = (Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), setter);
            IsLocalPlayer = (Func<Player, bool>)Delegate.CreateDelegate(typeof(Func<Player, bool>), isLocal);

            var config = AccessTools.TypeByName("UseItemsAnywhere.Configuration");
            var shortcuts = new List<WheelShortcut>();
            foreach (var pair in new[] { new[] { "QuickUseWheelKey", "EnableQuickUseWheel" }, new[] { "WeaponDeviceWheelKey", "EnableWeaponDeviceWheel" } })
            {
                var key = config == null ? null : AccessTools.Field(config, pair[0]);
                if (key == null || !key.IsStatic || key.FieldType != typeof(ConfigEntry<KeyboardShortcut>)) continue;
                var enable = config == null ? null : AccessTools.Field(config, pair[1]);
                if (enable != null && (!enable.IsStatic || enable.FieldType != typeof(ConfigEntry<bool>))) enable = null;
                if (enable == null)
                    Log.LogWarning("UIA " + pair[1] + " entry not found; its shortcut counts as always enabled in conflict resolution.");
                shortcuts.Add(new WheelShortcut { Key = key, Enable = enable });
            }
            _shortcuts = shortcuts.ToArray();
            if (_shortcuts.Length < 2)
                Log.LogWarning("UIA wheel shortcut config entries not found; shortcut conflict resolution disabled.");

            var harmony = new Harmony(HarmonyId);
            var extrasHarmony = new Harmony(ExtrasHarmonyId);
            // HarmonyX logs "Failed to patch ..." and swallows the exception, so Harmony.Patch never
            // throws for e.g. a bad injected field name. Watch its error channel while we patch.
            _patchErrors.Clear();
            HarmonyLib.Tools.Logger.ChannelFilter |= HarmonyLib.Tools.Logger.LogChannel.Error;
            EventHandler<HarmonyLib.Tools.Logger.LogEventArgs> watcher = (sender, args) =>
            {
                if (args.LogChannel == HarmonyLib.Tools.Logger.LogChannel.Error && args.Message != null && args.Message.Contains("Failed to patch"))
                    _patchErrors.Add(args.Message.Split('\n')[0]);
            };
            HarmonyLib.Tools.Logger.MessageReceived += watcher;
            try
            {
                PatchTranslateInput(harmony);
                PatchWheelOpening(harmony, controller);
                PatchOpen(harmony, controller);
                PatchAvailability(harmony);
                PatchBusySelection(harmony, controller);
                if (_patchErrors.Count > 0)
                    throw new InvalidOperationException("HarmonyX reported " + _patchErrors.Count + " failed patch(es): " + string.Join(" | ", _patchErrors.ToArray()));

                // Pinned wheel patches live on their own Harmony instance: if one of those patches
                // fails, only they are unpatched and the movement fixes above keep working.
                try
                {
                    Slots.Patch(extrasHarmony, Config, controller);
                    if (_patchErrors.Count > 0)
                        throw new InvalidOperationException("HarmonyX reported " + _patchErrors.Count + " failed patch(es): " + string.Join(" | ", _patchErrors.ToArray()));
                    Log.LogInfo("Pinned item wheel active (" + Slots.MainPages() + " page(s) x " + Slots.SlotsPerPage() + " slots).");
                }
                catch (Exception e)
                {
                    extrasHarmony.UnpatchSelf();
                    Log.LogError("[UIA Extras] Pinned wheel patching failed, those patches removed: " + e);
                }
            }
            catch (Exception e)
            {
                harmony.UnpatchSelf();
                Log.LogError("[UIA Extras] Patching failed, everything unpatched: " + e);
                return;
            }
            finally
            {
                HarmonyLib.Tools.Logger.MessageReceived -= watcher;
            }
            Log.LogInfo("Movement passthrough active.");

            // Debug hooks: only when the toggle is on at startup, on their own Harmony instance and try,
            // so a failure here can never unpatch the features above.
            if (DebugLogWheelKey.Value)
            {
                var debugHarmony = new Harmony(DebugHarmonyId);
                _patchErrors.Clear();
                HarmonyLib.Tools.Logger.MessageReceived += watcher;
                try
                {
                    WheelDebug.Patch(debugHarmony, controller);
                    if (_patchErrors.Count > 0)
                        throw new InvalidOperationException("HarmonyX reported " + _patchErrors.Count + " failed patch(es): " + string.Join(" | ", _patchErrors.ToArray()));
                }
                catch (Exception e)
                {
                    debugHarmony.UnpatchSelf();
                    WheelDebug.Active = false;
                    Log.LogError("[UIA Extras] Debug hooks failed, removed (movement fixes unaffected): " + e);
                }
                finally
                {
                    HarmonyLib.Tools.Logger.MessageReceived -= watcher;
                }
            }
        }

        // Throws unless one of our patches is registered on the method.
        // Optionally also requires specific patch kinds (a transpiler that silently went missing is worse
        // than a missing method).
        internal static void VerifyPatched(MethodBase m, string owner = HarmonyId, bool prefix = false, bool postfix = false, bool transpiler = false)
        {
            var info = Harmony.GetPatchInfo(m);
            bool ours = info != null && info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers).Any(p => p.owner == owner);
            if (!ours)
                throw new InvalidOperationException("No " + owner + " patch registered on " + m.DeclaringType?.Name + "." + m.Name);
            if ((prefix && !info.Prefixes.Any(p => p.owner == owner))
                || (postfix && !info.Postfixes.Any(p => p.owner == owner))
                || (transpiler && !info.Transpilers.Any(p => p.owner == owner)))
                throw new InvalidOperationException("Missing expected " + owner + " patch kind on " + m.DeclaringType?.Name + "." + m.Name);
        }

        private static void PatchTranslateInput(Harmony harmony)
        {
            var target = AccessTools.DeclaredMethod(typeof(InputNode), "TranslateInput");
            if (target == null)
            {
                Log.LogWarning("InputNode.TranslateInput not found; movement will stay blocked while the wheel is open.");
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(TranslateInputPatch), "Prefix"),
                finalizer: new HarmonyMethod(typeof(TranslateInputPatch), "Finalizer"));
            VerifyPatched(target);
            TranslateInputPatch.PatchSessionEnd(harmony);
        }

        // The wheel is opened through KeyboardShortcut.IsDown(), which also demands that no other key
        // is held, so it never opens while walking. Swap just UIA's calls for a looser check.
        private static void PatchWheelOpening(Harmony harmony, Type controller)
        {
            var update = AccessTools.Method(controller, "Update");
            if (update != null)
            {
                harmony.Patch(update, transpiler: new HarmonyMethod(typeof(Plugin), nameof(OpenCheckTranspiler)));
                VerifyPatched(update);
            }
            else
                Log.LogWarning("QuickUseWheelController.Update not found; wheel will not open while moving.");

            var shortcut = AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelShortcut");
            var held = shortcut == null ? null : AccessTools.Method(shortcut, "IsMainKeyPressed");
            if (held != null)
            {
                harmony.Patch(held, prefix: new HarmonyMethod(typeof(Plugin), nameof(HeldPrefix)));
                VerifyPatched(held);
            }
            else
                Log.LogWarning("QuickUseWheelShortcut.IsMainKeyPressed not found; wheel hold detection left as is.");
        }

        // UIA's Open nulls MovementContext.RotationAction. Put the saved value back right after, so
        // the camera stays pinned to the hands rotation and UIA's RestoreInput (which only restores
        // when the action is null) keeps working for mounted guns / cutscene blocks.
        private static void PatchOpen(Harmony harmony, Type controller)
        {
            var open = AccessTools.Method(controller, "Open");
            if (open == null)
            {
                Log.LogWarning("QuickUseWheelController.Open not found; camera may wobble while the wheel is open.");
                return;
            }
            harmony.Patch(open,
                prefix: new HarmonyMethod(typeof(Plugin), nameof(OpenPrefix)),
                postfix: new HarmonyMethod(typeof(Plugin), nameof(OpenPostfix)));
            VerifyPatched(open);
        }

        // UIA's wheel availability calls Item.CheckAction(null) in these methods. While a med/food use
        // runs, the player's ProcessStatus is non-None, so the inventory counts as locked and every
        // other item fails with InventoryBlockedError. Redirect those calls to CheckActionDuringUse.
        private static void PatchAvailability(Harmony harmony)
        {
            var inv = AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelInventory");
            var tr = new HarmonyMethod(typeof(Plugin), nameof(AvailabilityTranspiler));
            foreach (var name in new[] { "CreateWheelItem", "IsItemStillUsable", "GetAvailabilityRank" })
            {
                var m = inv == null ? null : AccessTools.Method(inv, name);
                if (m == null)
                {
                    Log.LogWarning("QuickUseWheelInventory." + name + " not found; items stay unavailable while a med is in use.");
                    continue;
                }
                harmony.Patch(m, transpiler: tr);
                VerifyPatched(m);
            }
        }

        // Close commits through UseSelectedItem -> Player.SetItemInHands, whose own vanilla
        // Item.CheckAction(null) fails with InventoryBlockedError while our hands process runs (it logs
        // "error null take to hands" and does nothing), even though the wheel now shows the item as
        // usable. When UIA has no pending/waiting entry of its own (pendingItem == null, e.g. access
        // delays off) and the only obstacle is that hands process, hand the item to Player.TryProceed
        // with scheduled=true instead: vanilla's own Process.Proceed then parks it as the scheduled
        // process and runs it when the current one ends (same as pressing a weapon key during a med use).
        // With delays on, UIA's TryProceed prefix still applies its delay on that call. Anything else
        // (not busy, other errors, UIA pending entry) goes through UIA's own path untouched.
        private static void PatchBusySelection(Harmony harmony, Type controller)
        {
            var m = AccessTools.Method(controller, "UseSelectedItem");
            if (m == null)
            {
                Log.LogWarning("QuickUseWheelController.UseSelectedItem not found; a selection made while hands are busy may do nothing.");
                return;
            }
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(Plugin), nameof(UseSelectedPrefix)));
            VerifyPatched(m, HarmonyId, prefix: true);
        }

        private static readonly Callback<IHandsController> IgnoreHandsResult = delegate { };

        private static bool UseSelectedPrefix(Player player, Item selectedItem, Item pendingItem)
        {
            if (pendingItem != null || player == null || selectedItem == null || !ShowItemsDuringUse.Value) return true;
            if (player.ProcessStatus != Player.EProcessStatus.Scheduled) return true;
            if (player.InventoryController == null || player.InventoryController.IsChangingWeapon) return true;
            if (player.IsInBufferZone && !player.CanManipulateWithHandsInBufferZone) return true;
            if (!CheckActionDuringUse(selectedItem, null).Succeeded) return true;
            player.TryProceed(selectedItem, IgnoreHandsResult, true);
            return false;
        }

        private static readonly MethodInfo CheckActionMethod = AccessTools.Method(typeof(Item), nameof(Item.CheckAction));

        private static IEnumerable<CodeInstruction> AvailabilityTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var repl = AccessTools.Method(typeof(Plugin), nameof(CheckActionDuringUse));
            var list = instructions.ToList();
            int n = 0;
            foreach (var ins in list)
            {
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodInfo m && m == CheckActionMethod)
                {
                    ins.opcode = OpCodes.Call;
                    ins.operand = repl;
                    n++;
                }
            }
            if (n == 0)
                Log.LogWarning("No Item.CheckAction call found in " + original.Name + "; items stay unavailable while a med is in use.");
            return list;
        }

        // Same as item.CheckAction(location), except a failure that is only the inventory lock from
        // the player's own running hands process (med/food use, or switching hands e.g. med -> weapon)
        // counts as success, so wheel items don't vanish for the length of the switch. Only the
        // Scheduled status is the hands process; Internal is a transient inventory operation and stays blocked.
        // Everything else (empty, unreachable, unexamined, other errors) is judged by UIA as before.
        internal static Option CheckActionDuringUse(Item item, ItemAddress location)
        {
            var result = item.CheckAction(location);
            if (result.Succeeded || !ShowItemsDuringUse.Value || !(result.Error is InventoryBlockedError)) return result;
            var owner = item.Owner as Player.PlayerInventoryController;
            var player = owner?.Player;
            if (player == null || player.ProcessStatus != Player.EProcessStatus.Scheduled)
                return result;
            return default(Option);
        }

        // Harmony injects "___x" as field "x", so the underscore-prefixed fields _isOpen / _player /
        // _previousRotationAction need four underscores. A wrong name makes HarmonyX log "Failed to
        // patch" and swallow it (no exception reaches the caller), see VerifyPatched.
        private static void OpenPrefix(bool ____isOpen, out bool __state)
        {
            __state = ____isOpen;
            if (!____isOpen) Slots.OnOpen();
        }

        private static void OpenPostfix(bool __state, bool ____isOpen, Player ____player, Action<Player> ____previousRotationAction)
        {
            if (!Enabled.Value || __state || !____isOpen || ____player == null || ____previousRotationAction == null) return;
            var mc = ____player.MovementContext;
            if (mc != null && mc.RotationAction == null)
                mc.RotationAction = ____previousRotationAction;
        }

        private static IEnumerable<CodeInstruction> OpenCheckTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            var isDown = AccessTools.Method(typeof(KeyboardShortcut), nameof(KeyboardShortcut.IsDown));
            var loose = AccessTools.Method(typeof(Plugin), nameof(LooseIsDown));
            var list = instructions.ToList();
            int n = 0;
            // Stack at the call is [ref shortcut]; Update is an instance method, so push the controller
            // (ldarg.0) as a second argument for the debug log.
            for (int i = 0; i < list.Count; i++)
            {
                var ins = list[i];
                if ((ins.opcode == OpCodes.Call || ins.opcode == OpCodes.Callvirt) && ins.operand is MethodInfo m && m == isDown)
                {
                    var ldarg = new CodeInstruction(OpCodes.Ldarg_0);
                    ldarg.labels.AddRange(ins.labels);
                    ins.labels.Clear();
                    ins.opcode = OpCodes.Call;
                    ins.operand = loose;
                    list.Insert(i, ldarg);
                    i++;
                    n++;
                }
            }
            if (n == 0)
                Log.LogWarning("Transpiler replaced 0 KeyboardShortcut.IsDown calls in QuickUseWheelController.Update; wheel will not open while moving.");
            else
                Log.LogInfo("Loosened " + n + " wheel shortcut check(s).");
            return list;
        }

        // Main key just went down and the shortcut's own modifiers are held. Other held keys (Shift
        // for sprint, movement keys, ...) are ignored. Only conflict handled: if the same main key
        // also matches another ENABLED UIA wheel shortcut with a strictly larger modifier set (e.g. H vs
        // H+LeftAlt), that more specific shortcut wins. A disabled wheel never takes the key.
        internal static bool LooseIsDown(ref KeyboardShortcut s, object controller)
        {
            bool result = LooseCore(ref s);
            if (WheelDebug.Active) WheelDebug.LogKey(ref s, controller, result);
            return result;
        }

        internal static bool LooseCore(ref KeyboardShortcut s)
        {
            if (!Enabled.Value) return s.IsDown();
            var main = s.MainKey;
            if (main == KeyCode.None || !UnityInput.Current.GetKeyDown(main)) return false;
            var mods = s.Modifiers.ToList();
            foreach (var m in mods)
                if (!UnityInput.Current.GetKey(m)) return false;

            foreach (var sc in _shortcuts)
            {
                if (sc.Enable != null && sc.Enable.GetValue(null) is ConfigEntry<bool> en && !en.Value) continue;
                var entry = sc.Key.GetValue(null) as ConfigEntry<KeyboardShortcut>;
                if (entry == null) continue;
                var o = entry.Value;
                if (o.MainKey != main) continue;
                var omods = o.Modifiers.ToList();
                if (omods.Count <= mods.Count || !mods.All(omods.Contains)) continue;
                if (omods.All(k => UnityInput.Current.GetKey(k))) return false;
            }
            return true;
        }

        // Hold/release tracking: IsPressed has the same flaw, but UIA falls back to GetAsyncKeyState
        // for the main key. Accept Unity's view of the main key first, keep the fallback otherwise.
        private static bool HeldPrefix(KeyboardShortcut shortcut, ref bool __result)
        {
            if (!Enabled.Value) return true;
            var main = shortcut.MainKey;
            if (main != KeyCode.None && UnityInput.Current.GetKey(main))
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    internal static class TranslateInputPatch
    {
        internal struct State
        {
            public bool Flipped;
            public float[] Axes;
        }

        private static float[] _buffer = new float[7];

        // True while we hold InputBlocked at false for the original call. If UIA closes or resets the
        // wheel inside that window (it sets InputBlocked=false itself), the finalizer must not flip it
        // back to true, or InputBlocked would stay stuck with no wheel open and block all input.
        private static bool _inCall, _sessionEnded;

        internal static void PatchSessionEnd(Harmony harmony)
        {
            var ctrl = AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelController");
            foreach (var name in new[] { "Close", "ResetSession" })
            {
                var m = ctrl == null ? null : AccessTools.Method(ctrl, name);
                if (m == null) { Plugin.Log.LogWarning("QuickUseWheelController." + name + " not found; InputBlocked restore guard disabled for it."); continue; }
                harmony.Patch(m, prefix: new HarmonyMethod(typeof(TranslateInputPatch), nameof(SessionEndPrefix)));
                Plugin.VerifyPatched(m);
            }
        }

        private static void SessionEndPrefix()
        {
            if (_inCall) _sessionEnded = true;
        }

        // axes: 0,1 move; 2,3 mouse rotate; 4,5 free look; 6 slow lean.
        private static void Prefix(InputNode __instance, List<ECommand> commands, ref float[] axes, out State __state)
        {
            __state = default;
            if (!(__instance is GamePlayerOwner owner) || !Plugin.Active(owner)) return;

            if (commands != null)
                for (int i = commands.Count - 1; i >= 0; i--)
                    if (!Plugin.IsAllowed(commands[i]))
                        commands.RemoveAt(i);

            if (axes != null && axes.Length > 5)
            {
                if (_buffer.Length != axes.Length) _buffer = new float[axes.Length];
                Array.Copy(axes, _buffer, axes.Length);
                _buffer[2] = _buffer[3] = _buffer[4] = _buffer[5] = 0f;
                __state.Axes = axes;
                axes = _buffer;
            }

            // UIA's prefixes read this; false makes them pass through for this call only.
            _sessionEnded = false;
            _inCall = true;
            Plugin.SetBlocked(false);
            __state.Flipped = true;
        }

        // Void finalizer: restores state and lets any exception propagate.
        private static void Finalizer(ref float[] axes, State __state)
        {
            if (__state.Axes != null) axes = __state.Axes;
            if (!__state.Flipped) return;
            _inCall = false;
            // Restore only if the wheel was not closed/reset meanwhile and nobody else changed it.
            if (!_sessionEnded && !Plugin.IsBlocked()) Plugin.SetBlocked(true);
            _sessionEnded = false;
        }
    }

    internal static class WheelDebug
    {
        private static Type _ctrl;
        private static FieldInfo _held, _isOpen, _holdTriggered, _gesture, _pending, _view, _pressedTime;
        private static PropertyInfo _blocked, _viewAvail;
        private static MethodInfo _tryGetPlayer;
        private static FieldInfo _items;
        private static readonly KeyCode[] Mods = { KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.LeftShift, KeyCode.LeftControl, KeyCode.LeftAlt };

        // True only when the debug hooks were installed at startup.
        internal static bool Active;

        internal static void Patch(Harmony harmony, Type controller)
        {
            Active = true;
            _ctrl = controller;
            _held = AccessTools.Field(controller, "_shortcutHeld");
            _isOpen = AccessTools.Field(controller, "_isOpen");
            _holdTriggered = AccessTools.Field(controller, "_shortcutHoldTriggered");
            _gesture = AccessTools.Field(controller, "_gestureMode");
            _pending = AccessTools.Field(controller, "_pendingOpenRequest");
            _view = AccessTools.Field(controller, "_view");
            _pressedTime = AccessTools.Field(controller, "_shortcutPressedTime");
            _blocked = AccessTools.Property(controller, "InputBlocked");
            _viewAvail = _view == null ? null : AccessTools.Property(_view.FieldType, "IsAvailable");
            _tryGetPlayer = AccessTools.Method(controller, "TryGetLocalPlayer");
            _items = AccessTools.Field(AccessTools.TypeByName("UseItemsAnywhere.Configuration"), "QuickUseWheelKey");

            Plugin.Log.LogInfo("Debug hooks: _items(QuickUseWheelKey) " + (_items != null ? "resolved" : "MISSING") + ", _held=" + (_held != null) + ", _isOpen=" + (_isOpen != null)
                + ", _view=" + (_view != null) + ", TryGetLocalPlayer=" + (_tryGetPlayer != null) + ", toggle Log Wheel Key=" + Plugin.DebugLogWheelKey.Value);
            // No Update hook: the wheel-key log runs from LooseIsDown (called from Update through our
            // IsDown transpiler), so Update gets no extra patch that could displace that transpiler.
            Hook(harmony, AccessTools.Method(controller, "Open"), nameof(OpenPrefix));
            Hook(harmony, AccessTools.Method(controller, "BeginShortcutGesture"), nameof(BeginPrefix));
            Hook(harmony, AccessTools.Method(controller, "ResetShortcutGesture"), nameof(ResetPrefix));
        }

        private static void Hook(Harmony h, MethodInfo m, string name)
        {
            if (m == null) { Plugin.Log.LogWarning("Debug hook target missing for " + name); return; }
            h.Patch(m, prefix: new HarmonyMethod(typeof(WheelDebug), name));
            Plugin.VerifyPatched(m, Plugin.DebugHarmonyId, prefix: true);
            Plugin.Log.LogInfo("Debug hook installed: " + m.Name + " -> " + name);
        }

        private static string F(FieldInfo f, object o)
        {
            if (f == null) return "?";
            try { return Convert.ToString(f.GetValue(o)); } catch (Exception e) { return "err:" + e.GetType().Name; }
        }

        private static string State(object inst)
        {
            string blocked = "?";
            try { if (_blocked != null) blocked = Convert.ToString(_blocked.GetValue(null, null)); } catch { }
            string pend = _pending == null ? "?" : (_pending.GetValue(null) != null).ToString();
            return "_shortcutHeld=" + F(_held, inst) + " _isOpen=" + F(_isOpen, inst) + " _shortcutHoldTriggered=" + F(_holdTriggered, inst)
                + " _gestureMode=" + F(_gesture, inst) + " _pendingOpenRequest!=null=" + pend + " InputBlocked=" + blocked
                + " focused=" + Application.isFocused;
        }

        // Called from LooseIsDown for every wheel shortcut UIA's Update checks.
        internal static void LogKey(ref KeyboardShortcut sc, object controller, bool result)
        {
            if (!Plugin.DebugLogWheelKey.Value) return;
            var main = sc.MainKey;
            if (main == KeyCode.None || !UnityInput.Current.GetKeyDown(main)) return;
            var sb = new System.Text.StringBuilder();
            sb.Append("[wheel-key] ").Append(sc).Append(" main ").Append(main).Append(" down; LooseIsDown=").Append(result).Append(" held:");
            foreach (var k in Mods) sb.Append(' ').Append(k).Append('=').Append(UnityInput.Current.GetKey(k) ? 1 : 0);
            sb.Append(' ').Append(State(controller));
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static void OpenPrefix(object __instance, object[] __args)
        {
            if (!Plugin.DebugLogWheelKey.Value) return;
            string viewAvail = "?", localOk = "?", inv = "?", alive = "?";
            try { if (_viewAvail != null) viewAvail = Convert.ToString(_viewAvail.GetValue(_view.GetValue(__instance), null)); } catch (Exception e) { viewAvail = "err:" + e.GetType().Name; }
            try
            {
                if (_tryGetPlayer != null)
                {
                    var a = new object[] { null, null };
                    localOk = Convert.ToString(_tryGetPlayer.Invoke(null, a));
                    var pl = a[0] as Player;
                    if (pl != null)
                    {
                        inv = pl.IsInventoryOpened.ToString();
                        alive = (pl.HealthController != null && pl.HealthController.IsAlive).ToString();
                    }
                }
            }
            catch (Exception e) { localOk = "err:" + e.GetType().Name; }
            Plugin.Log.LogInfo("[wheel-open] Open(" + (__args != null && __args.Length > 0 ? __args[0] : "?") + ") view.IsAvailable=" + viewAvail
                + " TryGetLocalPlayer=" + localOk + " IsInventoryOpened=" + inv + " alive=" + alive + " | " + State(__instance));
        }

        private static void BeginPrefix(object __instance, object[] __args)
        {
            if (!Plugin.DebugLogWheelKey.Value) return;
            Plugin.Log.LogInfo("[wheel-gesture] Begin(" + (__args != null && __args.Length > 0 ? __args[0] : "?") + ") time=" + Time.unscaledTime.ToString("F2"));
        }

        private static void ResetPrefix(object __instance)
        {
            if (!Plugin.DebugLogWheelKey.Value) return;
            Plugin.Log.LogInfo("[wheel-gesture] Reset time=" + Time.unscaledTime.ToString("F2") + " pressedAt=" + F(_pressedTime, __instance) + " heldWas=" + F(_held, __instance)
                + " triggeredWas=" + F(_holdTriggered, __instance) + " isOpen=" + F(_isOpen, __instance));
        }
    }
}
