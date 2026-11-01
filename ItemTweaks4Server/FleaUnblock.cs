using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemTweaks4Server;

/// <summary>
/// Puts the items of <see cref="FleaItems"/> on the flea: flea-sellable, and removed from the global item blacklist and the flea's own
/// custom blacklist, which both keep an item out of the dynamic (simulated player) offers. Runs after every mod added its items
/// (TraderRegistration + 11) and before RagfairCallbacks generates the offers.
/// The global item blacklist also keeps an item out of generated loot and rewards, so removing it can let those spawn it too.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 11)]
public sealed class FleaUnblock(
    ILogger<FleaUnblock> logger,
    TemplateTable templateTable,
    ItemConfig itemConfig,
    RagfairConfig ragfairConfig) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        foreach (var def in FleaItems.All)
        {
            try
            {
                var tpl = new MongoId(def.ItemTpl);
                if (!templateTable.Items.TryGetValue(tpl, out var item) || item.Properties == null)
                {
                    logger.LogInformation("[ItemTweaks4Server] {Mod} item {Name} ({Tpl}) not found, not put on the flea", def.SourceMod, def.Name, def.ItemTpl);
                    continue;
                }
                item.Properties.CanSellOnRagfair = true;
                item.Properties.CanRequireOnRagfair = true;
                var itemBlacklist = itemConfig.Blacklist.Remove(tpl);
                var fleaBlacklist = ragfairConfig.Dynamic.Blacklist.Custom.Remove(tpl);
                logger.LogInformation("[ItemTweaks4Server] {Name} put on the flea (was on item blacklist: {Item}, flea blacklist: {Flea})", def.Name, itemBlacklist, fleaBlacklist);
            }
            catch (Exception e)
            {
                logger.LogError("[ItemTweaks4Server] Putting {Name} on the flea failed: {Error}", def.Name, e);
            }
        }
        return Task.CompletedTask;
    }
}
