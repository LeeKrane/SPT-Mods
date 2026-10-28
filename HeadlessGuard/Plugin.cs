// Ask for confirmation before hosting a raid yourself instead of on a Fika headless client.
//
// Fika's matchmaker Start button runs one async lambda: with the "Use headless" toggle off it hosts the
// raid locally (FikaBackendUtils.CreateMatch), with it on it starts the raid on the selected headless.
// After MatchMakerUIScript.CreateMatchMakerUI builds the UI, the Start button's click event is swapped:
// Fika's original event is kept aside and a new click first checks the toggle.
//   Toggle on:  Fika's original event runs right away.
//   Toggle off: a Yes/No window opens. Yes runs Fika's original event, No does nothing.
// Fika's own code is never patched, only invoked later, so a cancelled click leaves the screen untouched.
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
        private static readonly AccessTools.FieldRef<MatchMakerUIScript, MatchMakerUI> UiRef =
            AccessTools.FieldRefAccess<MatchMakerUIScript, MatchMakerUI>("_fikaMatchMakerUi");

        private const string Caption = "Host raid yourself?";
        private const string Message = "No headless client is selected. You will host this raid on your own machine.";

        [HarmonyPostfix]
        private static void Postfix(MatchMakerUIScript __instance)
        {
            MatchMakerUI ui = UiRef(__instance);
            if (ui == null || ui.StartButton == null || ui.DedicatedToggle == null)
            {
                Plugin.Log.LogWarning("Fika matchmaker UI not found, Start button left untouched");
                return;
            }

            Button.ButtonClickedEvent fikaStart = ui.StartButton.onClick;
            ui.StartButton.onClick = new Button.ButtonClickedEvent();
            ui.StartButton.onClick.AddListener(() =>
            {
                if (!Plugin.Enabled.Value || ui.DedicatedToggle.isOn)
                {
                    fikaStart.Invoke();
                    return;
                }

                ItemUiContext context = ItemUiContext.Instance;
                if (context == null)
                {
                    Plugin.Log.LogWarning("ItemUiContext missing, starting without confirmation");
                    fikaStart.Invoke();
                    return;
                }

                context.ShowMessageWindow(Message, () => fikaStart.Invoke(), null, Caption, 0f, true);
            });
        }
    }
}
