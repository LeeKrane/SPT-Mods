// Disable buying player flea offers per handbook subcategory.
//
// One bool config entry per non-root handbook category, created once both the handbook (RagFair.UpdateSettings)
// and the full locale (LocalizationManager.UpdateLocales) are loaded. Section = top-level
// category, key = path below it. A disabled category also covers every category and item below it.
// Harmony patches using the resulting set:
//   EftClientBackendSession.GetOffers (prefix + postfix)   keeps flea pages full. A selection that is in or
//     contains a disabled category (also all offers, linked or needed search) asks for page 0 with MaxOffers
//     offers instead, drops disabled player offers and hands the game the requested page cut from the rest,
//     with the total count set to what is left, so paging stays right. Owner filter and tree counts are
//     left to the server.
//   RagFair.ProceedWithOffers (prefix)        drops disabled player offers from every page before it is
//     cached or shown (catches anything the request patch did not cover). Trader and own offers stay, and
//     the browse tree is untouched (its counts come from the server and still include the dropped offers).
//   RagfairScreen.OfferPurchaseHandler (prefix)   refuses to open the purchase window for a disabled player
//     offer (money or barter, single or mass buy) and shows a warning.
// Changing a setting clears the offer cache.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Comfort.Common;
using EFT;
using EFT.Communications;
using EFT.HandBook;
using EFT.UI.Ragfair;
using HarmonyLib;

