// K.E.T.A. (ItemTweaks4Server template 4f3445dcc79d0c4d911c6144) is an anomalous stim like ISB-Aishi's KTE stims: its buff stats show
// as ??????? instead of their values. ExaminedByDefault does not do this; ISB-Aishi masks its own KTE stims (hard-coded template list)
// in a postfix on StimulatorBuffsComponent.SetupStimulatorBuffsAttributes, which adds one attribute per buff to the item. Same here:
// every attribute that method added gets a masked name and value. Display only, the buffs are unchanged.
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
                prefix: nameof(Prefix), postfix: nameof(Postfix));
        }

        private static void Prefix(Item __0, out int __state)
        {
            __state = __0?.Attributes?.Count ?? 0;
        }

        private static void Postfix(Item __0, int __state)
        {
            if (__0?.Attributes == null || __0.TemplateId != KetaId) return;
            for (int i = __state; i < __0.Attributes.Count; i++)
            {
                var attribute = __0.Attributes[i];
                attribute.Name = Mask;
                attribute.DisplayNameFunc = () => Mask;
                attribute.StringValue = () => Mask;
                attribute.FullStringValue = () => Mask;
            }
        }
    }
}
