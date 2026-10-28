using System.Reflection;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ModWeapons;

/// <summary>
/// Creates every clone in <see cref="Clones.All"/> (item, default preset, handbook and flea price) through WTT-ServerCommonLib
/// (db/**.json), then fixes slot parents and shares the source's weapon mastery.
/// Runs just after WTT-Armory (Preload + 2), before anything caches items or presets. A clone whose source item does not
/// exist (optional mod missing) is skipped with a log line; the others still load.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public sealed class ModWeaponItems(
    ILogger<ModWeaponItems> logger,
    WTTServerCommonLib.WTTServerCommonLib wttCommon,
    TemplateTable templateTable,
    GlobalTable globalTable) : IOnLoad
{
    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // WTT loads a directory recursively. Clones with an optional source have a directory of their own, so a missing
        // source skips just that directory instead of raising a WTT error.
        foreach (var group in Clones.All.GroupBy(d => d.ConfigDir))
        {
            try
            {
                var defs = group.ToList();
                var missing = defs.Where(d => !templateTable.Items.ContainsKey(new MongoId(d.SourceTpl))).ToList();
                foreach (var def in missing)
                {
                    if (def.SourceMod != null)
                        logger.LogInformation("[ModWeapons] {Mod} item {Source} ({Tpl}) not found, skipping {Name}", def.SourceMod, def.SourceName, def.SourceTpl, def.Name);
                    else
                        logger.LogError("[ModWeapons] Source item {Source} ({Tpl}) not found, {Name} skipped", def.SourceName, def.SourceTpl, def.Name);
                }
                if (missing.Count > 0) continue;

                await wttCommon.CustomItemServiceExtended.CreateCustomItems(Assembly.GetExecutingAssembly(), group.Key);
                foreach (var def in defs) Finish(def);
            }
            catch (Exception e)
            {
                logger.LogError("[ModWeapons] Item creation in {Dir} failed: {Error}", group.Key, e);
            }
        }
    }

    private void Finish(CloneDef def)
    {
        var newId = new MongoId(def.CloneTpl);
        if (!templateTable.Items.TryGetValue(newId, out var item))
        {
            logger.LogError("[ModWeapons] Item {Id} ({Name}) was not created (see WTT errors above), skipped", def.CloneTpl, def.Name);
            return;
        }

        // The clone keeps the source as parent of every slot, chamber and cartridge slot; point them at the new item.
        var slots = 0;
        foreach (var slot in item.Properties?.Slots ?? [])
        {
            slot.Parent = newId;
            slots++;
        }
        foreach (var chamber in item.Properties?.Chambers ?? [])
            chamber.Parent = newId;
        foreach (var cartridge in item.Properties?.Cartridges ?? [])
            cartridge.Parent = newId;

        if (def.PresetId != null && !globalTable.ItemPresets.ContainsKey(new MongoId(def.PresetId)))
            logger.LogWarning("[ModWeapons] Default preset {Id} of {Name} missing, flea offers will lack attachments", def.PresetId, def.Name);

        if (def.ShareMastery) ShareMastery(def);
        logger.LogInformation("[ModWeapons] Created {Name} ({Slots} slots)", def.Name, slots);
    }

    private void ShareMastery(CloneDef def)
    {
        var shared = 0;
        var source = new MongoId(def.SourceTpl);
        var newId = new MongoId(def.CloneTpl);
        foreach (var mastering in globalTable.Configuration.Mastering)
        {
            var templates = mastering.Templates.ToList();
            if (!templates.Contains(source) || templates.Contains(newId)) continue;
            templates.Add(newId);
            mastering.Templates = templates.ToArray();
            shared++;
        }
        if (shared == 0)
            logger.LogWarning("[ModWeapons] No mastery entry contains {Source}, {Name} has no mastery", def.SourceName, def.Name);
    }
}