namespace Krane.FleaCategoryBlock
{
    [BepInPlugin("krane.fleacategoryblock", "Krane's FleaCategoryBlock", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;

        private static readonly Dictionary<string, ConfigEntry<bool>> Entries = new Dictionary<string, ConfigEntry<bool>>();
        private static readonly HashSet<string> Disabled = new HashSet<string>();
        private static RagFair _ragfair;
        private static string _profileId;
        internal static ConfigEntry<int> MaxOffers;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            MaxOffers = Config.Bind("General", "Max offers per request", 1000, new ConfigDescription(
                "Offers fetched at once when a selection contains a disabled category; pages are cut from them.",
                new AcceptableValueRange<int>(100, 5000)));
            MaxOffers.SettingChanged += (_, __) => _ragfair?.ClearCachedInfo();
            new Harmony("krane.fleacategoryblock").PatchAll(typeof(Patches));
        }

        internal static void SetRagfair(RagFair ragfair, Profile profile)
        {
            _ragfair = ragfair;
            _profileId = profile?.Id;
        }

        // Needs the handbook and the full locale (category names). The handbook loads during profile load,
        // the full locale only afterwards (DataPrepareOperation.Run), so this is tried from both hooks.
        internal static void TryBuildConfig()
        {
            if (Entries.Count > 0 || !Singleton<Handbook>.Instantiated)
            {
                return;
            }
            Handbook handbook = Singleton<Handbook>.Instance;
            Dictionary<string, HandbookData> categories = handbook.Categories.Where(c => c?.Id != null).GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.First());
            if (!categories.Values.Any(c => !string.IsNullOrEmpty(c.ParentId) && Name(c) != c.Id))
            {
                return;
            }

            HashSet<string> usedKeys = new HashSet<string>();
            bool saveOnSet = Instance.Config.SaveOnConfigSet;
            Instance.Config.SaveOnConfigSet = false;
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
                entry.SettingChanged += (_, __) => Refresh();
            }
            Instance.Config.Save();
            Instance.Config.SaveOnConfigSet = saveOnSet;
            Refresh();
            Log.LogInfo("Created " + Entries.Count + " category settings");
        }

        private static void Refresh()
        {
            Disabled.Clear();
            foreach (KeyValuePair<string, ConfigEntry<bool>> pair in Entries)
            {
                if (pair.Value.Value)
                {
                    Disabled.Add(pair.Key);
                }
            }
            _ragfair?.ClearCachedInfo();
        }

        // Handbook id (category or item template) -> disabled category id at or above it, or null.
        internal static string DisabledCategory(string id)
        {
            if (Disabled.Count == 0 || id == null)
            {
                return null;
            }
            for (HandbookNode node = Singleton<Handbook>.Instance?[id]; node != null; node = node.Parent)
            {
                if (node.Data != null && Disabled.Contains(node.Data.Id))
                {
                    return node.Data.Id;
                }
            }
            return null;
        }

        // True when the selection is in or contains a disabled category: fetch MaxOffers and page on the client.
        internal static bool PageOnClient(string handbookId)
        {
            if (Disabled.Count == 0)
            {
                return false;
            }
            if (string.IsNullOrEmpty(handbookId) || DisabledCategory(handbookId) != null)
            {
                return true;
            }
            Handbook handbook = Singleton<Handbook>.Instance;
            foreach (string id in Disabled)
            {
                for (HandbookNode node = handbook?[id]; node != null; node = node.Parent)
                {
                    if (node.Data?.Id == handbookId)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static string DisabledCategory(Offer offer)
        {
            if (offer?.Item == null || offer.User == null || (offer.MemberType & EMemberCategory.Trader) != 0
                || (_profileId != null && offer.User.Id.ToString() == _profileId))
            {
                return null;
            }
            return DisabledCategory(offer.Item.StringTemplateId);
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

    internal static class Patches
    {
        [HarmonyPatch(typeof(RagFair), nameof(RagFair.UpdateSettings))]
        [HarmonyPostfix]
        private static void UpdateSettingsPostfix(RagFair __instance, Profile profile)
        {
            Plugin.SetRagfair(__instance, profile);
            TryBuildConfig();
        }

        [HarmonyPatch(typeof(LocalizationManager), nameof(LocalizationManager.UpdateLocales))]
        [HarmonyPostfix]
        private static void UpdateLocalesPostfix()
        {
            TryBuildConfig();
        }

        private static void TryBuildConfig()
        {
            try
            {
                Plugin.TryBuildConfig();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Building category settings failed: " + e);
            }
        }

        internal class Paging
        {
            public int Page;
            public int Limit;
        }

        [HarmonyPatch(typeof(EftClientBackendSession), nameof(EftClientBackendSession.GetOffers))]
        [HarmonyPrefix]
        private static void GetOffersPrefix(ref int page, ref int limit, int offerOwnerType, string handbookId,
            Dictionary<string, int> buildItems, out Paging __state)
        {
            __state = null;
            if (offerOwnerType == (int)EOfferOwnerType.TraderOwnerType || (buildItems != null && buildItems.Count > 0) || limit <= 0
                || !Plugin.PageOnClient(handbookId))
            {
                return;
            }
            __state = new Paging { Page = page, Limit = limit };
            page = 0;
            limit = Math.Max(Plugin.MaxOffers.Value, limit);
        }

        [HarmonyPatch(typeof(EftClientBackendSession), nameof(EftClientBackendSession.GetOffers))]
        [HarmonyPostfix]
        private static void GetOffersPostfix(ref Task<Result<OffersList>> __result, Paging __state)
        {
            if (__state != null && __result != null)
            {
                __result = CutPage(__result, __state);
            }
        }

        private static async Task<Result<OffersList>> CutPage(Task<Result<OffersList>> request, Paging paging)
        {
            Result<OffersList> result = await request;
            OffersList list = result.Failed ? null : result.Value;
            if (list?.offers == null)
            {
                return result;
            }
            try
            {
                Offer[] kept = list.offers.Where(o => Plugin.DisabledCategory(o) == null).ToArray();
                if (list.offersCount > list.offers.Length)
                {
                    Plugin.Log.LogWarning("Server has " + list.offersCount + " offers, only " + list.offers.Length
                        + " fetched (Max offers per request); the rest is not shown");
                }
                list.offersCount = kept.Length;
                list.offers = kept.Skip(paging.Page * paging.Limit).Take(paging.Limit).ToArray();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Cutting offer page failed: " + e);
            }
            return result;
        }

        [HarmonyPatch(typeof(RagFair), nameof(RagFair.ProceedWithOffers))]
        [HarmonyPrefix]
        private static void ProceedWithOffersPrefix(OffersList result)
        {
            if (result?.offers != null)
            {
                result.offers = result.offers.Where(o => Plugin.DisabledCategory(o) == null).ToArray();
            }
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
    }
}
