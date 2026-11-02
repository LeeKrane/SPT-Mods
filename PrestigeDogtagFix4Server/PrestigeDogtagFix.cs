using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigeDogtagFix4Server;

/// <summary>
/// Adds every prestige dogtag to each container grid filter that already whitelists one (SICC, dogtag case and clones).
/// Runs after every OnLoad at or below PostLoad so dogtags added by other mods (WTT-ContentBackport prestige 5-6) exist.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 1)]
public sealed partial class PrestigeDogtagFix(ILogger<PrestigeDogtagFix> logger, TemplateTable templateTable) : IOnLoad
{
    // Unanchored: WTT-ServerCommonLib renames its clones to "[<mod>]_(item_barter_dogtags_bear_prestige_05)".
    [GeneratedRegex(@"item_barter_dogtags_(bear|usec)_prestige_\d+(?!\d)", RegexOptions.IgnoreCase)]
    private static partial Regex PrestigeName();

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var dogtags = templateTable.Items
            .Where(kv => kv.Value.Properties?.DogTagQualities == true && kv.Value.Name != null && PrestigeName().IsMatch(kv.Value.Name))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Parent);
        if (dogtags.Count == 0)
        {
            logger.LogWarning("[PrestigeDogtagFix4Server] No prestige dogtag templates found, nothing to do");
            return Task.CompletedTask;
        }

        var added = 0;
        var containers = new List<string>();
        foreach (var item in templateTable.Items.Values)
        {
            var before = added;
            foreach (var grid in item.Properties?.Grids ?? [])
            {
                foreach (var filter in grid.Properties?.Filters ?? [])
                {
                    var set = filter.Filter;
                    if (set == null || !set.Overlaps(dogtags.Keys)) continue;
                    var excluded = filter.ExcludedFilter;
                    foreach (var (tpl, parent) in dogtags)
                    {
                        if (excluded != null && (excluded.Contains(tpl) || excluded.Contains(parent))) continue;
                        if (set.Add(tpl)) added++;
                    }
                }
            }
            if (added > before) containers.Add($"{item.Name} ({item.Id})");
        }

        logger.LogInformation("[PrestigeDogtagFix4Server] Added {Added} prestige dogtag entries ({Tags} dogtag templates) to {Count} container(s): {Containers}",
            added, dogtags.Count, containers.Count, string.Join(", ", containers));
        return Task.CompletedTask;
    }
}
