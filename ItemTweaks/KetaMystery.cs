// K.E.T.A. (ItemTweaks4Server template 4f3445dcc79d0c4d911c6144) is an anomalous stim like ISB-Aishi's KTE stims: its buff stats show
// as ??????? instead of their values. ExaminedByDefault does not do this; ISB-Aishi masks its own KTE stims (hard-coded template list)
// in a postfix on StimulatorBuffsComponent.SetupStimulatorBuffsAttributes, which adds one StimulatorBuffAttribute per buff to the
// item. Same here: every StimulatorBuffAttribute of the item gets a masked name and value (by type, not list position, so it holds
// however the list was built). Display only, the buffs are unchanged.
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ItemTweaks
{
    internal static class KetaMystery
    {
        private static readonly MongoID KetaId = "4f3445dcc79d0c4d911c6144";
        private const string Mask = "???????";

        internal static void Init(Harmony harmony)
        {
            Plugin.SafePatch(harmony, typeof(KetaMystery), AccessTools.Method(typeof(StimulatorBuffsComponent), "SetupStimulatorBuffsAttributes"),
                postfix: nameof(Postfix));
        }

        private static void Postfix(Item item)
        {
            if (item?.Attributes == null || item.TemplateId != KetaId) return;
            foreach (var attribute in item.Attributes)
            {
                if (!(attribute is StimulatorBuffAttribute)) continue;
                attribute.Name = Mask;
                attribute.DisplayNameFunc = () => Mask;
                attribute.StringValue = () => Mask;
                attribute.FullStringValue = () => Mask;
            }
        }
    }
}
