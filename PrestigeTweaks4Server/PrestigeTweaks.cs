using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigeTweaks4Server;

public sealed record PrestigeTweaksConfig
{
    [JsonPropertyName("Enabled")]
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Copies prestige 4's transfer settings (stash height, item filter, skill and mastery multipliers) to every later
/// prestige element. SVM edits only elements 0-3 in its OnLoad (priority 100005, after the prestige backport's 100003);
/// this runs after SVM's last OnLoad (SVMPostLoad, 100040).
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public sealed class PrestigeTweaks(ILogger<PrestigeTweaks> logger, TemplateTable templateTable, ModHelper modHelper) : IOnLoad
{
    private const int SourceIndex = 3;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var config = modHelper.GetJsonDataFromFile<PrestigeTweaksConfig>(folder, "config.json") ?? new PrestigeTweaksConfig();
            if (!config.Enabled)
            {
                logger.LogInformation("[PrestigeTweaks4Server] Disabled in config.json");
                return Task.CompletedTask;
            }

            var elements = templateTable.Prestige?.Elements;
            if (elements == null || elements.Count <= SourceIndex + 1) return Task.CompletedTask;

            var src = elements[SourceIndex].TransferConfigs;
            var srcStash = src.StashConfig;
            var srcFilters = srcStash.Filters;
            var copied = new List<int>();
            for (var i = SourceIndex + 1; i < elements.Count; i++)
            {
                var dst = elements[i].TransferConfigs;
                dst.StashConfig.XCellCount = srcStash.XCellCount;
                dst.StashConfig.Filters = srcFilters == null ? null : new()
                {
                    IncludedItems = srcFilters.IncludedItems?.ToList()!,
                    ExcludedItems = srcFilters.ExcludedItems?.ToList()!,
                };
                dst.SkillConfig.TransferMultiplier = src.SkillConfig.TransferMultiplier;
                dst.MasteringConfig.TransferMultiplier = src.MasteringConfig.TransferMultiplier;
                copied.Add(i);
            }

            logger.LogInformation(
                "[PrestigeTweaks4Server] Copied prestige {Source} settings to element(s) {Indices}: stash width {Cells}, filter {Included} included / {Excluded} excluded, skills x{Skills}, mastery x{Mastery}",
                SourceIndex + 1, string.Join(", ", copied), srcStash.XCellCount, srcFilters?.IncludedItems?.Count ?? 0,
                srcFilters?.ExcludedItems?.Count ?? 0, src.SkillConfig.TransferMultiplier, src.MasteringConfig.TransferMultiplier);
        }
        catch (Exception e)
        {
            logger.LogWarning("[PrestigeTweaks4Server] Failed, nothing applied: {Error}", e);
        }
        return Task.CompletedTask;
    }
}
