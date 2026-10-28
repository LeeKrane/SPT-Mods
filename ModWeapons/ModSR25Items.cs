using System.Reflection;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModWeapons;

/// <summary>
/// Creates the MOD-SR-25 item, its default preset, handbook and flea price through WTT-ServerCommonLib
/// (db/CustomItems/ModSR25.json), then fixes slot parents and shares SR-25's weapon mastery.
/// Runs just after WTT-Armory (Preload + 2), before anything caches items or presets.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public sealed class ModSR25Items(
    ILogger<ModSR25Items> logger,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    TemplateTable templateTable,
    GlobalTable globalTable) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            await wttCommon.CustomItemServiceExtended.CreateCustomItems(Assembly.GetExecutingAssembly());

            var newId = new MongoId(Ids.ModSr25);
            if (!templateTable.Items.TryGetValue(newId, out var item))
            {
                logger.LogError("[ModWeapons] Item {Id} was not created (see WTT errors above), mod inactive", Ids.ModSr25);
                return;
            }

            // The clone keeps SR-25 as parent of every slot and chamber; point them at the new item.
            var slots = 0;
            foreach (var slot in item.Properties?.Slots ?? [])
            {
                slot.Parent = newId;
                slots++;
            }
            foreach (var chamber in item.Properties?.Chambers ?? [])
                chamber.Parent = newId;

            if (!globalTable.ItemPresets.ContainsKey(new MongoId(Ids.ModSr25Preset)))
                logger.LogWarning("[ModWeapons] Default preset {Id} missing, flea offers will lack attachments", Ids.ModSr25Preset);

            ShareMastery(newId);
            logger.LogInformation("[ModWeapons] Created MOD-SR-25 ({Slots} slots)", slots);
        }
        catch (Exception e)
        {
            logger.LogError("[ModWeapons] Item creation failed: {Error}", e);
        }
    }

    private void ShareMastery(MongoId newId)
    {
        var shared = 0;
        var sr25 = new MongoId(Ids.Sr25);
        foreach (var mastering in globalTable.Configuration.Mastering)
        {
            var templates = mastering.Templates.ToList();
            if (!templates.Contains(sr25) || templates.Contains(newId)) continue;
            templates.Add(newId);
            mastering.Templates = templates.ToArray();
            shared++;
        }
        if (shared == 0)
            logger.LogWarning("[ModWeapons] No mastery entry contains SR-25, MOD-SR-25 has no mastery");
    }
}
