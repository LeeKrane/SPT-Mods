using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace ItemTweaks4Server;

/// <summary>
/// Adds one Secure container Omega item reward to prestige level <see cref="Level"/> (element index = level - 1 of the prestige template
/// list). PrestigeHelper.ProcessPendingPrestige grants the rewards of every level up to the reached one, and mails their items to the new
/// profile, so every prestige from 4 on gets exactly one Omega. Runs late (PostLoad) so prestige levels replaced by other mods
/// (Content Backport - Prestiges) are covered. Skipped when the Omega item was not created.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class PrestigeRewards(ILogger<PrestigeRewards> logger, TemplateTable templateTable) : IOnLoad
{
    private const int Level = 4;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        if (!templateTable.Items.ContainsKey(new MongoId(Ids.SecureContainerOmega)))
        {
            logger.LogInformation("[ItemTweaks4Server] Secure container Omega not created, prestige rewards skipped");
            return Task.CompletedTask;
        }

        var elements = templateTable.Prestige?.Elements;
        if (elements == null)
        {
            logger.LogWarning("[ItemTweaks4Server] No prestige data, Secure container Omega not added to any prestige level");
            return Task.CompletedTask;
        }

        if (elements.Count < Level)
        {
            logger.LogWarning("[ItemTweaks4Server] Only {Total} prestige levels, Secure container Omega not added", elements.Count);
            return Task.CompletedTask;
        }

        var element = elements[Level - 1];
        var rewards = element.Rewards ?? [];
        if (!rewards.Any(r => r.Items?.Any(i => i.Template == Ids.SecureContainerOmega) == true))
        {

            // Reuse the picture of the level's first item reward; the reward is only a start item, it has no art of its own.
            var template = rewards.FirstOrDefault(r => r.Type == RewardType.Item);
            var itemId = DeterministicId($"omega-item-{element.Id}");
            rewards.Add(new Reward
            {
                Id = new MongoId(DeterministicId($"omega-reward-{element.Id}")),
                Unknown = false,
                GameMode = ["regular", "pve"],
                AvailableInGameEditions = [],
                IllustrationConfig = template?.IllustrationConfig,
                IsHidden = false,
                Target = itemId,
                Value = 1,
                IsEncoded = false,
                FindInRaid = true,
                Items = [new Item { Id = new MongoId(itemId), Template = Ids.SecureContainerOmega, Upd = new Upd { SpawnedInSession = true, StackObjectsCount = 1 } }],
                Type = RewardType.Item,
            });
            element.Rewards = rewards;
        }
        logger.LogInformation("[ItemTweaks4Server] Secure container Omega added as a reward of prestige {Level} (granted at every prestige from it on)", Level);
        return Task.CompletedTask;
    }

    /// <summary>24 hex characters derived from the text, stable across restarts.</summary>
    private static string DeterministicId(string text) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(text)))[..24].ToLowerInvariant();
}
