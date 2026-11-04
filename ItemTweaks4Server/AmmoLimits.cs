using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemTweaks4Server;

/// <summary>
/// Doubles the per-restock purchase limit (BuyRestrictionMax) of every single-round ammo offer of every trader until it is over
/// 600 (penetration 40+) or 1000 (other ammo). A finite stock below the new limit is raised to it. Ammo packs, grenades, flares,
/// the Zvezda flashbang and the 20x1mm disk are left alone. TypePriority sits after every trader registration (modded ones too),
/// SVM's restriction multipliers (Preload + 5) and <see cref="ItemIntegrations"/>, and before TraderCallbacks and RagfairCallbacks.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.TraderCallbacks - 1)]
public sealed class AmmoLimits(ILogger<AmmoLimits> logger, TemplateTable templateTable, TradersTable tradersTable) : IOnLoad
{
    private const string AmmoParent = "5485a8684bdc2da71d8b4567";
    private const string Zvezda = "5e85a9f4add9fe03027d9bf1";
    private static readonly HashSet<string> ExcludedCalibers = ["Caliber40x46", "Caliber26x75", "Caliber20x1mm"];
    private const double HighPen = 40;
    private const int HighPenTarget = 600;
    private const int Target = 1000;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try { Apply(); }
        catch (Exception e) { logger.LogError("[ItemTweaks4Server] Ammo purchase limits failed: {Error}", e); }
        return Task.CompletedTask;
    }

    private void Apply()
    {
        var offers = 0;
        var stocks = 0;
        foreach (var (traderId, trader) in tradersTable)
        {
            if (traderId == Traders.FENCE || trader.Assort == null) continue;
            foreach (var root in trader.Assort.Items.Where(i => i.SlotId == "hideout"))
            {
                if (root.Upd?.BuyRestrictionMax is not > 0 || !templateTable.Items.TryGetValue(root.Template, out var tpl)) continue;
                var props = tpl.Properties;
                if (tpl.Parent != new MongoId(AmmoParent) || props == null || props.AmmoType == "grenade" || tpl.Id == new MongoId(Zvezda)
                    || (props.Caliber != null && ExcludedCalibers.Contains(props.Caliber)))
                    continue;

                var target = (props.PenetrationPower ?? 0) >= HighPen ? HighPenTarget : Target;
                var limit = root.Upd.BuyRestrictionMax.Value;
                while (limit <= target) limit *= 2;
                if (limit == root.Upd.BuyRestrictionMax) continue;
                root.Upd.BuyRestrictionMax = limit;
                offers++;
                if (root.Upd.UnlimitedCount != true && root.Upd.StackObjectsCount < limit)
                {
                    root.Upd.StackObjectsCount = limit;
                    stocks++;
                }
            }
        }
        logger.LogInformation("[ItemTweaks4Server] Ammo purchase limits doubled on {Offers} trader offers ({Stocks} stocks raised)", offers, stocks);
    }
}
