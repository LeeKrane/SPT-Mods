// Nightfall (ItemTweaks4Server template 6190dcad54cbff2ac9b7b8f4, a Surv12 clone with half its medUseTime) plays its use animation
// at 2x speed. The animation speed is the animator float UseTimeMultiplier, set through FirearmsAnimator.SetUseTimeMultiplier by:
// MedsController.Setup (1 + surgery skill, overwritten before the first limb locally), MedsInHandsOperation.GoNextBodyPart (local
// player and bots, once per limb) and, for Fika observed players, the same Setup inside ObservedMedsController.Create plus
// ObservedMedsOperation.HealthController_EffectRemovedEvent (next limb). While one of these runs for a Nightfall, a prefix on
// SetUseTimeMultiplier doubles the value the game computed. The heal itself is timed by medUseTime
// (ActiveHealthController med effect), not by the animation. Leave is a finalizer, so a throwing patched method cannot leave the scope open.
using System;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ItemTweaks
{
    internal static class MedSpeed
    {
        private static readonly MongoID NightfallId = "6190dcad54cbff2ac9b7b8f4";
        private const float Factor = 2f;

        // Main thread only (hands controllers and health events), so a plain counter is enough.
        private static int _scope;

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(MedSpeed), AccessTools.Method(typeof(FirearmsAnimator), nameof(FirearmsAnimator.SetUseTimeMultiplier)),
                prefix: nameof(SpeedPrefix));
            Plugin.SafePatch(harmony, typeof(MedSpeed), AccessTools.Method(typeof(Player.MedsController.MedsInHandsOperation), "GoNextBodyPart"),
                prefix: nameof(OperationPrefix), finalizer: nameof(Leave));

            // Fika is optional: without it there are no observed players.
            var observed = AccessTools.TypeByName("Fika.Core.Main.ObservedClasses.HandsControllers.ObservedMedsController");
            if (observed == null) return;
            Plugin.SafePatch(harmony, typeof(MedSpeed), AccessTools.Method(observed, "Create"),
                prefix: nameof(CreatePrefix), finalizer: nameof(Leave));
            Plugin.SafePatch(harmony, typeof(MedSpeed), AccessTools.Method(AccessTools.Inner(observed, "ObservedMedsOperation"), "HealthController_EffectRemovedEvent"),
                prefix: nameof(OperationPrefix), finalizer: nameof(Leave));
        }

        private static void SpeedPrefix(ref float speed)
        {
            if (_scope > 0) speed *= Factor;
        }

        // __state: whether this call entered the scope, so Leave only undoes its own Enter.
        private static void OperationPrefix(Player.MedsController.MedsInHandsOperation __instance, out bool __state)
        {
            __state = __instance?._controller?.Item?.TemplateId == NightfallId;
            if (__state) _scope++;
        }

        private static void CreatePrefix(Item item, out bool __state)
        {
            __state = item?.TemplateId == NightfallId;
            if (__state) _scope++;
        }

        private static void Leave(bool __state)
        {
            if (__state) _scope--;
        }
    }
}
