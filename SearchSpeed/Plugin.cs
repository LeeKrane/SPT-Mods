// Faster container and body searches by a configurable multiplier.
//
// ActiveSearchContentOperation awaits Task.Delay twice: a fixed 2000 ms "Searching" delay in Open()
// (first search only), and per item Random(1..2) s / (1 + AttentionLootSpeed [+ SearchBuffSpeed on
// bodies]) in SearchContent(). Both are compiler-generated async state machines, so a transpiler
// swaps their Task.Delay(int) call for ScaledDelay, which divides the already skill-adjusted value.
// Skill values are never read or written: Attention, the search buff and the elite instant search
// (0 ms stays 0) keep working unchanged. Observed players do not run this code (Fika's
// ObservedInventoryController creates no search operation), so only the local player is affected.
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Krane.SearchSpeed
{
    [BepInPlugin("krane.searchspeed", "Krane's SearchSpeed", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string TargetType = "EFT.InventoryLogic.Operations.ActiveSearchContentOperation";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> SpeedMultiplier;

        private static readonly MethodInfo DelayMethod = typeof(Task).GetMethod(nameof(Task.Delay), new[] { typeof(int) });
        private static readonly MethodInfo ScaledDelayMethod = AccessTools.Method(typeof(Plugin), nameof(ScaledDelay));

        private void Awake()
        {
            Log = Logger;
            SpeedMultiplier = Config.Bind("General", "SpeedMultiplier", 1.25f, new ConfigDescription(
                "Search speed multiplier on top of skills. 1.25 = 25% faster (delays / 1.25), 1 = vanilla.",
                new AcceptableValueRange<float>(0.1f, 10f)));

            var type = AccessTools.TypeByName(TargetType);
            if (type == null)
            {
                Log.LogError($"{TargetType} not found, search speed unchanged");
                return;
            }

            var harmony = new Harmony("krane.searchspeed");
            var transpiler = new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(Transpiler)));
            // Only Open (initial delay) and SearchContent (per-item reveal), so any other async method
            // added to the operation later keeps its own timing.
            foreach (var name in StateMachines)
            {
                var sm = type.GetNestedType(name, BindingFlags.Public | BindingFlags.NonPublic);
                var moveNext = sm != null && typeof(IAsyncStateMachine).IsAssignableFrom(sm)
                    ? AccessTools.Method(sm, "MoveNext") : null;
                if (moveNext == null)
                {
                    Log.LogWarning($"{TargetType}+{name} not found, that delay stays vanilla");
                    continue;
                }
                harmony.Patch(moveNext, transpiler: transpiler);
            }

            if (_replaced < StateMachines.Length)
                Log.LogWarning($"Scaled {_replaced} search delays, expected {StateMachines.Length}; the rest stay vanilla");
            if (_replaced > 0)
                Log.LogInfo($"Loaded, x{SpeedMultiplier.Value} search speed");
        }

        private static readonly string[] StateMachines = { "CG_Open", "CG_SearchContent" };
        private static int _replaced;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            foreach (var ins in instructions)
            {
                if (ins.Calls(DelayMethod))
                {
                    ins.operand = ScaledDelayMethod;
                    _replaced++;
                }
                yield return ins;
            }
        }

        // Read per call, so config edits apply to the next search without a restart. The config range
        // keeps the multiplier positive; ms <= 0 (instant search, Timeout.Infinite) passes through.
        public static Task ScaledDelay(int ms) =>
            Task.Delay(ms > 0 ? (int)(ms / SpeedMultiplier.Value) : ms);
    }
}
