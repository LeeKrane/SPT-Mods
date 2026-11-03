// Offers Labs to MapVariants as "Vanilla" / "Infected" and, when ManimalInterchange is installed, Interchange as
// "Manimal" / "Infected". The server half (InfectedVariants4Server) owns the data: zombies spawn only on Factory
// "Halloween 2026", the Infected Labs and the Infected Interchange. Registration is reflection only, so a different
// MapVariants build cannot stop this plugin from loading. The only UI touch is a Harmony prefix on MapVariants' prompt
// window, which never runs on a headless client (no map screen there).
using System;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;

namespace Krane.InfectedVariants
{
    [BepInPlugin("krane.infectedvariants", "Krane's InfectedVariants", "1.1.0")]
    // Hard, and 1.1.0 or later: it also guarantees MapVariants' Awake has run, which makes registering from Awake legal.
    [BepInDependency("com.lennoxp90.mapvariants", "1.1.0")]
    // Hard: Factory's Halloween 2026 variant (the other infected map) is FactoryClassic's.
    [BepInDependency("com.lennoxp90.factoryclassic", "1.2.0")]
    // Soft: Interchange is offered only when it is installed. Declared so it loads first and Chainloader.PluginInfos is reliable here.
    [BepInDependency(ManimalGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private const string MapsTypeName = "MapVariants.Client.Api.Maps";
        private const string ManimalGuid = "com.manimal.interchange";
        private const string WindowTypeName = "MapVariants.Client.MapChoiceWindow";
        private const string InterchangeTitle = "CHOOSE YOUR INTERCHANGE"; // MapVariants: "CHOOSE YOUR " + DisplayName.ToUpperInvariant()
        private const string InfectedId = "infected";                      // must equal the id the server declares

        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            try
            {
                Register();
            }
            catch (Exception e)
            {
                Log.LogError($"Registering Labs with MapVariants threw; Labs is not offered as Infected: {e}");
            }

            try
            {
                RegisterInterchange();
            }
            catch (Exception e)
            {
                Log.LogError($"Registering Interchange with MapVariants threw; Interchange is not offered as Infected: {e}");
            }
        }

        private static void Register()
        {
            var type = AccessTools.TypeByName(MapsTypeName);
            if (type == null)
            {
                Log.LogError($"{MapsTypeName} not found. MapVariants is a hard dependency, so this should be unreachable.");
                return;
            }

            var register = AccessTools.Method(type, "Register", new[]
            {
                typeof(string[]), typeof(string), typeof(string), typeof(string),
                typeof(string), typeof(string), typeof(Action<string>), typeof(Func<string[]>)
            });
            if (register == null)
            {
                Log.LogError($"{MapsTypeName}.Register has an unexpected signature; Labs is not offered as Infected.");
                return;
            }

            // backportName is the variant's name alone ("Vanilla"); MapVariants composes "Labs - Vanilla" itself.
            // The server registers the vanilla Labs as the backport (what MapVariants rests on and defaults to)
            // and the infected copy as the original, so the default is Vanilla.
            var result = register.Invoke(null, new object[]
            {
                new[] { "laboratory" }, "Labs", "Vanilla", "Infected", null, null, new Action<string>(_ => { }), null
            });

            if (result is bool ok && ok) Log.LogInfo("Registered Labs with MapVariants (Infected / Vanilla)");
            else Log.LogError("MapVariants refused the Labs registration; look for a [MV] Register refused line above this one.");
        }

        // Interchange has no maps.json entry on purpose: this is a provider registration, so MapVariants never touches
        // ManimalInterchange's patches (they stay live for both variants) and no standDown applies. The server auto-discovers
        // Interchange (Manimal data = backport) and "infected" is declared on top of it by InfectedVariants4Server.
        private static void RegisterInterchange()
        {
            if (!Chainloader.PluginInfos.ContainsKey(ManimalGuid)) return; // not installed: skip quietly

            var type = AccessTools.TypeByName(MapsTypeName);
            var register = type == null ? null : AccessTools.Method(type, "RegisterVariants", new[]
            {
                typeof(string[]), typeof(string), typeof(string), typeof(string), typeof(string), typeof(string),
                typeof(string[]), typeof(string[]), typeof(string[]), typeof(Action<string>), typeof(Func<string[]>)
            });
            if (register == null)
            {
                Log.LogError($"{MapsTypeName}.RegisterVariants not found (needs MapVariants 1.1.0, API 3.1); Interchange is not offered as Infected.");
                return;
            }

            // Both base names are "Manimal": the backport is the default, and the original can then never be the F12 default
            // or a transit preference (DefaultSelection matches the backport name first). The Vanilla tile itself is removed
            // from the prompt by HidePlainTile.
            var result = register.Invoke(null, new object[]
            {
                new[] { "interchange" }, "Interchange", "Manimal", "Manimal", null, null,
                new[] { InfectedId }, new[] { "Infected" }, null,
                new Action<string>(OnInterchangeVariant), null
            });

            if (!(result is bool ok && ok))
            {
                Log.LogError("MapVariants refused the Interchange registration (a maps.json entry for interchange would cause this); look for a [MV] Register line above.");
                return;
            }
            Log.LogInfo("Registered Interchange with MapVariants (Manimal / Infected)");
            PatchPrompt();
        }

        private static void OnInterchangeVariant(string variant)
        {
            // Nothing to swap: ManimalInterchange's patches stay live for both. Only "original" is worth a line, because it
            // would mean the server serves shipped data under Manimal's scenes.
            if (string.Equals(variant, "original", StringComparison.OrdinalIgnoreCase))
                Log.LogWarning("Interchange resolved to 'original' (vanilla data); this setup does not offer that. Pick Manimal or Infected.");
        }

        private static void PatchPrompt()
        {
            var window = AccessTools.TypeByName(WindowTypeName);
            var show = window == null ? null : AccessTools.Method(window, "Show");
            if (show == null || show.GetParameters().Length != 4)
            {
                Log.LogWarning($"{WindowTypeName}.Show not found as expected; the Interchange prompt will also show a Vanilla tile.");
                return;
            }
            new Harmony("krane.infectedvariants.prompt").Patch(show,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(Plugin), nameof(HidePlainTile))));
        }

        // Show(string title, Choice[] choices, string[] warnings, Action<string> done). Drops the "original" tile of the
        // Interchange prompt only, by swapping in a filtered array (Choice is internal, so it is built by reflection).
        private static void HidePlainTile(object[] __args)
        {
            try
            {
                if (!(__args[0] is string title) || title != InterchangeTitle || !(__args[1] is Array choices)) return;
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
                Log.LogError($"Hiding the Vanilla Interchange tile failed, the prompt shows it: {e}");
            }
        }
    }
}
