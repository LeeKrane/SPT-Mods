// Disable buying player flea offers per handbook subcategory. Thin client for the server mod
// FleaCategoryBlock4Server, which does the filtering.
//
// One bool config entry per non-root handbook category, created once both the handbook (RagFair.UpdateSettings)
// and the full locale (LocalizationManager.UpdateLocales) are loaded. Section = top-level category, key = path
// below it. Section "0. Global" (sorts first) holds the master switch and one entry per top-level category that
// blocks the whole category. The ids of the enabled entries (none while the master switch is off) are posted to /krane/fleacategoryblock/set once per login
// (after the settings exist) and on every change; the server saves them per profile. The server drops other
// players' offers in and below those categories from this session's searches before paging; trader offers,
// own offers and the category tree counts are untouched. After a change the cached offer pages are dropped
// once the server has the new list.
// Fallback: RagfairScreen.OfferPurchaseHandler (prefix) refuses to buy a disabled player offer that is still on
// screen (shown before a change, or the server mod is missing) and shows a warning.
using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HandBook;
using EFT.UI.Ragfair;
using HarmonyLib;
using SPT.Common.Http;

namespace Krane.FleaCategoryBlock
{
    [BepInPlugin("krane.fleacategoryblock", "Krane's FleaCategoryBlock", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private const string Route = "/krane/fleacategoryblock/set";
        private const string GlobalSection = "0. Global";

        private static readonly Dictionary<string, ConfigEntry<bool>> Entries = new Dictionary<string, ConfigEntry<bool>>();
        private static ConfigEntry<bool> _enabled;
        private static readonly HashSet<string> Disabled = new HashSet<string>();
        private static RagFair _ragfair;
        private static string _profileId;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            new Harmony("krane.fleacategoryblock").PatchAll(typeof(Patches));
        }

        internal static void SetRagfair(RagFair ragfair, Profile profile)
        {
            _ragfair = ragfair;
            _profileId = profile?.Id;
        }

        // Needs the handbook and the full locale (category names). The handbook loads during profile load,
        // the full locale only afterwards (DataPrepareOperation.Run), so this is tried from both hooks.
        // Returns true when the settings were created by this call.
        internal static bool TryBuildConfig()
        {
            if (Entries.Count > 0 || !Singleton<Handbook>.Instantiated)
            {
                return false;
            }
            Handbook handbook = Singleton<Handbook>.Instance;
            Dictionary<string, HandbookData> categories = handbook.Categories.Where(c => c?.Id != null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            if (!categories.Values.Any(c => !string.IsNullOrEmpty(c.ParentId) && Name(c) != c.Id))
            {
                return false;
            }

            HashSet<string> usedKeys = new HashSet<string>();
            bool saveOnSet = Instance.Config.SaveOnConfigSet;
            Instance.Config.SaveOnConfigSet = false;
            _enabled = Instance.Config.Bind(GlobalSection, "Enabled", true,
                new ConfigDescription("Master switch: off shows every flea offer again, whatever the settings below say.", null,
                    new ConfigurationManagerAttributes { Order = 1 }));
            _enabled.SettingChanged += (_, __) => Send(true);
            foreach (HandbookData root in categories.Values.Where(c => string.IsNullOrEmpty(c.ParentId)).OrderBy(c => c.Order))
            {
                string key = Clean(Name(root));
                string unique = key;
                for (int n = 2; !usedKeys.Add(GlobalSection + "\n" + unique); n++)
                {
                    unique = key + " (" + n + ")";
                }
                ConfigEntry<bool> entry = Instance.Config.Bind(GlobalSection, unique, false);
                Entries[root.Id] = entry;
                entry.SettingChanged += (_, __) => Send(true);
            }
            foreach (HandbookData category in categories.Values.OrderBy(c => c.Order))
            {
                if (string.IsNullOrEmpty(category.ParentId))
                {
                    continue;
                }

                // Walk up to the root: the root names the section, everything below it forms the key.
                List<string> path = new List<string> { Name(category) };
                HandbookData root = category;
                while (!string.IsNullOrEmpty(root.ParentId) && categories.TryGetValue(root.ParentId, out HandbookData parent))
                {
                    root = parent;
                    if (!string.IsNullOrEmpty(root.ParentId))
                    {
                        path.Insert(0, Name(root));
                    }
                }

                string section = Clean(Name(root));
                string key = Clean(string.Join(" / ", path));
                string unique = key;
                for (int n = 2; !usedKeys.Add(section + "\n" + unique); n++)
                {
                    unique = key + " (" + n + ")";
                }

                ConfigEntry<bool> entry = Instance.Config.Bind(section, unique, false);
                Entries[category.Id] = entry;
                entry.SettingChanged += (_, __) => Send(true);
            }
            Instance.Config.Save();
            Instance.Config.SaveOnConfigSet = saveOnSet;
            Log.LogInfo("Created " + Entries.Count + " category settings");
            return true;
        }

        // Posts the enabled category ids (an empty list clears the server side). On a change the cached offer
        // pages are dropped once the server has the new list, so the next load is filtered by it.
        internal static async void Send(bool changed)
        {
            if (Entries.Count == 0)
            {
                return;
            }
            Disabled.Clear();
            if (_enabled.Value)
            {
                Disabled.UnionWith(Entries.Where(p => p.Value.Value).Select(p => p.Key));
            }
            string json = "{\"categories\":[" + string.Join(",", Disabled.Select(id => "\"" + id + "\"")) + "]}";
            try
            {
                await RequestHandler.PostJsonAsync(Route, json);
                if (changed)
                {
                    _ragfair?.ClearCachedInfo();
                }
            }
            catch (Exception e)
            {
                Log.LogWarning("Sending disabled categories failed (is FleaCategoryBlock4Server installed?): " + e.Message);
            }
        }

        // Disabled category id at or above the offer's item, for other players' offers only; null otherwise.
        internal static string DisabledCategory(Offer offer)
        {
            if (Disabled.Count == 0 || offer?.Item == null || offer.User == null || (offer.MemberType & EMemberCategory.Trader) != 0
                || (_profileId != null && offer.User.Id.ToString() == _profileId))
            {
                return null;
            }
            for (HandbookNode node = Singleton<Handbook>.Instance?[offer.Item.StringTemplateId]; node != null; node = node.Parent)
            {
                if (node.Data != null && Disabled.Contains(node.Data.Id))
                {
                    return node.Data.Id;
                }
            }
            return null;
        }

        internal static string DisplayName(string categoryId)
        {
            return Singleton<Handbook>.Instance?[categoryId]?.Data is HandbookData data ? Name(data) : categoryId;
        }

        private static string Name(HandbookData data)
        {
            // Raw handbook categories carry no Name; the game localizes them by Id (HandbookData.CloneAs).
            string name = (string.IsNullOrEmpty(data.Name) ? data.Id : data.Name).Localized();
            return string.IsNullOrEmpty(name) ? data.Id : name;
        }

        private static string Clean(string text)
        {
            foreach (char c in new[] { '=', '\n', '\t', '\\', '"', '\'', '[', ']' })
            {
                text = text.Replace(c.ToString(), "");
            }
            return text.Trim();
        }
    }

    // Read by Configuration Manager through reflection (matched by type name).
    internal sealed class ConfigurationManagerAttributes
    {
        public int? Order;
    }

    internal static class Patches
    {
        // Runs at every login: creates the settings once the names are loaded, then sends the list once.
        [HarmonyPatch(typeof(RagFair), nameof(RagFair.UpdateSettings))]
        [HarmonyPostfix]
        private static void UpdateSettingsPostfix(RagFair __instance, Profile profile)
        {
            Plugin.SetRagfair(__instance, profile);
            Run(() => Plugin.TryBuildConfig());
            Run(() => Plugin.Send(false));
        }

        // The full locale usually arrives after UpdateSettings; the first login's send happens here then.
        [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.UpdateLocales))]
        [HarmonyPostfix]
        private static void UpdateLocalesPostfix()
        {
            Run(() =>
            {
                if (Plugin.TryBuildConfig())
                {
                    Plugin.Send(true);
                }
            });
        }

        [HarmonyPatch(typeof(RagfairScreen), nameof(RagfairScreen.OfferPurchaseHandler))]
        [HarmonyPrefix]
        private static bool OfferPurchaseHandlerPrefix(Dictionary<IExchangeable, int> offers)
        {
            if (offers == null)
            {
                return true;
            }
            foreach (IExchangeable key in offers.Keys)
            {
                string category = Plugin.DisabledCategory(key as Offer);
                if (category != null)
                {
                    NotificationManager.DisplayWarningNotification("Flea purchases of " + Plugin.DisplayName(category) + " are disabled (FleaCategoryBlock)");
                    return false;
                }
            }
            return true;
        }

        private static void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("FleaCategoryBlock failed: " + e);
            }
        }
    }
}
