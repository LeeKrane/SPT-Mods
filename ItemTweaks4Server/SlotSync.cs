using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemTweaks4Server;

/// <summary>
/// Makes a clone share its source's slot list. ISB-Aishi's White Tusk EXFIL helmet shares its slot list with the vanilla EXFIL helmet,
/// so edits other mods make to the vanilla helmet reach it: WolfiksHeavyTrooperMasks' heavy trooper masks in mod_nvg and an extra
/// mod_equipment_002 slot. These arrive after every IOnLoad step (White Tusk still has 4 slots at PostLoad + 100), so copying the slots
/// at any load stage misses them; sharing the same list object picks them up whenever they are added. Slot parents stay the vanilla
/// helmet's, as on White Tusk.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
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
