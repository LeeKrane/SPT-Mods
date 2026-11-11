using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ItemTweaks4Server;

/// <summary>
/// Gives a clone the final slots of its source, after every other mod changed them. ISB-Aishi's White Tusk EXFIL helmet shares its
/// slot objects with the vanilla EXFIL helmet, so later edits of the vanilla helmet reach it: WolfiksHeavyTrooperMasks adds its
/// heavy trooper masks to the mod_nvg slot, another mod adds a mod_equipment_002 slot. Our clone is a deep copy made at
/// Preload + 10, before those edits, so it has neither. Runs after PostLoad; slot ids stay the source's, parents point to the clone.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public sealed class SlotSync(ILogger<SlotSync> logger, TemplateTable templateTable, ICloner cloner) : IOnLoad
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

                var slots = source.Properties.Slots.Select(s => cloner.Clone(s)!).ToList();
                foreach (var slot in slots) slot.Parent = clone.Id;
                clone.Properties.Slots = slots;
                logger.LogInformation("[ItemTweaks4Server] {Name}: slots synced from its source ({Count} slots)", name, slots.Count);
            }
            catch (Exception e)
            {
                logger.LogError("[ItemTweaks4Server] {Name} slot sync failed: {Error}", name, e);
            }
        }
        return Task.CompletedTask;
    }
}
