// Offers an Infected variant on every stock map to MapVariants' map prompt. The server half (InfectedVariants4Server) owns the data and
// the zombies: it says which maps to offer and how (GET /infectedvariants/maps), and the registration happens when the map screen
// is shown, so the server config is the only switch. Registration is reflection only, so a different MapVariants build cannot
// stop this plugin from loading. The UI touch is two Harmony prefixes on MapVariants' prompt, which never run on a headless
// client (no map screen there; such a client registers nothing, and nothing here needs to swap on it).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using EFT;
using EFT.UI.Matchmaker;
using HarmonyLib;
using Newtonsoft.Json.Linq;

namespace Krane.InfectedVariants
{
    [BepInPlugin("krane.infectedvariants", "Krane's InfectedVariants", "1.2.1")]
    // Hard, and 1.1.0 or later: it also guarantees MapVariants' Awake has run, which makes registering later legal.
    [BepInDependency("com.lennoxp90.mapvariants", "1.1.0")]
    // Hard: Factory's Halloween 2026 variant (the other infected map) is FactoryClassic's.
    [BepInDependency("com.lennoxp90.factoryclassic", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string MapsTypeName = "MapVariants.Client.Api.Maps";
        private const string PromptTypeName = "MapVariants.Client.MapVariantPrompt";
        private const string WindowTypeName = "MapVariants.Client.MapChoiceWindow";
        private const string RequestTypeName = "SPT.Common.Http.RequestHandler";
        private const string Route = "/infectedvariants/maps";
        private const string InfectedId = "infected"; // must equal the id the server declares
        private const string ClientMapsTypeName = "MapVariants.Client.ClientMaps";
        private const string FactoryKey = "factory4_day";      // either Factory id finds the one map FactoryClassic registered
        private const string FactoryHalloweenId = "halloween2026"; // FactoryClassic's declared variant id
        private const string InfectedName = "Infected";

        internal static ManualLogSource Log;

        private static bool _registered;
        private static int _attempts;
        private const int MaxAttempts = 10;

        // Map keys (first location id) whose prompt shows no "original" tile, and whether the prompt being built is one of them.
        private static readonly HashSet<string> HideOriginalFor = new HashSet<string>();
        private static bool _hideOriginalNow;
        private static bool _promptPatched;

        private void Awake()
        {
            Log = Logger;
            RenameFactoryHalloween();
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

        // FactoryClassic registered Factory in its Awake (ours runs after it: hard dependency), naming its third variant "Halloween 2026".
        // Every label (prompt tile, loading screen, notifications) is composed from the registered ExtraVariant.Name at use, so renaming that
        // one object renames them all; the F12 DefaultSelection list was built from the old name once, so its array and the stored value follow.
        private static void RenameFactoryHalloween()
        {
            try
            {
                var clientMaps = AccessTools.TypeByName(ClientMapsTypeName);
                var set = clientMaps == null ? null : AccessTools.Field(clientMaps, "Set")?.GetValue(null);
                var map = set?.GetType().GetMethod("For", new[] { typeof(string) })?.Invoke(set, new object[] { FactoryKey });
                if (map == null) { Log.LogWarning($"Factory is not registered with MapVariants (or its layout changed); its Halloween 2026 variant keeps that name."); return; }

                var extra = (map.GetType().GetProperty("Extras")?.GetValue(map, null) as Array)?.Cast<object>()
                    .FirstOrDefault(x => string.Equals(x.GetType().GetProperty("Id")?.GetValue(x, null) as string, FactoryHalloweenId, StringComparison.OrdinalIgnoreCase));
                var nameProp = extra?.GetType().GetProperty("Name");
                var oldName = nameProp?.GetValue(extra, null) as string;
                var field = extra == null ? null : AccessTools.Field(extra.GetType(), "<Name>k__BackingField");
                if (field == null || string.IsNullOrEmpty(oldName)) { Log.LogWarning("Factory's Halloween 2026 variant was not found; it keeps that name."); return; }
                if (oldName == InfectedName) return;
                field.SetValue(extra, InfectedName);

                // F12: the option list and, when it holds the old name, the stored default (it would otherwise read as an invalid value).
                var key = map.GetType().GetProperty("Key")?.GetValue(map, null) as string;
                var defaults = AccessTools.Field(clientMaps, "Defaults")?.GetValue(null) as System.Collections.IDictionary;
                if (key != null && defaults != null && defaults[key] is BepInEx.Configuration.ConfigEntryBase entry)
                {
                    if (entry.Description?.AcceptableValues is BepInEx.Configuration.AcceptableValueList<string> list)
                        for (var i = 0; i < list.AcceptableValues.Length; i++)
                            if (list.AcceptableValues[i] == oldName) list.AcceptableValues[i] = InfectedName;
                    if (string.Equals(entry.GetSerializedValue(), oldName, StringComparison.OrdinalIgnoreCase)) entry.BoxedValue = InfectedName;
                }
                Log.LogInfo($"Renamed Factory's '{oldName}' variant to '{InfectedName}'.");
            }
            catch (Exception e)
            {
                Log.LogError($"Could not rename Factory's Halloween 2026 variant; it keeps that name: {e}");
            }
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

            var registerVariants = AccessTools.Method(type, "RegisterVariants", new[]
            {
                typeof(string[]), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string[]), typeof(string[]), typeof(string[]), typeof(Action<string>), typeof(Func<string[]>)
            });
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
                        new[] { InfectedId }, new[] { "Infected" }, null,
                        new Action<string>(v => OnDeclaredVariant(name, v)), null
                    });
                    if (result is bool d && d) HideOriginalFor.Add(ids[0].Trim().ToLowerInvariant());

                    if (result is bool ok && ok) { offered.Add($"{name} ({backport} / Infected)"); }
                    else Log.LogError($"MapVariants refused the {name} registration; look for a [MV] Register line above this one.");
                }
                catch (Exception e)
                {
                    Log.LogError($"Registering {name} threw; it is not offered as Infected: {e}");
                }
            }

            Log.LogInfo($"Registered with MapVariants: {(offered.Count == 0 ? "nothing" : string.Join(", ", offered))}");
            if (HideOriginalFor.Count > 0) PatchPrompt();
        }

        private static void OnDeclaredVariant(string map, string variant)
        {
            // Nothing to swap: the backport mod's patches stay live for both variants (MapVariants never touches a provider's patches).
            // Logged on every call so a raid's log shows which variant this client was told (and when) if a load ever misbehaves;
            // "original" also warns, because it would mean the server serves shipped data under the backport's scenes.
            Log.LogInfo($"{map}: MapVariants resolved '{variant}'.");
            if (string.Equals(variant, "original", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning($"{map} resolved to 'original' (vanilla data); this setup does not offer that. Pick the other two.");
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
            var harmony = new Harmony("krane.infectedvariants.prompt");
            harmony.Patch(choose, prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(MarkPrompt))));
            harmony.Patch(show, prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(HideOriginalTile))));
            _promptPatched = true;
        }

        // ShowChoice(ManagedMap map, Action<string> done) builds the prompt for one map and calls Show synchronously (map screen and
        // transit alike), so the map's key, its first location id, says which prompt Show is building. Matched by id, never by title.
        private static void MarkPrompt(object[] __args)
        {
            _hideOriginalNow = false;
            try
            {
                var map = __args[0];
                var key = map?.GetType().GetProperty("Key")?.GetValue(map, null) as string;
                _hideOriginalNow = key != null && HideOriginalFor.Contains(key.ToLowerInvariant());
            }
            catch (Exception e)
            {
                Log.LogError($"Reading the prompt's map failed, it shows its original tile: {e}");
            }
        }

        // Show(string title, Choice[] choices, string[] warnings, Action<string> done). Drops the "original" tile when MarkPrompt flagged
        // this prompt, by swapping in a filtered array (Choice is internal, so it is built by reflection).
        private static void HideOriginalTile(object[] __args)
        {
            if (!_hideOriginalNow) return;
            _hideOriginalNow = false;
            try
            {
                if (!(__args[1] is Array choices)) return;
                var element = choices.GetType().GetElementType();
                var variant = element.GetField("Variant");
                if (variant == null) return;

                var kept = choices.Cast<object>().Where(c => !string.Equals(variant.GetValue(c) as string, "original", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (kept.Length == choices.Length || kept.Length == 0) return;

                var filtered = Array.CreateInstance(element, kept.Length);
                for (var i = 0; i < kept.Length; i++) filtered.SetValue(kept[i], i);
                __args[1] = filtered; // Harmony writes __args back to the parameters
            }
            catch (Exception e)
            {
                Log.LogError($"Hiding the original tile failed, the prompt shows it: {e}");
            }
        }
    }
}
