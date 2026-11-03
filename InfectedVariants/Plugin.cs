// Offers Labs to MapVariants as "Vanilla" / "Infected". The server half (InfectedVariants4Server) owns the data:
// zombies spawn only on Factory "Halloween 2026" and on the Infected Labs. Registration is reflection only, so a
// different MapVariants build cannot stop this plugin from loading; nothing here touches UI, so it is headless safe.
using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace Krane.InfectedVariants
{
    [BepInPlugin("krane.infectedvariants", "Krane's InfectedVariants", "1.0.0")]
    // Hard, and 1.1.0 or later: it also guarantees MapVariants' Awake has run, which makes registering from Awake legal.
    [BepInDependency("com.lennoxp90.mapvariants", "1.1.0")]
    // Hard: Factory's Halloween 2026 variant (the other infected map) is FactoryClassic's.
    [BepInDependency("com.lennoxp90.factoryclassic", "1.2.0")]
    public class Plugin : BaseUnityPlugin
    {
        private const string MapsTypeName = "MapVariants.Client.Api.Maps";

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
    }
}
