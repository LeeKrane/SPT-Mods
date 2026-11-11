using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Cloners;

namespace ItemTweaks4Server;

/// <summary>
/// Makes a clone share its source's final slot list. ISB-Aishi's AishiCosmeticSlots (priority int.MaxValue - 10) gives each of its
/// cosmetic variants, such as the White Tusk EXFIL helmet, a copy of its vanilla source's slots after every other mod edited them
/// (WolfiksHeavyTrooperMasks' heavy trooper masks in mod_nvg, an extra mod_equipment_002 slot). Our clone was copied from White Tusk at
/// Preload + 10, before that, so it would miss them; this runs right after Aishi's step. Slot parents stay the vanilla helmet's, as on
/// White Tusk. Also gives Secure container Omega the final grid filters of Theta (see SyncOmegaFilters) and drops the M4SD-K from the JailBrake's conflict list (see FixJailBrakeConflict).
/// </summary>
[Injectable(TypePriority = int.MaxValue - 5)]
public sealed class SlotSync(ILogger<SlotSync> logger, TemplateTable templateTable, ICloner cloner) : IOnLoad
{
    // (clone, source, name)
    private static readonly (string Clone, string Source, string Name)[] Pairs =
    [
        (Ids.ExfilBdBlackCamo, Ids.ExfilWhiteTusk, "EXFIL helmet (BD Black Camo)"),
    ];

    // Omega grid index -> Theta grid index whose final filters it takes (4x2 <- 4x2, 1x2 <- 1x2, 5x1 <- 4x2).
    private static readonly int[] OmegaFromTheta = [0, 1, 0];

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
        Guard("Secure container Omega grid filters", SyncOmegaFilters);
        Guard("JailBrake / M4SD-K conflict", FixJailBrakeConflict);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Omega's grid filters come from Theta's database entry (db JSON). Other mods change every secure container's filters at runtime
    /// (SVM's RemoveSecureContainerFilters widens them), so Omega would stay stricter than all others, e.g. refusing the Med case and the
    /// MediPouch. Copy Theta's final filters instead.
    /// </summary>
    private void SyncOmegaFilters()
    {
        if (!templateTable.Items.TryGetValue(new MongoId(Ids.SecureContainerOmega), out var omega) || omega.Properties?.Grids == null) return;
        var theta = templateTable.Items[new MongoId(Ids.Theta)].Properties?.Grids?.ToList() ?? throw new InvalidOperationException("Theta has no grids");
        var grids = omega.Properties.Grids.ToList();
        for (var i = 0; i < grids.Count && i < OmegaFromTheta.Length; i++)
            grids[i].Properties!.Filters = cloner.Clone(theta[OmegaFromTheta[i]].Properties!.Filters);
        logger.LogInformation("[ItemTweaks4Server] Secure container Omega: grid filters copied from Theta");
    }

    /// <summary>
    /// The SAI JailBrake's ConflictingItems lists the normal Griffin M4SD-K suppressor but not ISB-Aishi's FDE one, so the normal one
    /// cannot be fitted with a JailBrake (the JailBrake sits in the SAI handguard's muzzle slot, the M4SD-K on the barrel's muzzle device).
    /// Removes it from that list so it behaves like the FDE one. Nothing else lists the JailBrake and no list of the M4SD-K does.
    /// </summary>
    private void FixJailBrakeConflict()
    {
        if (!templateTable.Items.TryGetValue(new MongoId(Ids.JailBrake), out var jailBrake) || jailBrake.Properties?.ConflictingItems is not { } conflicts) return;
        var removed = conflicts.Remove(new MongoId(Ids.M4sdK));
        logger.LogInformation("[ItemTweaks4Server] SAI JailBrake no longer conflicts with the M4SD-K (was listed: {Removed}, FDE one listed: {Fde})", removed, conflicts.Contains(new MongoId(Ids.M4sdKFde)));
    }

    private void Guard(string what, Action action)
    {
        try { action(); }
        catch (Exception e) { logger.LogError("[ItemTweaks4Server] {What} failed: {Error}", what, e); }
    }
}
