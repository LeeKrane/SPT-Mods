// Show the insurance screen before joining another player's raid from Fika's main-menu player list.
//
// Fika's Join button (MainMenuUIScript.SetupPlayers) skips the whole matchmaker sequence and calls
// TarkovApplication.LocalGameMatching directly, so a joining PMC never passes the insurance screen
// that the host sees (location -> raid settings -> insurance -> lobby).
//
// After SetupPlayers builds the list, each PMC raid's Join button gets its click event swapped:
// the click opens the vanilla insurance screen instead, and Fika's original event is kept aside.
//   Next: pending insure operations are flushed, the screen closes, the server is asked again whether
//         the raid is still open, then Fika's original join runs.
//   Back: the screen closes and nothing else happens.
// Fika's join code is never patched, only invoked later. Scav raids, spectators, a join already in
// progress and a missing key for the map skip the screen and go straight to Fika.
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI.Matchmaker;
using EFT.UI.Screens;
using Fika.Core.Main.Utils;
using Fika.Core.Networking.Http;
using Fika.Core.Networking.Models.Presence;
using Fika.Core.UI;
using Fika.Core.UI.Custom;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Krane.JoinInsurance
{
    [BepInPlugin("krane.joininsurance", "Krane's JoinInsurance", "1.0.0")]
    [BepInDependency("com.fika.core")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Show the insurance screen before joining a PMC raid from the Fika main-menu player list.");
            new Harmony("krane.joininsurance").PatchAll(typeof(SetupPlayersPatch));
        }
    }

    [HarmonyPatch(typeof(MainMenuUIScript), "SetupPlayers")]
    internal static class SetupPlayersPatch
    {
        private static readonly AccessTools.FieldRef<MainMenuUIScript, List<GameObject>> PlayersRef =
            AccessTools.FieldRefAccess<MainMenuUIScript, List<GameObject>>("_players");

        [HarmonyPostfix]
        private static void Postfix(MainMenuUIScript __instance, ref FikaPlayerPresence[] responses)
        {
            // SetupPlayers adds exactly one entry to _players per response, in order,
            // after the old entries were cleared, so _players[i] belongs to responses[i].
            List<GameObject> players = PlayersRef(__instance);
            if (players == null || players.Count != responses.Length)
            {
                Plugin.Log.LogWarning($"Player list mismatch ({players?.Count} entries, {responses.Length} presences), Join buttons left untouched");
                return;
            }

            for (int i = 0; i < responses.Length; i++)
            {
                // Same condition Fika uses before it adds its join listener.
                RaidInformation? raid = responses[i].RaidInformation;
                if (responses[i].Activity != FikaUIGlobals.EFikaPlayerPresence.IN_RAID || !raid.HasValue
                    || raid.Value.Started || raid.Value.Side != ESideType.Pmc)
                {
                    continue;
                }

                MainMenuUIPlayer entry = players[i].GetComponent<MainMenuUIPlayer>();
                if (entry == null || entry.JoinButton == null)
                {
                    continue;
                }

                Button button = entry.JoinButton;
                Button.ButtonClickedEvent fikaJoin = button.onClick;
                RaidInformation info = raid.Value;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => OnJoinClicked(__instance, info, fikaJoin));
            }
        }

        private static readonly AccessTools.FieldRef<TarkovApplication, MainMenuShowOperation> MenuOperationRef =
            AccessTools.FieldRefAccess<TarkovApplication, MainMenuShowOperation>("_menuOperation");

        private static void OnJoinClicked(MainMenuUIScript mainMenu, RaidInformation raid, Button.ButtonClickedEvent fikaJoin)
        {
            // Fika ignores clicks while a join runs; let it, and never open the screen over one.
            if (mainMenu.JoinInProgress)
            {
                fikaJoin.Invoke();
                return;
            }

            MainMenuShowOperation menu = TarkovApplication.Exist(out TarkovApplication app) ? MenuOperationRef(app) : null;
            if (!Plugin.Enabled.Value || menu == null || FikaBackendUtils.IsSpectator || !PassesKeyCheck(menu, raid))
            {
                // A failing key/level check is left to Fika, which shows its own error before anything is insured.
                fikaJoin.Invoke();
                return;
            }

            var controller = new MatchmakerInsuranceScreen.InsuranceScreenController(
                menu.QuestController, menu.HealthController, menu.InventoryController,
                menu.iEftSession, menu.MatchmakerPlayersController);

            bool handled = false;
            controller.OnShowNextScreen += async () =>
            {
                if (handled || controller.Closed)
                {
                    return;
                }
                handled = true;
                try
                {
                    // TryCloseScreen closes backwards (moveForward false), which does not wait for the
                    // insure queue, so flush it here first; CloseAction's own flush then finds it empty.
                    if ((await menu.iEftSession.FlushOperationQueue()).Failed)
                    {
                        Plugin.Log.LogWarning("Insurance flush failed, join cancelled");
                        NotificationManager.DisplayWarningNotification("Insurance could not be saved, join cancelled");
                        handled = false;
                        return;
                    }
                    // Back or Escape during the flush: the user cancelled.
                    if (controller.Closed || !await controller.TryCloseScreen())
                    {
                        return;
                    }
                    if (!RaidStillJoinable(raid))
                    {
                        NotificationManager.DisplayWarningNotification("The raid has already started or ended");
                        return;
                    }
                    fikaJoin.Invoke();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"Join after insurance failed: {ex}");
                }
            };
            controller.ShowScreen(EScreenState.Queued);
        }

        // Mirrors Fika's join check: no keys needed, not a PMC, under the key level, or an examined key equipped.
        private static bool PassesKeyCheck(MainMenuShowOperation menu, RaidInformation raid)
        {
            IEftSession session = menu.iEftSession;
            var location = session?.LocationSettings?.locations?.Values
                .FirstOrDefault(l => l.Id == raid.Location);
            if (location == null)
            {
                return false;
            }
            InventoryController inventory = menu.InventoryController;
            return location.AccessKeys.Length == 0
                || raid.Side != ESideType.Pmc
                || session.Profile.Info.Level < location.MinPlayerLvlAccessKeys
                || inventory.Inventory.GetPlayerItems(EPlayerItems.Equipment)
                    .Any(item => inventory.Examined(item) && location.AccessKeys.Contains(item.StringTemplateId));
        }

        // The list can be minutes old by the time insuring is done; ask the server again.
        private static bool RaidStillJoinable(RaidInformation raid)
        {
            foreach (FikaPlayerPresence presence in FikaRequestHandler.GetPlayerPresences())
            {
                if (presence.RaidInformation.HasValue && presence.RaidInformation.Value.MatchId == raid.MatchId)
                {
                    return !presence.RaidInformation.Value.Started;
                }
            }
            return false;
        }
    }
}
