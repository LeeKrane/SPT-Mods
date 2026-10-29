// Client-side trader tweaks. Currently: lock Hideout Harry (acidphantasm-harryhideout) for this player only.
//
// EFT decides whether a trader is usable through Profile.TraderInfo.Available (!Disabled && Unlocked && !banned).
// Everything reads it: the trader card state, TraderScreensGroup.SelectTrader (returns early when not Available),
// DisplayableTraders, Insurers, Repairers, Medic and flea offer views.
// A postfix on the getter returns false for Harry's trader id while the "Disable Hideout Harry" option is on, so Harry is
// treated like a trader that is not unlocked yet. Nothing is sent to the server and the profile is untouched.
//
// TraderCard.SetCardState only applies a new state when the trader is Available, so a card that already looked
// available would stay that way after the toggle. A second postfix forces the Locked card state in that case.
// Changing the option re-runs UpdateView on every shown Harry card, so it applies live. A trader screen that is
// already open on Harry's page keeps showing his assortment until it is left.
// Without the Harry server mod no trader has this id, so nothing matches and nothing happens.
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Krane.TraderTweaks
{
    [BepInPlugin("krane.tradertweaks", "Krane's TraderTweaks", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal const string HarryId = "67419e9d0d4541ce671543bb";
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> DisableHarry;

        private void Awake()
        {
            Log = Logger;
            DisableHarry = Config.Bind("Hideout Harry", "Disable Hideout Harry", false,
                "Lock Hideout Harry (acidphantasm-harryhideout) for you only: he shows as locked and cannot be opened or used. Nothing changes on the server.");
            DisableHarry.SettingChanged += (s, e) => RefreshCards();
            new Harmony("krane.tradertweaks").PatchAll(typeof(AvailablePatch).Assembly);
        }

        internal static readonly FieldInfo TraderField = AccessTools.Field(typeof(TraderCard), "_trader");

        private static void RefreshCards()
        {
            if (TraderField == null)
            {
                return;
            }
            foreach (TraderCard card in Resources.FindObjectsOfTypeAll<TraderCard>())
            {
                var info = TraderField.GetValue(card) as EFT.Profile.TraderInfo;
                if (info != null && info.Id == HarryId)
                {
                    card.UpdateView();
                }
            }
        }
    }

    [HarmonyPatch(typeof(EFT.Profile.TraderInfo), nameof(EFT.Profile.TraderInfo.Available), MethodType.Getter)]
    internal static class AvailablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(EFT.Profile.TraderInfo __instance, ref bool __result)
        {
            if (__result && Plugin.DisableHarry.Value && __instance.Id == Plugin.HarryId)
            {
                __result = false;
            }
        }
    }

    [HarmonyPatch(typeof(TraderCard), nameof(TraderCard.SetCardState))]
    internal static class SetCardStatePatch
    {
        [HarmonyPostfix]
        private static void Postfix(TraderCard __instance)
        {
            var info = Plugin.TraderField?.GetValue(__instance) as EFT.Profile.TraderInfo;
            if (Plugin.DisableHarry.Value && info != null && info.Id == Plugin.HarryId && !info.Available)
            {
                __instance.ApplyState(TraderCard.ETraderCardState.Locked);
            }
        }
    }
}
