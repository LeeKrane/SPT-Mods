// Remember the insurer picked in the insurance dropdown and preselect it next time.
//
// The backend session builds a new InsuranceCompany every time its Profile is set (login, after each
// raid), so InsuranceCompany.SelectedInsurerId starts out null and InsurerParametersPanel.Show falls
// back to the first insurer in the list (Prapor).
//
// InsurerSelected is the dropdown callback. Its postfix stores the chosen trader id in the config file,
// so the choice also survives a game restart. Show's prefix writes the stored id back into
// SelectedInsurerId when that trader is still an available insurer; Show then selects it in the
// dropdown on its own. Show also fires InsurerSelected itself, so saving is skipped while Show runs:
// only a pick by the player is stored, never a fallback. A depth counter, not a flag, so a nested Show
// cannot clear the guard of an outer one. Picks are stored even while disabled, so re-enabling
// preselects the latest pick.
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using EFT.UI.Insurance;
using HarmonyLib;

namespace Krane.RememberInsurer
{
    [BepInPlugin("krane.rememberinsurer", "Krane's RememberInsurer", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> LastInsurerId;

        private void Awake()
        {
            Enabled = Config.Bind("General", "Enabled", true,
                "Preselect the last insurer you picked when an insurance screen opens.");
            LastInsurerId = Config.Bind("State", "LastInsurerId", "",
                new ConfigDescription("Trader id of the last insurer you picked. Set by the plugin.", null,
                    new ConfigurationManagerAttributes { Browsable = false }));
            new Harmony("krane.rememberinsurer").PatchAll(typeof(InsurerPanelPatches));
        }
    }

    // Read by ConfigurationManager through reflection on the type name.
    internal sealed class ConfigurationManagerAttributes
    {
        public bool? Browsable;
    }

    [HarmonyPatch(typeof(InsurerParametersPanel))]
    internal static class InsurerPanelPatches
    {
        private static int _showDepth;

        [HarmonyPatch(nameof(InsurerParametersPanel.Show))]
        [HarmonyPrefix]
        private static void ShowPrefix(InsuranceCompany insurance)
        {
            _showDepth++;
            if (!Plugin.Enabled.Value || insurance == null)
                return;
            string id = Plugin.LastInsurerId.Value;
            if (!string.IsNullOrEmpty(id) && insurance.Insurers.Any(trader => trader.Id == id))
                insurance.SelectedInsurerId = id;
        }

        [HarmonyPatch(nameof(InsurerParametersPanel.Show))]
        [HarmonyFinalizer]
        private static void ShowFinalizer()
        {
            _showDepth--;
        }

        [HarmonyPatch(nameof(InsurerParametersPanel.InsurerSelected))]
        [HarmonyPostfix]
        private static void InsurerSelectedPostfix(InsuranceCompany ____insurance)
        {
            if (_showDepth > 0)
                return;
            string id = ____insurance?.SelectedInsurerId;
            if (!string.IsNullOrEmpty(id))
                Plugin.LastInsurerId.Value = id;
        }
    }
}
