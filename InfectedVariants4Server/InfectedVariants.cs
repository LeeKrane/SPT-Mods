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
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services.InRaid;
using SPTarkov.Server.Core.Utils;
using SPTarkov.Server.Core.Utils.Cloners;
using Location = SPTarkov.Server.Core.Models.Eft.Common.Location;

namespace InfectedVariants4Server;

public sealed record InfectedConfig
{
    [JsonPropertyName("InfectionPercentage")]
    public double? InfectionPercentage { get; init; }

    [JsonPropertyName("InfectedLabsOnlyInfected")]
    public bool? InfectedLabsOnlyInfected { get; init; }

    [JsonPropertyName("EnabledMaps")]
    public string[]? EnabledMaps { get; init; }
}

/// <summary>One player-facing map: its key (what EnabledMaps lists), name and location ids (Ground Zero has two).</summary>
internal sealed record MapSpec(string Key, string Name, string[] Ids);

/// <summary>How an enabled location got its Infected variant.</summary>
internal enum Mode
{
    /// <summary>Unmodded map: this mod registered a shared-data pair and declared "infected" on it.</summary>
    Pair,
    /// <summary>A mod replaced the location data, so MapVariants manages it already: "infected" is declared on top of that backport.</summary>
    Declared,
}

/// <summary>Shared state for the load-time services and the static Harmony postfix.</summary>
internal static class State
{
    public const string Tag = "[InfectedVariants4Server]";
    public const string Labs = "laboratory";
    public const string ZombieGroup = "halloweenzombies";
    public const string FactoryHalloween = "halloween2026"; // FactoryClassic's declared variant id, MapVariants' wire value
    public const string Original = "original";              // MapVariants' wire value of the original (the infected copy of a Pair map; also its answer for an unmanaged map)
    public const string Infected = "infected";              // declared variant id on a Declared map, MapVariants' wire value

    // Every stock raidable map except Factory (FactoryClassic's). Terminal (ManimalTerminal), Icebreaker (ManimalIcebreaker, own id),
    // Labyrinth (disabled in SPT 4.1.6), Arena, hideout, town, develop, privatearea, suburbs are deliberately absent.
    public static readonly MapSpec[] Specs =
    [
        new("bigmap", "Customs", ["bigmap"]),
        new("woods", "Woods", ["woods"]),
        new("shoreline", "Shoreline", ["shoreline"]),
        new("interchange", "Interchange", ["interchange"]),
        new("lighthouse", "Lighthouse", ["lighthouse"]),
        new("rezervbase", "Reserve", ["rezervbase"]),
        new("tarkovstreets", "Streets of Tarkov", ["tarkovstreets"]),
        new("sandbox", "Ground Zero", ["sandbox", "sandbox_high"]),
        new("laboratory", "Labs", ["laboratory"]),
    ];

    public static readonly string[] FactoryIds = ["factory4_day", "factory4_night"];

    /// <summary>Enabled and successfully set up: location id (lower case) to how it is infected. Filled at PostLoad.</summary>
    public static readonly Dictionary<string, Mode> Modes = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The name of the non-infected variant per registered map key ("Vanilla", or "Manimal"/"Modded" for a Declared map).</summary>
    public static readonly Dictionary<string, string> BackportNames = new(StringComparer.OrdinalIgnoreCase);

    public static bool LabsOnlyInfected = true;

    public static double Percent = 100;
    public static MapVariantsBridge? Bridge;
    public static SeasonalEventConfig? Seasonal;
    public static ILogger? Log;
    public static ICloner? Cloner;

    public static bool IsManaged(string id) => FactoryIds.Contains(id) || Modes.ContainsKey(id);

    /// <summary>Zombies spawn only on Factory "halloween2026" and on the Infected variant of an enabled map.</summary>
    public static bool IsInfected(string id, string? variant) => (FactoryIds.Contains(id)
        ? variant == FactoryHalloween
        : Modes.ContainsKey(id) && variant == Infected) && Percent > 0; // 0% means no zombies anywhere, so no zombie boss entries either
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
    private readonly MethodInfo? _declareVariant;

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
        _declareVariant = type.GetMethod("DeclareVariant", [typeof(string)]); // MapVariants 1.1.0+ (API 3.1)
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

