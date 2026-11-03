using System.Reflection;
using System.Text.Json.Serialization;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils.Cloners;
using Location = SPTarkov.Server.Core.Models.Eft.Common.Location;

namespace InfectedVariants4Server;

public sealed record InfectedConfig
{
    [JsonPropertyName("InfectionPercentage")]
    public double? InfectionPercentage { get; init; }

    [JsonPropertyName("InfectedLabsOnlyInfected")]
    public bool? InfectedLabsOnlyInfected { get; init; }
}

/// <summary>Shared state for the load-time services and the static Harmony postfix.</summary>
internal static class State
{
    public const string Tag = "[InfectedVariants4Server]";
    public const string Labs = "laboratory";
    public const string ZombieGroup = "halloweenzombies";
    public const string FactoryHalloween = "halloween2026"; // FactoryClassic's declared variant id, MapVariants' wire value
    public const string Original = "original";              // MapVariants' wire value of the original (our infected Labs; also its answer for an unmanaged map)

    public static bool LabsRegistered;       // false: InstalledVariant("laboratory") says "original" for an unmanaged map too
    public static bool LabsOnlyInfected = true;

    public static readonly string[] FactoryIds = ["factory4_day", "factory4_night"];

    public static double Percent = 100;
    public static MapVariantsBridge? Bridge;
    public static SeasonalEventConfig? Seasonal;
    public static ILogger? Log;
    public static ICloner? Cloner;

    public static bool IsManaged(string id) => id == Labs || FactoryIds.Contains(id);

    /// <summary>Zombies spawn only on Factory "halloween2026" and on the infected (original) Labs.</summary>
    public static bool IsInfected(string id, string? variant) => id switch
    {
        "factory4_day" or "factory4_night" => variant == FactoryHalloween,
        Labs => LabsRegistered && variant == Original,
        _ => false,
    } && Percent > 0; // 0% means no zombies anywhere, so the zombie boss entries are not added either
}

/// <summary>
/// Reaches MapVariants' server half by reflection (same approach as FactoryClassic's bridge): no compile-time
/// reference, so a different MapVariants build cannot stop this mod from loading.
/// </summary>
internal sealed class MapVariantsBridge
{
    private const string ProvidersTypeName = "MapVariants.Server.Variants.Providers";

    private readonly object? _providers;
    private readonly MethodInfo? _register;
    private readonly MethodInfo? _installedVariant;

    public MapVariantsBridge(IServiceProvider provider, ILogger logger)
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(ProvidersTypeName, false)).FirstOrDefault(t => t != null);
        if (type is null)
        {
            logger.LogError("{Tag} {Type} is not loaded; MapVariants is a hard dependency, so this should be unreachable", State.Tag, ProvidersTypeName);
            return;
        }
        _providers = provider.GetService(type);
        _register = type.GetMethod("Register", [typeof(string), typeof(Location), typeof(Location)]);
        _installedVariant = type.GetMethod("InstalledVariant", [typeof(string)]);
        if (_providers is null || _register is null || _installedVariant is null)
        {
            logger.LogError("{Tag} MapVariants' provider surface is not the expected layout (instance={Inst}, Register={Reg}, InstalledVariant={Inst2})",
                State.Tag, _providers is not null, _register is not null, _installedVariant is not null);
            _providers = null;
        }
    }

    public bool Available => _providers is not null;

    /// <summary>False when unavailable or MapVariants refuses (a bool false return); throws what Register throws.</summary>
    public bool Register(string locationId, Location original, Location backport)
    {
        if (_providers is null) return false;
        return _register!.Invoke(_providers, [locationId, original, backport]) is not false;
    }

    /// <summary>MapVariants' wire value ("original", "backport" or a declared id), null when unavailable or it throws.</summary>
    public string? InstalledVariant(string locationId)
    {
        if (_providers is null) return null;
        try { return _installedVariant!.Invoke(_providers, [locationId]) as string; }
        catch { return null; }
    }
}

/// <summary>The one place that decides what a Location's infection and infected spawns look like.</summary>
internal static class Enforcer
{
    private static bool _warnedNoEvents;

