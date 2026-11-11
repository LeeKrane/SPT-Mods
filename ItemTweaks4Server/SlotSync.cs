using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemTweaks4Server;

/// <summary>
/// Makes a clone share its source's final slot list. ISB-Aishi's AishiCosmeticSlots (priority int.MaxValue - 10) gives each of its
/// cosmetic variants, such as the White Tusk EXFIL helmet, a copy of its vanilla source's slots after every other mod edited them
/// (WolfiksHeavyTrooperMasks' heavy trooper masks in mod_nvg, an extra mod_equipment_002 slot). Our clone was copied from White Tusk at
/// Preload + 10, before that, so it would miss them; this runs right after Aishi's step. Slot parents stay the vanilla helmet's, as on
/// White Tusk.
/// </summary>
[Injectable(TypePriority = int.MaxValue - 5)]
public sealed class SlotSync(ILogger<SlotSync> logger, TemplateTable templateTable) : IOnLoad
{
    // (clone, source, name)
    private static readonly (string Clone, string Source, string Name)[] Pairs =
    [
        (Ids.ExfilBdBlackCamo, Ids.ExfilWhiteTusk, "EXFIL helmet (BD Black Camo)"),
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var (cloneTpl, sourceTpl, name) in Pairs)
        {
            try
            {
                if (!templateTable.Items.TryGetValue(new MongoId(cloneTpl), out var clone) || clone.Properties == null) continue;
                if (!templateTable.Items.TryGetValue(new MongoId(sourceTpl), out var source) || source.Properties?.Slots == null) continue;

                clone.Properties.Slots = source.Properties.Slots;
                logger.LogInformation("[ItemTweaks4Server] {Name}: shares its source's slot list", name);
            }
            catch (Exception e)
            {
                logger.LogError("[ItemTweaks4Server] {Name} slot sharing failed: {Error}", name, e);
            }
        }
        return Task.CompletedTask;
    }
}