    /// <summary>False when unavailable, when MapVariants predates declared variants, or when it refuses the id.</summary>
    public bool DeclareVariant(string variant)
    {
        if (_providers is null || _declareVariant is null) return false;
        try { return _declareVariant.Invoke(_providers, [variant]) is true; }
        catch { return false; }
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

    // Maps SPT has no halloweenzombies entry for borrow the first of these (open-zone Infected/PMC/Assault packs + Tagilla), zone cleared.
    private static readonly string[] FallbackTemplates = ["shoreline", "woods", "bigmap"];
    private static readonly Dictionary<string, List<BossLocationSpawn>> Fallbacks = new(StringComparer.OrdinalIgnoreCase);

    private static List<BossLocationSpawn> ZombieEntries(string id)
    {
        if (State.Seasonal is not { } s || !s.EventBossSpawns.TryGetValue(State.ZombieGroup, out var byMap)) return [];
        if (byMap.TryGetValue(id, out var list) && list.Count > 0) return list;
        if (!State.Modes.ContainsKey(id)) return []; // only an enabled infected map needs a fallback
        lock (Fallbacks)
        {
            if (Fallbacks.TryGetValue(id, out var cached)) return cached;
            var template = FallbackTemplates.Select(t => byMap.TryGetValue(t, out var l) ? (Name: t, List: l) : default).FirstOrDefault(t => t.List is { Count: > 0 });
            var built = template.List is null ? [] : template.List.Select(e => { var c = State.Cloner!.Clone(e)!; c.BossZone = ""; return c; }).ToList();
            State.Log?.LogWarning("{Tag} SPT has no '{Group}' boss entries for {Id}; {Count} borrowed from {Template} (open zones)", State.Tag, State.ZombieGroup, id, built.Count, template.Name ?? "nowhere");
            return Fallbacks[id] = built;
        }
    }

    private static bool IsInfectedName(string? name) => name?.StartsWith("infected", StringComparison.OrdinalIgnoreCase) == true;

    private static string Key(BossLocationSpawn s) =>
        $"{s.BossName}|{s.BossChance}|{s.Time}|{s.BossZone}|{s.BossEscortType}|{s.BossEscortAmount}|{s.BossDifficulty}|{s.TriggerName}";
}

/// <summary>
/// The client plugin hides the "original" tile on every map this mod registers, but nothing on the server may rely on that: a stale
/// or other client could still post "original". On a shared-data pair installing "original" assigns the same objects and leaves
/// MapVariants' _installedId (VariantRegistry.InstallKeyed, backport branch only) at "infected", so the next raid would be
/// zombies on a vanilla pick. So for a map this mod manages, any answer that is not "infected" becomes "backport" before MapVariants
/// sees it: in the two routes that take a choice (VariantRoutes.Record, ClaimTransit: the mutated request is what is stored, installed
/// and answered to the client) and, as the catch-all every install passes through, VariantRegistry.Install. Patched by reflection
/// so a different MapVariants layout only costs this guard (logged), never the mod.
/// </summary>
internal static class OriginalGuard
{
    private const string RoutesType = "MapVariants.Server.Variants.VariantRoutes";
    private const string RegistryType = "MapVariants.Server.Variants.VariantRegistry";
    private const string Backport = "backport"; // MapVariants' wire value

