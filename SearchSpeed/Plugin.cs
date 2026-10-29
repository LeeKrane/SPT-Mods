// Faster container and body searches by a configurable multiplier.
//
// ActiveSearchContentOperation awaits Task.Delay twice: a fixed 2000 ms "Searching" delay in Open()
// (first search only), and per item Random(1..2) s / (1 + AttentionLootSpeed [+ SearchBuffSpeed on
// bodies]) in SearchContent(). Both are compiler-generated async state machines, so a transpiler
// swaps their Task.Delay(int) call for ScaledDelay, which divides the already skill-adjusted value.
// Skill values are never read or written: Attention, the search buff and the elite instant search
// (0 ms stays 0) keep working unchanged. Observed players do not run this code (Fika's
// ObservedInventoryController creates no search operation), so only the local player is affected.
using System;
using System.Collections.Generic;
using System.Linq;
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
            int patched = 0;
            foreach (var sm in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(t => typeof(IAsyncStateMachine).IsAssignableFrom(t)))
            {
                var moveNext = AccessTools.Method(sm, "MoveNext");
                if (moveNext == null) continue;
                harmony.Patch(moveNext, transpiler: transpiler);
                patched++;
            }

            // Open (initial delay) and SearchContent (per-item reveal).
            if (patched < 2)
                Log.LogWarning($"Patched {patched} search state machines, expected 2; some delays stay vanilla");
            Log.LogInfo($"Loaded, x{SpeedMultiplier.Value} search speed");
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            int replaced = 0;
            foreach (var ins in instructions)
            {
                if (ins.Calls(DelayMethod))
                {
                    ins.operand = ScaledDelayMethod;
                    replaced++;
                }
                yield return ins;
            }
            if (replaced == 0)
                Log.LogWarning($"No Task.Delay(int) in {original.DeclaringType?.Name}.{original.Name}");
        }

        // Read per call, so config edits apply to the next search without a restart.
        public static Task ScaledDelay(int ms)
        {
            float mult = SpeedMultiplier.Value;
            return Task.Delay(Math.Max(0, (int)(ms / (mult > 0f ? mult : 1f))));
        }
    }
}