    /// <summary>Enforces the rules on one LocationBase (a per-raid clone or a table object). Returns a short change summary, or null if nothing changed.</summary>
    public static string? Apply(LocationBase b, string rawId, bool infected)
    {
        var id = rawId.ToLowerInvariant();
        var changes = new List<string>();

        if (b.Events?.Halloween2024 is { } h)
        {
            var target = infected ? State.Percent : 0;
            if (h.InfectionPercentage != target) { h.InfectionPercentage = target; changes.Add($"infection {target:0.##}%"); }
            if (h.MinInfectionPercentage > target) { h.MinInfectionPercentage = target; changes.Add($"min infection {target:0.##}"); }
        }
        else if (infected && !_warnedNoEvents)
        {
            _warnedNoEvents = true;
            State.Log?.LogWarning("{Tag} {Id} has no Events.Halloween2024 block; infection cannot be set there", State.Tag, id);
        }

        var group = ZombieEntries(id);
        if (b.BossLocationSpawn is { } bosses)
        {
            if (!infected)
            {
                var markers = new HashSet<string>(group.Where(e => !IsInfectedName(e.BossName)).Select(Key));
                var removed = bosses.RemoveAll(s => IsInfectedName(s.BossName) || (s.Time >= 9999 && markers.Contains(Key(s))));
                if (removed > 0) changes.Add($"-{removed} zombie boss entries");
            }
            else
            {
                // The live infected Lab (SPT disableBosses) has only the infected: mirror that on the Labs copy when configured.
                if (id == State.Labs && State.LabsOnlyInfected)
                {
                    var dropped = bosses.RemoveAll(s => !IsInfectedName(s.BossName));
                    if (dropped > 0) changes.Add($"-{dropped} non-infected bosses");
                }
                // SPT's AddEventBossesToMaps rule: an entry is added only when the map has none of that name yet.
                var added = 0;
                foreach (var entry in group)
                {
                    if (bosses.Any(s => s.TriggerName != "botEvent" && s.BossName == entry.BossName)) continue;
                    bosses.Add(State.Cloner!.Clone(entry)!);
                    added++;
                }
                if (added > 0) changes.Add($"+{added} zombie boss entries");
            }
        }

        if (!infected && b.Waves is { } waves)
        {
            var removed = waves.RemoveAll(w => w.WildSpawnType is { } t && t.ToString().StartsWith("infected", StringComparison.OrdinalIgnoreCase));
            if (removed > 0) changes.Add($"-{removed} zombie waves");
        }

        return changes.Count == 0 ? null : string.Join(", ", changes);
    }

    private static List<BossLocationSpawn> ZombieEntries(string id) =>
        State.Seasonal is { } s && s.EventBossSpawns.TryGetValue(State.ZombieGroup, out var byMap) && byMap.TryGetValue(id, out var list)
            ? list
            : [];

    private static bool IsInfectedName(string? name) => name?.StartsWith("infected", StringComparison.OrdinalIgnoreCase) == true;

    private static string Key(BossLocationSpawn s) =>
        $"{s.BossName}|{s.BossChance}|{s.Time}|{s.BossZone}|{s.BossEscortType}|{s.BossEscortAmount}|{s.BossDifficulty}|{s.TriggerName}";
}

/// <summary>
/// PostLoad (before MapVariants' VariantDiscovery at PostLoad + 500000): offers Labs to MapVariants as a vanilla / infected pair
/// and installs the per-raid enforcement patch.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad)]
public sealed class InfectedRegistration(
    ILogger<InfectedRegistration> logger,
    ModHelper modHelper,
    LocationTable locationTable,
    SeasonalEventConfig seasonalConfig,
    ICloner cloner,
    IServiceProvider provider) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            State.Log = logger;
            State.Seasonal = seasonalConfig;
            State.Cloner = cloner;
            LoadConfig();
            State.Bridge = new MapVariantsBridge(provider, logger);

            new Harmony("krane.infectedvariantsserver").PatchAll(typeof(InfectedRegistration).Assembly);
            RegisterLabs();
        }
        catch (Exception e)
        {
            logger.LogError("{Tag} Setup failed; Labs stays vanilla and every raid is enforced zombie-free where possible: {Error}", State.Tag, e);
        }
        return Task.CompletedTask;
    }

    /// <summary>Sets State.Percent and State.LabsOnlyInfected from config.json; defaults (100, true) on anything invalid.</summary>
    private void LoadConfig()
    {
        const double fallback = 100;
        State.Percent = fallback;
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var cfg = modHelper.GetJsonDataFromFile<InfectedConfig>(folder, "config.json");
            State.LabsOnlyInfected = cfg?.InfectedLabsOnlyInfected ?? true;
            if (cfg?.InfectionPercentage is not { } v || !double.IsFinite(v))
            {
                logger.LogWarning("{Tag} invalid or missing InfectionPercentage in config.json (must be a number), using {Default}", State.Tag, fallback);
                return;
            }
            State.Percent = Math.Clamp(v, 0, 100);
            if (State.Percent != v) logger.LogWarning("{Tag} InfectionPercentage {Value} out of range, clamped to {Clamped}", State.Tag, v, State.Percent);
        }
        catch (Exception e)
        {
            logger.LogWarning("{Tag} Could not read config.json ({Error}), using {Default}", State.Tag, e.Message, fallback);
        }
    }

    // Backport (what MapVariants rests on) = the live Labs Location, infection 0. Original = Infected: a copy that shares the
    // other six fields by reference (so loot mods that edit them in place hit both) and owns a deep-copied Base.
    private void RegisterLabs()
    {
        var bridge = State.Bridge!;
        if (!bridge.Available) { logger.LogError("{Tag} MapVariants not usable; Labs stays vanilla (no infection)", State.Tag); return; }

        var live = locationTable.GetLocation(State.Labs);
        if (live?.Base is null) { logger.LogError("{Tag} No '{Id}' location in the table; Labs stays vanilla", State.Tag, State.Labs); return; }

        // Copy first, so it keeps whatever the live Base holds (infected bosses included); then the live Base becomes vanilla.
        var infectedBase = cloner.Clone(live.Base)!;
        if (infectedBase.Events?.Halloween2024 is { } h) h.InfectionPercentage = State.Percent;
        var infected = live with { Base = infectedBase };

        Enforcer.Apply(live.Base, State.Labs, infected: false);
        if (live.Base.BossLocationSpawn is not { Count: > 0 })
            logger.LogWarning("{Tag} Vanilla Labs has no boss spawns; SPT's zombie disableBosses wiped them. Set PPES halloween.labInfectedOnly to false", State.Tag);

        try
        {
            // MapVariants rests on the backport, so the VANILLA Labs is the backport and the infected copy the original:
            // an unchosen raid, a transit and the raid-end reset all land on Vanilla.
            if (!bridge.Register(State.Labs, infected, live))
            {
                logger.LogError("{Tag} MapVariants refused the Labs pair; Labs stays vanilla (no infection)", State.Tag);
                return;
            }
            State.LabsRegistered = true;
            logger.LogInformation("{Tag} Labs handed to MapVariants: Vanilla (default, no infection) / Infected ({Percent:0.##}%)", State.Tag, State.Percent);
        }
        catch (Exception e)
        {
            logger.LogError("{Tag} Handing Labs to MapVariants threw; it keeps whatever the table holds: {Error}", State.Tag, e);
        }
    }
}