    public static void Install(Harmony harmony, ILogger logger)
    {
        var patched = new List<string>();
        try
        {
            var routes = Find(RoutesType);
            foreach (var name in new[] { "Record", "ClaimTransit" })
            {
                var method = routes?.GetMethod(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (method is null || method.GetParameters().Length == 0) continue;
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(OriginalGuard), nameof(RequestPrefix)));
                patched.Add(name);
            }
            var install = Find(RegistryType)?.GetMethod("Install", [typeof(string), typeof(string)]);
            if (install is not null)
            {
                harmony.Patch(install, prefix: new HarmonyMethod(typeof(OriginalGuard), nameof(InstallPrefix)));
                patched.Add("Install");
            }
        }
        catch (Exception e)
        {
            logger.LogError("{Tag} Could not guard against an 'original' pick: {Error}", State.Tag, e);
        }
        if (patched.Count == 3) logger.LogInformation("{Tag} 'original' picks on managed maps are served as the backport", State.Tag);
        else logger.LogWarning("{Tag} 'original' guard incomplete (hooked: {Hooked}); MapVariants' layout is not the expected one", State.Tag, string.Join(",", patched));
    }

    private static Type? Find(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name, false)).FirstOrDefault(t => t != null);

    private static bool MustBeBackport(string? locationId, string? variant)
    {
        var id = locationId?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(id) || !State.Modes.ContainsKey(id)) return false;
        var v = variant?.Trim().ToLowerInvariant();
        return v != State.Infected && v != Backport; // MapVariant.Normalise folds every other word to "original"
    }

    // VariantChoiceRequest { LocationId, Variant }: first parameter of both routes.
    private static void RequestPrefix(object __0)
    {
        try
        {
            var type = __0?.GetType();
            var location = type?.GetProperty("LocationId");
            var variant = type?.GetProperty("Variant");
            if (location is null || variant is null || !variant.CanWrite) return;
            var id = location.GetValue(__0) as string;
            var was = variant.GetValue(__0) as string;
            if (!MustBeBackport(id, was)) return;
            variant.SetValue(__0, Backport);
            State.Log?.LogInformation("{Tag} {Id}: '{Was}' requested, served as '{Backport}' (the original tile is never offered)", State.Tag, id, was, Backport);
        }
        catch (Exception e)
        {
            State.Log?.LogError("{Tag} Guarding a variant request failed: {Error}", State.Tag, e);
        }
    }

    private static void InstallPrefix(string locationId, ref string variant)
    {
        if (MustBeBackport(locationId, variant)) variant = Backport;
    }
}

/// <summary>
/// Asks MapVariants' registry (by reflection, private layout) what happened to a location before this mod ran: whether a mod already
/// replaced its data (MapVariants will discover it as a backport), or another provider already registered it. Returns Unknown
/// when the layout is not the expected one, and the caller falls back to the known backport assemblies.
/// </summary>
internal sealed class RegistryProbe
{
    internal enum Status { Unknown, Unmodified, Replaced, ManagedElsewhere }

    private readonly System.Collections.IDictionary? _entries;
    private readonly MethodInfo? _capture;

