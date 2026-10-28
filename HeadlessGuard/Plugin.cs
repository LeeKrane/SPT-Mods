// Ask for confirmation before hosting a raid yourself instead of on a Fika headless client.
//
// Fika's matchmaker Start button runs one async lambda: with the "Use headless" toggle off it hosts the
// raid locally (FikaBackendUtils.CreateMatch), with it on it starts the raid on the selected headless.
// A Harmony postfix on MatchMakerUIScript.CreateMatchMakerUI swaps the Start button's click event after
// Fika builds the UI: Fika's original event is kept aside and a new click first checks the toggle.
//   Toggle on:  Fika's original event runs right away.
//   Toggle off: a Yes/No window opens. Yes runs Fika's original event, No does nothing.
// Fika's Start lambda itself is not patched, only invoked later, so a cancelled click leaves the screen
// untouched. While the window is open further Start clicks are ignored, and Yes does nothing once the
// matchmaker screen is gone. Without ItemUiContext no window can be shown, so the click is blocked.
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT.UI;
using Fika.Core.UI.Custom;
using HarmonyLib;
using UnityEngine.UI;

namespace Krane.HeadlessGuard
{
    [BepInPlugin("krane.headlessguard", "Krane's HeadlessGuard", "1.0.0")]
    [BepInDependency("com.fika.core")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Enabled;

        private void Awake()
        {
            Log = Logger;
            Enabled = Config.Bind("General", "Enabled", true,
                "Ask for confirmation before starting a raid that you host yourself instead of a headless client.");
            new Harmony("krane.headlessguard").PatchAll(typeof(CreateMatchMakerUIPatch));
        }
    }

    [HarmonyPatch(typeof(MatchMakerUIScript), "CreateMatchMakerUI")]
    internal static class CreateMatchMakerUIPatch
    {
        // Looked up without throwing, so a renamed field only disables the guard instead of breaking Fika's screen.
        private static readonly FieldInfo UiField = AccessTools.Field(typeof(MatchMakerUIScript), "_fikaMatchMakerUi");

        private const string Caption = "Host raid yourself?";
        private const string Message = "No headless client is selected. You will host this raid on your own machine.";

        [HarmonyPostfix]
        private static void Postfix(MatchMakerUIScript __instance)
        {
            MatchMakerUI ui = UiField?.GetValue(__instance) as MatchMakerUI;
            if (ui == null || ui.StartButton == null || ui.DedicatedToggle == null)
            {
                Plugin.Log.LogWarning("Fika matchmaker UI not found, Start button left untouched");
                return;
            }

            Button.ButtonClickedEvent fikaStart = ui.StartButton.onClick;
            bool asking = false;
            ui.StartButton.onClick = new Button.ButtonClickedEvent();
            ui.StartButton.onClick.AddListener(() =>
            {
                if (!Plugin.Enabled.Value || ui.DedicatedToggle.isOn)
                {
                    fikaStart.Invoke();
                    return;
                }

                if (asking)
                {
                    return;
                }

                ItemUiContext context = ItemUiContext.Instance;
                if (context == null)
                {
                    Plugin.Log.LogError("ItemUiContext missing, cannot ask for confirmation, raid not started");
                    return;
                }

                asking = true;
                context.ShowMessageWindow(Message,
                    () =>
                    {
                        asking = false;
                        // The matchmaker screen is destroyed when it is left, while this window may stay open.
                        if (__instance == null || ui == null || !__instance.isActiveAndEnabled)
                        {
                            Plugin.Log.LogWarning("Matchmaker screen closed, raid not started");
                            return;
                        }
                        fikaStart.Invoke();
                    },
                    () => asking = false,
                    Caption, 0f, true);
            });
        }
    }
}
