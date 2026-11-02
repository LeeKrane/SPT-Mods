using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Ragfair;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace FleaCategoryBlock4Server;

/// <summary>Body of /krane/fleacategoryblock/set: the handbook category ids the client has disabled.</summary>
public record SetBlockedCategoriesRequest : IRequestData
{
    [JsonPropertyName("categories")]
    public List<string>? Categories { get; set; }
}

/// <summary>The FleaCategoryBlock client posts its disabled categories here (session from the PHPSESSID cookie).</summary>
[Injectable]
public class FleaCategoryBlockRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : StaticRouter(jsonUtil, [
    new RouteAction<SetBlockedCategoriesRequest>("/krane/fleacategoryblock/set",
        (url, info, sessionId, output, ct) =>
        {
            FleaCategoryBlockMod.SetBlocked(sessionId, info.Categories);
            return new ValueTask<string>(httpResponseUtil.NullResponse());
        })
]);

[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class FleaCategoryBlockMod(ILogger<FleaCategoryBlockMod> logger, TemplateTable templateTable, ModHelper modHelper) : IOnLoad
{
    internal static FleaCategoryBlockMod? Instance;

    private readonly ILogger logger = logger;

    /// <summary>Disabled categories per session (profile id). Only sessions whose client sent a non-empty list are present.</summary>
    private static readonly ConcurrentDictionary<MongoId, HashSet<MongoId>> Blocked = new();

    /// <summary>Saved lists, so a server restart keeps every player's settings. Outside the deployed files (see deploy script).</summary>
    private static string? statePath;
    private static readonly object SaveLock = new();

    /// <summary>Item template -> its handbook category and every category above it.</summary>
    private readonly ConcurrentDictionary<MongoId, MongoId[]> chains = new();
    private readonly Lazy<(Dictionary<MongoId, MongoId?> Parents, Dictionary<MongoId, MongoId> Items)> handbook = new(() =>
    {
        var parents = new Dictionary<MongoId, MongoId?>();
        foreach (var c in templateTable.Handbook.Categories) parents[c.Id] = c.ParentId;
        var items = new Dictionary<MongoId, MongoId>();
        foreach (var i in templateTable.Handbook.Items) items[i.Id] = i.ParentId;
        return (parents, items);
    }, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Session of the flea search running on this thread (RagfairController.GetOffers is synchronous).</summary>
    [ThreadStatic] internal static MongoId? SearchSession;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var harmony = new Harmony("krane.fleacategoryblockserver");
        try
        {
            statePath = Path.Combine(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), "state", "blocked.json");
            Load();
            Instance = this;
            harmony.PatchAll(typeof(FleaCategoryBlockMod).Assembly);
            logger.LogInformation("[FleaCategoryBlock] Flea searches are filtered for clients that disable categories");
        }
        catch (Exception e)
        {
            Instance = null;
            try { harmony.UnpatchSelf(); } catch { /* nothing more to undo */ }
            logger.LogWarning("[FleaCategoryBlock] Patching failed, flea is not filtered: {Error}", e);
        }
        return Task.CompletedTask;
    }

    internal static void SetBlocked(MongoId sessionId, List<string>? categories)
    {
        Blocked.TryRemove(sessionId, out _);
        SetBlockedNoSave(sessionId, categories);
        Save();
    }

    private void Load()
    {
        if (statePath == null || !File.Exists(statePath)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(statePath));
            foreach (var (session, categories) in saved ?? [])
            {
                try { SetBlockedNoSave(new MongoId(session), categories); } catch { /* not an id, skip */ }
            }
        }
        catch (Exception e)
        {
            logger.LogWarning("[FleaCategoryBlock] Could not read {Path}: {Error}", statePath, e.Message);
        }
    }

    private static void SetBlockedNoSave(MongoId sessionId, List<string>? categories)
    {
        var set = new HashSet<MongoId>();
        foreach (var id in categories ?? [])
        {
            try { set.Add(new MongoId(id)); } catch { /* not an id, ignore */ }
        }
        if (set.Count > 0) Blocked[sessionId] = set;
    }

    private static void Save()
    {
        if (statePath == null) return;
        lock (SaveLock)
        {
            try
            {
                var data = Blocked.ToDictionary(p => p.Key.ToString(), p => p.Value.Select(id => id.ToString()).ToList());
                Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
                var temp = statePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(data));
                File.Move(temp, statePath, true);
            }
            catch (Exception e)
            {
                Instance?.logger.LogWarning("[FleaCategoryBlock] Could not save {Path}: {Error}", statePath, e.Message);
            }
        }
    }

    internal static HashSet<MongoId>? BlockedFor(MongoId sessionId) =>
        Instance != null && Blocked.TryGetValue(sessionId, out var set) ? set : null;

    /// <summary>Removes player offers (not trader, not the session's own) whose item is in or below a blocked category.</summary>
    internal void Filter(List<RagfairOffer> offers, MongoId sessionId, HashSet<MongoId> blocked)
    {
        int removed = offers.RemoveAll(o =>
            !o.IsTraderOffer()
            && o.User?.Id != sessionId
            && o.Items is { Count: > 0 } items
            && Chain(items[0].Template).Any(blocked.Contains));
        if (removed > 0) logger.LogDebug("[FleaCategoryBlock] Hid {Count} offers for {Session}", removed, sessionId);
    }

    private MongoId[] Chain(MongoId tpl) => chains.GetOrAdd(tpl, t =>
    {
        var (categoryParent, itemCategory) = handbook.Value;
        var chain = new List<MongoId>();
        if (itemCategory.TryGetValue(t, out var category))
        {
            for (MongoId? c = category; c is { } id && chain.Count < 32; c = categoryParent.GetValueOrDefault(id))
            {
                chain.Add(id);
            }
        }
        return chain.ToArray();
    });
}

/// <summary>Marks the session whose search is running, for the AddIndexValueToOffers prefix.</summary>
[HarmonyPatch(typeof(RagfairController), nameof(RagfairController.GetOffers))]
internal static class GetOffersPatch
{
    private static void Prefix(MongoId sessionID) =>
        FleaCategoryBlockMod.SearchSession = FleaCategoryBlockMod.BlockedFor(sessionID) != null ? (MongoId?)sessionID : null;

    private static void Finalizer() => FleaCategoryBlockMod.SearchSession = null;
}

/// <summary>
/// GetOffers calls AddIndexValueToOffers exactly once on its fresh offer list, after the category counts are built
/// and before indexing, sorting, the count and pagination. Removing offers here keeps indexes contiguous, counts
/// and pages exact (build searches included) and leaves the category tree untouched.
/// </summary>
[HarmonyPatch(typeof(RagfairController), "AddIndexValueToOffers")]
internal static class AddIndexValueToOffersPatch
{
    private static void Prefix(IEnumerable<RagfairOffer> offers)
    {
        var session = FleaCategoryBlockMod.SearchSession;
        FleaCategoryBlockMod.SearchSession = null;
        if (session is not { } id || offers is not List<RagfairOffer> list) return;
        var blocked = FleaCategoryBlockMod.BlockedFor(id);
        if (blocked != null) FleaCategoryBlockMod.Instance?.Filter(list, id, blocked);
    }
}