    public RegistryProbe(IServiceProvider provider, ILogger logger)
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MapVariants.Server.Variants.VariantRegistry", false)).FirstOrDefault(t => t != null);
            var registry = type is null ? null : provider.GetService(type);
            _entries = registry is null ? null : type!.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(registry) as System.Collections.IDictionary;
            var fieldSet = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("MapVariants.Server.Variants.LocationFieldSet", false)).FirstOrDefault(t => t != null);
            _capture = fieldSet?.GetMethod("Capture", BindingFlags.Static | BindingFlags.Public);
        }
        catch (Exception e)
        {
            logger.LogWarning("{Tag} Could not inspect MapVariants' registry ({Error}); falling back to the known backport mods", State.Tag, e.Message);
        }
        if (_entries is null || _capture is null)
            logger.LogWarning("{Tag} MapVariants' registry layout is not the expected one; falling back to the known backport mods", State.Tag);
    }

    public Status Check(LocationTable table, string id)
    {
        try
        {
            if (_entries is null || _capture is null) return Status.Unknown;
            var key = table.GetMappedKey(id).ToLowerInvariant();
            if (!_entries.Contains(key)) return Status.Unknown;
            var entry = _entries[key]!;
            var entryType = entry.GetType();
            if (entryType.GetField("Backport")?.GetValue(entry) is not null) return Status.ManagedElsewhere;
            var original = entryType.GetField("Original")?.GetValue(entry);
            var live = table.GetLocation(id);
            if (original is null || live is null) return Status.Unknown;
            var before = (object?[])original.GetType().GetProperty("References")!.GetValue(original)!;
            var captured = _capture.Invoke(null, [live])!;
            var now = (object?[])captured.GetType().GetProperty("References")!.GetValue(captured)!;
            for (var i = 0; i < Math.Max(before.Length, now.Length); i++)
                if (!ReferenceEquals(i < before.Length ? before[i] : null, i < now.Length ? now[i] : null)) return Status.Replaced;
            return Status.Unmodified;
        }
        catch
        {
            return Status.Unknown;
        }
    }

    /// <summary>The backport mods known to replace one location: assembly prefix, and the name their variant gets on the prompt.</summary>
    private static readonly Dictionary<string, (string Assembly, string Name)> Known = new()
    {
        ["interchange"] = ("ManimalInterchange", "Manimal"),
        ["lighthouse"] = ("ManimalLighthouse", "Manimal"),
    };

    public static string BackportName(string id) => Known.TryGetValue(id, out var k) && Loaded(k.Assembly) ? k.Name : "Modded";

    public static bool KnownReplaced(string id) => Known.TryGetValue(id, out var k) && Loaded(k.Assembly);

    private static bool Loaded(string prefix) =>
        AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true);
}

