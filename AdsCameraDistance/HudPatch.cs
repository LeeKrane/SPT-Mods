using System;
using EFT.UI;
using EFT.UI.Screens;
using HarmonyLib;

namespace Krane.AdsCameraDistance
{
    // Shows the offset label on the battle screen's optic panel (the one that shows the scope zoom), the same
    // way for every sight type. Event-driven: drawn only on an adjustment.
    internal static class HudPatch
    {
        // Set by the plugin once this class applied.
        internal static bool Active;

        private static BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> _screen;

        [HarmonyPatch(typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>), "SubscribeOnPlayer")]
        [HarmonyPostfix]
        private static void SubscribePostfix(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> __instance)
        {
            _screen = __instance;
        }

        [HarmonyPatch(typeof(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType>), "Close")]
        [HarmonyPostfix]
        private static void ClosePostfix(BattleUIScreen<EftBattleUIScreen.EftBattleUIScreenController, EEftScreenType> __instance)
        {
            if (__instance == _screen)
                _screen = null;
        }

        public static void Show(string text)
        {
            if (!Active || _screen == null)
                return;
            try
            {
                _screen._opticCratePanel.Show(text);
                _screen.HideRaidTime();
            }
            catch (Exception e)
            {
                Active = false;
                Plugin.Log.LogError($"HUD label disabled: {e}");
            }
        }
    }
}
