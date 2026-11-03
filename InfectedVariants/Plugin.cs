// Offers an Infected variant on every stock map to MapVariants' map prompt. The server half (InfectedVariants4Server) owns the data and
// the zombies: it says which maps to offer and how (GET /infectedvariants/maps), and the registration happens when the map screen
// is shown, so the server config is the only switch. Registration is reflection only, so a different MapVariants build cannot
// stop this plugin from loading. The UI touch is two Harmony prefixes on MapVariants' prompt, which never run on a headless
// client (no map screen there; such a client registers nothing, and nothing here needs to swap on it).
//
// Factory's Infected name: FactoryClassic registers its third variant as "Halloween 2026" in its Awake, and MapVariants binds the
// F12 DefaultSelection list from that name at registration, so the name has to change BEFORE that call. BepInEx loads plugins in
// GUID order (a SortedDictionary, case-insensitive) with dependencies first, so this plugin's GUID sorts ahead of
// com.lennoxp90.factoryclassic and it has no dependency on it (a dependency, even a soft one, would load it first).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Krane.InfectedVariants
{
    // "com.krane." on purpose: it sorts before "com.lennoxp90.factoryclassic", see the header.
    [BepInPlugin("com.krane.infectedvariants", "Krane's InfectedVariants", "1.2.2")]
    // Hard, and 1.1.0 or later: it also guarantees MapVariants' Awake has run, which makes registering later legal.
    [BepInDependency("com.lennoxp90.mapvariants", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string MapsTypeName = "MapVariants.Client.Api.Maps";
        private const string PromptTypeName = "MapVariants.Client.MapVariantPrompt";
        private const string WindowTypeName = "MapVariants.Client.MapChoiceWindow";
        private const string RequestTypeName = "SPT.Common.Http.RequestHandler";
        private const string Route = "/infectedvariants/maps";
        private const string InfectedId = "infected"; // must equal the id the server declares
        private const string FactoryGuid = "com.lennoxp90.factoryclassic";
        private const string MapVariantsGuid = "com.lennoxp90.mapvariants";
        private const string FactoryId = "factory4_day";           // either Factory id marks FactoryClassic's registration
        private const string FactoryHalloweenId = "halloween2026"; // FactoryClassic's declared variant id
        private const string FactoryOldName = "Halloween 2026";    // the name FactoryClassic gives it
        private const string InfectedName = "Infected";
        private const string InfectedText = "Zombies everywhere";

        internal static ManualLogSource Log;

        private static bool _registered;
        private static int _attempts;
        private const int MaxAttempts = 10;

        // Map key (first location id, lower case) to the subtitle of its backport tile, for every map whose prompt shows no "original"
        // tile; _promptKey is the map of the prompt being built, _inShow marks our own re-invocation of Show.
        private static readonly Dictionary<string, string> BackportText = new Dictionary<string, string>();
        private static string _promptKey;
        private static bool _inShow;
        private static bool _promptPatched;
        private static MethodInfo _show;

        private void Awake()
        {
            Log = Logger;
            HookFactoryRename();
            try
            {
                // Awake runs once per screen instance, so a failed attempt there could never be retried; Show runs on every opening.
                var show = AccessTools.DeclaredMethod(typeof(MatchMakerSelectionLocationScreen), "Show", new[] { typeof(IEftSession), typeof(RaidSettings), typeof(MatchmakerPlayersController) });
                if (show == null) { Log.LogError("MatchMakerSelectionLocationScreen.Show not found; no map is offered as Infected."); return; }
                new Harmony("krane.infectedvariants.screen").Patch(show, postfix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(OnMapScreen))));
            }
            catch (Exception e)
            {
                Log.LogError($"Could not hook the map screen; no map is offered as Infected: {e}");
            }
        }

        // FactoryClassic registers Factory in its Awake and names its third variant "Halloween 2026"; MapVariants composes every label from
        // that name at registration (F12 list) and at use (tile, loading screen). A prefix on RegisterVariants rewrites the name in the
        // call's own array, so nothing is ever bound or stored under the old name. This plugin loads before FactoryClassic (header), so the
        // prefix is in place for that Awake; a FactoryClassic already loaded means the order assumption broke, and is reported.
        private static void HookFactoryRename()
        {
            try
            {
                if (Chainloader.PluginInfos.ContainsKey(FactoryGuid))
                {
                    Log.LogWarning("FactoryClassic loaded before this plugin; Factory's variant keeps the name Halloween 2026.");
                    return;
                }
                var register = RegisterVariantsMethod();
                if (register == null) { Log.LogWarning($"{MapsTypeName}.RegisterVariants not found; Factory's variant keeps its name."); return; }
                new Harmony("krane.infectedvariants.factory").Patch(register, prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(RenameFactoryVariant))));
            }
            catch (Exception e)
            {
                Log.LogError($"Could not hook Factory's variant name; it keeps Halloween 2026: {e}");
            }
        }

        // Parameter names must equal RegisterVariants' own. Void prefix: only the extra names array and, once, the stored F12 value change.
        private static void RenameFactoryVariant(string[] locationIds, string displayName, string[] extraIds, string[] extraNames)
        {
            try
            {
                if (locationIds == null || extraIds == null || extraNames == null || extraIds.Length != extraNames.Length) return;
                if (!locationIds.Any(id => string.Equals(id?.Trim(), FactoryId, StringComparison.OrdinalIgnoreCase))) return;
                var index = Array.FindIndex(extraIds, id => string.Equals(id?.Trim(), FactoryHalloweenId, StringComparison.OrdinalIgnoreCase));
                if (index < 0) { Log.LogDebug("FactoryClassic registered Factory without a Halloween variant; nothing to rename."); return; }
                extraNames[index] = InfectedName;
                MigrateStoredDefault(displayName);
                Log.LogInfo($"Factory's '{FactoryOldName}' variant is named '{InfectedName}'.");
            }
            catch (Exception e)
            {
                Log.LogError($"Could not rename Factory's variant; it keeps Halloween 2026: {e}");
            }
        }

        // MapVariants' F12 DefaultSelection is bound right after this prefix. BepInEx has read the file already and holds a stored value
        // that has no entry yet in OrphanedEntries, which Bind applies; a stored "Halloween 2026" would not be in the new list and would be
        // clamped away, so it is rewritten here. An "Infected" value needs nothing.
        private static void MigrateStoredDefault(string section)
        {
            if (string.IsNullOrEmpty(section)) return;
            var config = Chainloader.PluginInfos.TryGetValue(MapVariantsGuid, out var info) ? info.Instance?.Config : null;
            if (config == null) return;
            // Private in BepInEx 5.4 (ConfigFile.OrphanedEntries), hence reflection.
            var orphans = AccessTools.Property(typeof(ConfigFile), "OrphanedEntries")?.GetValue(config, null) as Dictionary<ConfigDefinition, string>;
            var definition = new ConfigDefinition(section, "DefaultSelection");
            if (orphans != null && orphans.TryGetValue(definition, out var stored) && string.Equals(stored?.Trim(), FactoryOldName, StringComparison.OrdinalIgnoreCase))
            {
                orphans[definition] = InfectedName;
                Log.LogInfo($"Stored {section} DefaultSelection '{stored}' migrated to '{InfectedName}'.");
            }
        }

        private static MethodInfo RegisterVariantsMethod()
        {
            var type = AccessTools.TypeByName(MapsTypeName);
            return type == null ? null : AccessTools.Method(type, "RegisterVariants", new[]
            {
                typeof(string[]), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string[]), typeof(string[]), typeof(string[]), typeof(Action<string>), typeof(Func<string[]>)
            });
        }

        // Every time the map screen is shown until registered: the session exists, so the server can be asked what to register. Retried
        // on the next showing (up to MaxAttempts) when the server did not answer. The request is SPT's synchronous GetJson, once per
        // showing at most. MapVariants prompts only on Next/Ready, after Show, so registration is always in place by then.
        private static void OnMapScreen()
        {
            if (_registered || _attempts >= MaxAttempts) return;
            _attempts++;
            try
            {
                var json = Get(Route);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Log.LogWarning($"The server did not answer {Route} (attempt {_attempts} of {MaxAttempts}); is InfectedVariants4Server installed? No map is offered as Infected.");
                    return;
                }
                var response = JObject.Parse(json); // a bad answer throws here and stays retryable
                _registered = true;
                RegisterAll(response);
            }
            catch (Exception e)
            {
                Log.LogError($"Registering the Infected variants threw (attempt {_attempts} of {MaxAttempts}): {e}");
            }
        }

        private static string Get(string route)
        {
            var type = AccessTools.TypeByName(RequestTypeName);
            var get = type == null ? null : AccessTools.Method(type, "GetJson", new[] { typeof(string) });
            if (get == null)
            {
                Log.LogError($"{RequestTypeName}.GetJson not found; no map is offered as Infected.");
                return null;
            }
            return get.Invoke(null, new object[] { route }) as string;
        }

        private static void RegisterAll(JObject response)
        {
            var maps = response["maps"] as JArray;
            if (maps == null || maps.Count == 0) { Log.LogInfo("The server offers no Infected maps."); return; }

            var type = AccessTools.TypeByName(MapsTypeName);
            if (type == null) { Log.LogError($"{MapsTypeName} not found. MapVariants is a hard dependency, so this should be unreachable."); return; }

            var registerVariants = RegisterVariantsMethod();
            var isManaged = AccessTools.Method(type, "IsManaged", new[] { typeof(string) });
            if (registerVariants == null) { Log.LogError($"{MapsTypeName}.RegisterVariants not found (needs MapVariants 1.1.0, API 3.1); no map is offered as Infected."); return; }

            var offered = new List<string>();
            foreach (var entry in maps.OfType<JObject>())
            {
                var name = (string)entry["name"];
                var backport = (string)entry["backport"] ?? "Vanilla";
                var ids = (entry["ids"] as JArray)?.Select(t => (string)t).Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
                if (string.IsNullOrEmpty(name) || ids == null || ids.Length == 0) continue;

                try
                {
                    if (isManaged != null && ids.Any(id => isManaged.Invoke(null, new object[] { id }) is bool b && b))
                    {
                        Log.LogError($"{name}: MapVariants already manages one of {string.Join(",", ids)} (a maps.json entry?); remove that entry, or {name} is not offered as Infected.");
                        continue;
                    }

                    // Both base names equal: the backport is the default, and the original can then never be the F12 default or a
                    // transit preference (DefaultSelection matches the backport name first). The original tile itself is removed from the prompt.
                    var result = registerVariants.Invoke(null, new object[]
                    {
                        ids, name, backport, backport, null, null,
                        new[] { InfectedId }, new[] { InfectedName }, null,
                        new Action<string>(v => OnDeclaredVariant(name, v)), null
                    });
                    if (result is bool ok && ok)
                    {
                        BackportText[ids[0].Trim().ToLowerInvariant()] = backport.Equals("Vanilla", StringComparison.OrdinalIgnoreCase) ? "Normal map" : backport + " rework";
                        offered.Add($"{name} ({backport} / {InfectedName})");
                    }
                    else Log.LogError($"MapVariants refused the {name} registration; look for a [MV] Register line above this one.");
                }
                catch (Exception e)
                {
                    Log.LogError($"Registering {name} threw; it is not offered as Infected: {e}");
                }
            }

            Log.LogInfo($"Registered with MapVariants: {(offered.Count == 0 ? "nothing" : string.Join(", ", offered))}");
            if (BackportText.Count > 0) PatchPrompt();
        }

        private static void OnDeclaredVariant(string map, string variant)
        {
            // Nothing to swap: the backport mod's patches stay live for both variants (MapVariants never touches a provider's patches).
            // Logged on every call so a raid's log shows which variant this client was told (and when) if a load ever misbehaves;
            // "original" also warns, because it would mean the server serves shipped data under the backport's scenes.
            Log.LogInfo($"{map}: MapVariants resolved '{variant}'.");
            if (string.Equals(variant, "original", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning($"{map} resolved to 'original' (vanilla data); this setup does not offer that tile (the server maps it to the backport).");
        }

        private static void PatchPrompt()
        {
            if (_promptPatched) return;
            var prompt = AccessTools.TypeByName(PromptTypeName);
            var choose = prompt == null ? null : AccessTools.Method(prompt, "ShowChoice");
            var window = AccessTools.TypeByName(WindowTypeName);
            var show = window == null ? null : AccessTools.Method(window, "Show");
            if (choose == null || choose.GetParameters().Length != 2 || show == null || show.GetParameters().Length != 4)
            {
                Log.LogWarning($"{PromptTypeName}.ShowChoice / {WindowTypeName}.Show not found as expected; the prompts of backport-based maps will also show their original tile.");
                return;
            }
            _show = show;
            var harmony = new Harmony("krane.infectedvariants.prompt");
            harmony.Patch(choose, prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(MarkPrompt))));
            harmony.Patch(show, prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(ReshapePrompt))));
            _promptPatched = true;
            Log.LogInfo("Prompt hooks installed (original tile hidden on every map offered here).");
        }

        // ShowChoice(ManagedMap map, Action<string> done) builds the prompt for one map and calls Show synchronously (map screen and
        // transit alike), so the map's key, its first location id, says which prompt Show is building. Matched by id, never by title.
        private static void MarkPrompt(object[] __args)
        {
            _promptKey = null;
            try
            {
                var map = __args[0];
                var key = (map?.GetType().GetProperty("Key")?.GetValue(map, null) as string)?.ToLowerInvariant();
                if (key != null && BackportText.ContainsKey(key)) _promptKey = key;
            }
            catch (Exception e)
            {
                Log.LogError($"Reading the prompt's map failed, it shows its original tile: {e}");
            }
        }

        // Show(string title, Choice[] choices, string[] warnings, Action<string> done). Returns false after calling Show again with the
        // original tile dropped and the other subtitles replaced (Choice is internal, so by reflection). Writing __args[1] would do nothing:
        // Harmony's __args only writes back ref parameters, and these are by value, which is why the earlier version hid nothing.
        private static bool ReshapePrompt(object[] __args)
        {
            var key = _promptKey;
            _promptKey = null;
            if (_inShow || key == null || !BackportText.TryGetValue(key, out var backportText)) return true;
            Array filtered;
            try
            {
                if (!(__args[1] is Array choices)) return true;
                var element = choices.GetType().GetElementType();
                var variant = element.GetField("Variant");
                var description = element.GetField("Description");
                if (variant == null || description == null) { Log.LogWarning("MapVariants' Choice layout changed; the prompt keeps its original tile."); return true; }

                var kept = choices.Cast<object>().Where(c => !string.Equals(variant.GetValue(c) as string, "original", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (kept.Length == 0) return true;
                foreach (var c in kept)
                {
                    var id = variant.GetValue(c) as string;
                    if (string.Equals(id, "backport", StringComparison.OrdinalIgnoreCase)) description.SetValue(c, backportText);
                    else if (string.Equals(id, InfectedId, StringComparison.OrdinalIgnoreCase)) description.SetValue(c, InfectedText);
                }

                filtered = Array.CreateInstance(element, kept.Length);
                for (var i = 0; i < kept.Length; i++) filtered.SetValue(kept[i], i);
                Log.LogDebug($"Prompt for {key}: {choices.Length} tiles reduced to {kept.Length}.");
            }
            catch (Exception e)
            {
                Log.LogError($"Reshaping the prompt failed, it shows its original tile: {e}");
                return true;
            }

            _inShow = true;
            try { _show.Invoke(null, new[] { __args[0], filtered, __args[2], __args[3] }); }
            finally { _inShow = false; }
            return false;
        }
    }
}