/// <summary>
/// PostLoad (before MapVariants' VariantDiscovery at PostLoad + 500000): for every enabled map either hands MapVariants a vanilla / infected
/// pair (unmodded map) or declares the "infected" variant on the backport a mod installed (modded map), and installs the per-raid
/// enforcement patch.
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
    private bool _declared;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            State.Log = logger;
            State.Seasonal = seasonalConfig;
            State.Cloner = cloner;
            var enabled = LoadConfig();
            State.Bridge = new MapVariantsBridge(provider, logger);

            var harmony = new Harmony("krane.infectedvariantsserver");
            harmony.PatchAll(typeof(InfectedRegistration).Assembly);
            OriginalGuard.Install(harmony, logger);

            var probe = new RegistryProbe(provider, logger);
            foreach (var spec in State.Specs)
            {
                if (enabled is not null && !enabled.Contains(spec.Key) && !spec.Ids.Any(enabled.Contains))
                {
                    logger.LogInformation("{Tag} {Map}: not in EnabledMaps; no Infected variant", State.Tag, spec.Name);
                    continue;
                }
                try { RegisterMap(spec, probe); }
                catch (Exception e) { logger.LogError("{Tag} {Map}: setup threw; no Infected variant there: {Error}", State.Tag, spec.Name, e); }
            }
        }
        catch (Exception e)
        {
            logger.LogError("{Tag} Setup failed; every raid is enforced zombie-free where possible: {Error}", State.Tag, e);
        }
        return Task.CompletedTask;
    }

    /// <summary>Sets State.Percent and State.LabsOnlyInfected from config.json (defaults 100, true); returns the EnabledMaps set, null meaning every map.</summary>
    private HashSet<string>? LoadConfig()
    {
        const double fallback = 100;
        State.Percent = fallback;
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var cfg = modHelper.GetJsonDataFromFile<InfectedConfig>(folder, "config.json");
            State.LabsOnlyInfected = cfg?.InfectedLabsOnlyInfected ?? true;
            if (cfg?.InfectionPercentage is not { } v || !double.IsFinite(v))
                logger.LogWarning("{Tag} invalid or missing InfectionPercentage in config.json (must be a number), using {Default}", State.Tag, fallback);
            else
            {
                State.Percent = Math.Clamp(v, 0, 100);
                if (State.Percent != v) logger.LogWarning("{Tag} InfectionPercentage {Value} out of range, clamped to {Clamped}", State.Tag, v, State.Percent);
            }
            if (cfg?.EnabledMaps is null) return null;
            var set = new HashSet<string>(cfg.EnabledMaps.Where(m => !string.IsNullOrWhiteSpace(m)).Select(m => m.Trim().ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);
            var unknown = set.Where(m => !State.Specs.Any(s => s.Key == m || s.Ids.Contains(m))).ToList();
            if (unknown.Count > 0) logger.LogWarning("{Tag} EnabledMaps names no known map: {Unknown}", State.Tag, string.Join(", ", unknown));
            return set;
        }
        catch (Exception e)
        {
            logger.LogWarning("{Tag} Could not read config.json ({Error}), using defaults", State.Tag, e.Message);
            return null;
        }
    }

    private void RegisterMap(MapSpec spec, RegistryProbe probe)
    {
        var bridge = State.Bridge!;
        if (!bridge.Available) { logger.LogError("{Tag} MapVariants not usable; no Infected {Map}", State.Tag, spec.Name); return; }

        var done = new List<string>();
        foreach (var id in spec.Ids)
        {
            if (locationTable.GetLocation(id)?.Base is null) { logger.LogError("{Tag} No '{Id}' location in the table; it stays vanilla", State.Tag, id); continue; }

            var status = probe.Check(locationTable, id);
            if (status == RegistryProbe.Status.Unknown)
                status = RegistryProbe.KnownReplaced(id) ? RegistryProbe.Status.Replaced : RegistryProbe.Status.Unmodified;

            switch (status)
            {
                case RegistryProbe.Status.ManagedElsewhere:
                    logger.LogWarning("{Tag} {Id} is already managed by another MapVariants provider; no Infected variant there", State.Tag, id);
                    break;
                case RegistryProbe.Status.Replaced:
                    if (DeclareOn(id, spec)) done.Add(id);
                    break;
                default:
                    if (RegisterPair(id, spec)) done.Add(id);
                    break;
            }
        }
        // A client registers the map's ids as one choice, so a Ground Zero with only one id set up (or the two in different modes) would half-work: all or nothing.
        if (spec.Ids.Length > 1 && done.Count > 0 && (done.Count != spec.Ids.Length || done.Select(i => State.Modes[i]).Distinct().Count() > 1))
        {
            foreach (var id in done) State.Modes.Remove(id);
            logger.LogError("{Tag} {Map}: only {Done} of {Total} location ids could be set up; no Infected variant there", State.Tag, spec.Name, done.Count, spec.Ids.Length);
        }
    }

    // A mod replaced this location's data (ManimalInterchange, ManimalLighthouse, ...): MapVariants discovers it as the backport at
    // PostLoad + 500000 (original = the shipped data). "infected" is declared on top of that backport: the server serves the same
    // modded data and this mod adds the zombies per raid. The client registers the prompt (backport name / Infected).
    private bool DeclareOn(string id, MapSpec spec)
    {
        var bridge = State.Bridge!;
        if (!_declared)
        {
            if (!bridge.DeclareVariant(State.Infected)) { logger.LogError("{Tag} MapVariants refused or cannot declare '{Id}'; no Infected {Map}", State.Tag, State.Infected, spec.Name); return false; }
            _declared = true;
        }
        State.Modes[id] = Mode.Declared;
        State.BackportNames[spec.Key] = RegistryProbe.BackportName(id);
        logger.LogInformation("{Tag} {Id}: declared '{Variant}' on the replaced (modded) data, prompt {Backport} / Infected ({Percent:0.##}%)", State.Tag, id, State.Infected, State.BackportNames[spec.Key], State.Percent);
        return true;
    }

    // Unmodded map. MapVariants only manages a map once a pair is registered, so register a pair whose two Locations are distinct objects
    // sharing ALL seven fields by reference (the live Base included): both sides are the live data, so nothing any mod does to it can diverge.
    // MapVariants cannot tell them apart (InstalledVariant would answer the backport for both), so "infected" is declared on top, exactly as
    // for a replaced map, and the client hides the never-offered "original" tile. Infection and every boss/wave change happen per raid
    // on SPT's deep-copied raid Base (the PostLoad sweep keeps the shared table Base clean).
    private bool RegisterPair(string id, MapSpec spec)
    {
        var bridge = State.Bridge!;
        var live = locationTable.GetLocation(id)!;
        if (id == State.Labs && live.Base.BossLocationSpawn is not { Count: > 0 })
            logger.LogWarning("{Tag} Vanilla Labs has no boss spawns; SPT's zombie disableBosses wiped them. Set PPES halloween.labInfectedOnly to false", State.Tag);
        try
        {
            if (!_declared)
            {
                if (!bridge.DeclareVariant(State.Infected)) { logger.LogError("{Tag} MapVariants refused or cannot declare '{Id}'; no Infected {Map}", State.Tag, State.Infected, spec.Name); return false; }
                _declared = true;
            }
            if (!bridge.Register(id, live with { }, live))
            {
                logger.LogError("{Tag} MapVariants refused the {Id} registration; it stays vanilla (no infection)", State.Tag, id);
                return false;
            }
            State.Modes[id] = Mode.Pair;
            State.BackportNames[spec.Key] = "Vanilla";
            logger.LogInformation("{Tag} {Id} handed to MapVariants (shared data): Vanilla (default, no infection) / Infected ({Percent:0.##}%, per raid)", State.Tag, id, State.Percent);
            return true;
        }
        catch (Exception e)
        {
            logger.LogError("{Tag} Handing {Id} to MapVariants threw; it keeps whatever the table holds: {Error}", State.Tag, id, e);
            return false;
        }
    }
}

