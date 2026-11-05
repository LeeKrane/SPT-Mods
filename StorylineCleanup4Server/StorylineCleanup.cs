using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils.Json;

namespace StorylineCleanup4Server;

public sealed record CleanupConfig
{
    [JsonPropertyName("RemoveQuestIds")]
    public List<string>? RemoveQuestIds { get; init; }

    [JsonPropertyName("RemapQuestIds")]
    public Dictionary<string, string>? RemapQuestIds { get; init; }

    [JsonPropertyName("DisableStoryMapLock")]
    public bool DisableStoryMapLock { get; init; }
}

/// <summary>
/// Removes configured quests from the quest database and strips every "Quest" condition that targets them.
/// VisitAPI registers pack quests in its QuestLoader.OnLoad (TypePriority PostLoad); its other IOnLoads run at most at
/// PostLoad + 21, so this runs after all of them. VisitAPI keeps no separate quest registry: its autoStart / autoFinish /
/// noteLinks flags are read on request from the quest's extension data in the database, so removing the quest is enough.
/// The only static copy is QuestLoader.Loaded (set of pack quest ids), pruned via reflection.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public sealed class StorylineCleanup(ILogger<StorylineCleanup> logger, ModHelper modHelper, TemplateTable templateTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            Run();
        }
        catch (Exception e)
        {
            logger.LogWarning("[StorylineCleanup4Server] Cleanup failed: {Error}", e);
        }
        return Task.CompletedTask;
    }

    private void Run()
    {
        List<string> ids;
        Dictionary<string, string> remapCfg;
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var cfg = modHelper.GetJsonDataFromFile<CleanupConfig>(folder, "config.json");
            ids = cfg?.RemoveQuestIds ?? [];
            remapCfg = cfg?.RemapQuestIds ?? [];
        }
        catch (Exception e)
        {
            logger.LogWarning("[StorylineCleanup4Server] Could not read config.json ({Error}), nothing removed", e.Message);
            return;
        }

        var wanted = ids.Select(i => i.Trim()).Where(i => i.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var quests = templateTable.Quests;
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in quests.Keys.ToList())
        {
            var id = key.ToString();
            if (wanted.Contains(id) && quests.Remove(key)) removed.Add(id);
        }

        var missing = wanted.Count - removed.Count;
        if (removed.Count == 0)
        {
            logger.LogWarning("[StorylineCleanup4Server] None of the {Count} configured quests exist in the database, nothing removed", wanted.Count);
            return;
        }

        var remap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (from, to) in remapCfg)
        {
            var f = from.Trim();
            var t = to.Trim();
            if (!removed.Contains(f)) continue;
            if (quests.Keys.Any(k => string.Equals(k.ToString(), t, StringComparison.OrdinalIgnoreCase)))
                remap[f] = quests.Keys.First(k => string.Equals(k.ToString(), t, StringComparison.OrdinalIgnoreCase)).ToString();
            else
                logger.LogWarning("[StorylineCleanup4Server] Remap target {To} for {From} not in database, conditions will be removed", t, f);
        }

        var stats = new Stats();
        var condQuests = new List<string>();
        var emptied = new List<string>();
        foreach (var quest in quests.Values)
        {
            var c = quest.Conditions;
            if (c == null) continue;
            var before = stats.Removed + stats.Remapped;
            var hadFinish = c.AvailableForFinish is { Count: > 0 };
            foreach (var list in new[] { c.Started, c.AvailableForStart, c.AvailableForFinish, c.Success, c.Fail })
                Strip(list, removed, remap, stats);
            if (stats.Removed + stats.Remapped > before) condQuests.Add($"{quest.QuestName} ({quest.Id})");
            if (hadFinish && c.AvailableForFinish!.Count == 0) emptied.Add($"{quest.QuestName} ({quest.Id})");
        }

        var loadedPruned = PruneVisitApiLoaded(removed);
        if (emptied.Count > 0)
            logger.LogWarning("[StorylineCleanup4Server] AvailableForFinish became empty (quest completes without objectives) for: {Names}", string.Join(", ", emptied));

        logger.LogInformation(
            "[StorylineCleanup4Server] Removed {Removed} quest(s) ({Missing} configured id(s) not found), {Conds} dangling quest condition(s) removed, {Remapped} remapped, in {Count} quest(s){Names}; VisitAPI pack set pruned: {Pruned}",
            removed.Count, missing, stats.Removed, stats.Remapped, condQuests.Count, condQuests.Count > 0 ? ": " + string.Join(", ", condQuests) : "", loadedPruned);
    }

    private sealed class Stats
    {
        public int Removed;
        public int Remapped;
    }

    /// <summary>Retargets "Quest" conditions on a removed id to its remap replacement when one exists, else drops them (for list targets the id is removed; the condition goes when the list empties).</summary>
    private static void Strip(List<QuestCondition>? list, HashSet<string> removed, Dictionary<string, string> remap, Stats stats)
    {
        if (list == null) return;
        for (var i = list.Count - 1; i >= 0; i--)
        {
            var cond = list[i];
            if (cond.ConditionType != "Quest" || cond.Target == null) continue;
            if (cond.Target.IsItem)
            {
                var id = cond.Target.Item;
                if (id == null || !removed.Contains(id)) continue;
                if (remap.TryGetValue(id, out var to))
                {
                    cond.Target = new ListOrT<string>(null, to);
                    stats.Remapped++;
                }
                else
                {
                    list.RemoveAt(i);
                    stats.Removed++;
                }
            }
            else if (cond.Target.List is { Count: > 0 } targets)
            {
                var result = new List<string>();
                var changed = false;
                var remapped = false;
                foreach (var t in targets)
                {
                    if (!removed.Contains(t)) { result.Add(t); continue; }
                    changed = true;
                    if (remap.TryGetValue(t, out var to))
                    {
                        remapped = true;
                        if (!result.Contains(to, StringComparer.OrdinalIgnoreCase)) result.Add(to);
                    }
                }
                if (!changed) continue;
                if (result.Count == 0)
                {
                    list.RemoveAt(i);
                    stats.Removed++;
                    continue;
                }
                cond.Target = new ListOrT<string>(result, null);
                if (remapped) stats.Remapped++;
                else stats.Removed++;
            }
        }
    }

    private bool PruneVisitApiLoaded(HashSet<string> removed)
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VisitAPI.Server.QuestLoader", false)).FirstOrDefault(t => t != null);
            if (type == null)
            {
                logger.LogWarning("[StorylineCleanup4Server] VisitAPI QuestLoader not found, pack set not pruned");
                return false;
            }
            var value = type.GetField("Loaded", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (value is not HashSet<string> loaded)
            {
                logger.LogWarning("[StorylineCleanup4Server] VisitAPI QuestLoader.Loaded missing or not HashSet<string> ({Type}), pack set not pruned", value?.GetType().Name ?? "null");
                return false;
            }
            lock (loaded) loaded.RemoveWhere(removed.Contains);
            return true;
        }
        catch (Exception e)
        {
            logger.LogWarning("[StorylineCleanup4Server] Could not prune VisitAPI pack set: {Error}", e.Message);
            return false;
        }
    }
}