/// <summary>
/// Runs on the per-raid clone SPT builds, after the other postfixes on it (PPES re-adds zombie bosses here for every map),
/// so the result is final. MapVariants installs the variant in its raid-start route, which runs before SPT's own routers
/// generate the location, so InstalledVariant is already the raid's variant.
/// </summary>
[HarmonyPatch(typeof(LocationLifecycleService), nameof(LocationLifecycleService.GenerateLocationAndLoot))]
internal static class GenerateLocationPatch
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(LocationBase __result, string name)
    {
        try
        {
            if (__result is null || string.IsNullOrEmpty(name)) return;
            var id = name.ToLowerInvariant();
            var variant = State.Bridge?.InstalledVariant(id);
            var infected = State.IsInfected(id, variant);
            var changes = Enforcer.Apply(__result, id, infected);
            if (infected || State.IsManaged(id) || changes is not null)
                State.Log?.LogInformation("{Tag} raid on {Id} ({Variant}): {State}{Changes}", State.Tag, id, variant ?? "unknown",
                    infected ? $"INFECTED {State.Percent:0.##}%" : "no infection", changes is null ? "" : $" [{changes}]");
        }
        catch (Exception e)
        {
            State.Log?.LogError("{Tag} Enforcement failed for {Name}, zombies may leak into this raid: {Error}", State.Tag, name, e);
        }
    }
}

/// <summary>
/// After PPES (PostLoad + 5000000): clears infection and infected spawns from every table Base except the Factory pair, which
/// shares one Base between classic and halloween2026 and is therefore enforced per raid only. Then sets the map-screen badges.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 6000000)]
public sealed class InfectedSweep(ILogger<InfectedSweep> logger, LocationTable locationTable, GlobalTable globalTable) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var cleaned = 0;
            foreach (var location in locationTable.GetDictionary().Values)
            {
                // Dictionary keys are property names ("SandboxHigh"); Base.Id is the location id ("Sandbox_high") the
                // seasonal config, MapVariants and the per-raid patch key on.
                var id = location?.Base?.Id?.ToLowerInvariant();
                if (id is null || State.FactoryIds.Contains(id)) continue;
                var variant = State.Bridge?.InstalledVariant(id);
                if (Enforcer.Apply(location!.Base!, id, State.IsInfected(id, variant)) is not null) cleaned++;
            }

            var badges = SetBadges();
            logger.LogInformation("{Tag} Sweep: {Count} location base(s) adjusted, badges {Badges}; infection {Percent:0.##}% on Factory halloween2026 and infected Labs only",
                State.Tag, cleaned, badges, State.Percent);
        }
        catch (Exception e)
        {
            logger.LogWarning("{Tag} Sweep failed: {Error}", State.Tag, e);
        }
        return Task.CompletedTask;
    }

    // Globals LocationInfection is only the map-screen badge; SeasonActivity InfectionHalloween stays as PPES / SPT set it.
    private string SetBadges()
    {
        var map = globalTable.LocationInfection;
        if (map is null) return "n/a";
        foreach (var key in map.Keys.ToList())
        {
            var k = key.ToLowerInvariant();
            map[key] = k is "factory4" or "factory4_day" or "factory4_night" or State.Labs ? (int)Math.Round(State.Percent) : 0;
        }
        return string.Join(",", map.Where(p => p.Value > 0).Select(p => p.Key));
    }
}