/// <summary>
/// The client plugin reads this at the map screen to learn which maps to offer as Infected and how (one entry per player-facing map),
/// so the server config is the only place a map is switched on or off.
/// </summary>
public sealed class MapsResponse
{
    [JsonPropertyName("maps")] public List<MapEntry> Maps { get; set; } = [];
}

public sealed class MapEntry
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("ids")] public List<string> Ids { get; set; } = [];
    [JsonPropertyName("backport")] public string Backport { get; set; } = "Vanilla";
}

[Injectable]
public sealed class InfectedRoutes(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : StaticRouter(jsonUtil,
[
    new RouteAction<EmptyRequestData>("/infectedvariants/maps",
        (url, info, sessionId, output, cancellationToken) => ValueTask.FromResult(httpResponseUtil.NoBody(Describe()))),
])
{
    private static MapsResponse Describe()
    {
        var response = new MapsResponse();
        foreach (var spec in State.Specs)
        {
            var ids = spec.Ids.Where(State.Modes.ContainsKey).ToList();
            if (ids.Count == 0) continue;
            response.Maps.Add(new MapEntry
            {
                Name = spec.Name,
                Ids = ids,
                Backport = State.BackportNames.TryGetValue(spec.Key, out var n) ? n : "Vanilla",
            });
        }
        return response;
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
/// shares one Base between classic and halloween2026 and is therefore enforced per raid only. Then zeroes the map-screen badges.
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

            ClearBadges(globalTable);
            logger.LogInformation("{Tag} Sweep: {Count} location base(s) adjusted, map-screen badges cleared; infection {Percent:0.##}% on Factory halloween2026 and the infected variant of: {Maps}",
                State.Tag, cleaned, State.Percent, string.Join(",", State.Modes.Keys));
        }
        catch (Exception e)
        {
            logger.LogWarning("{Tag} Sweep failed: {Error}", State.Tag, e);
        }
        return Task.CompletedTask;
    }

    // Globals LocationInfection is the map-screen infection badge, one value per map. Infection here depends on the chosen variant, which
    // the map screen does not know, so any non-zero badge would be wrong on the other variant: every badge is 0. SeasonActivity
    // InfectionHalloween stays as PPES / SPT set it.
    private static void ClearBadges(GlobalTable globalTable)
    {
        var map = globalTable.LocationInfection;
        if (map is null) return;
        foreach (var key in map.Keys.ToList()) map[key] = 0;
    }
}
