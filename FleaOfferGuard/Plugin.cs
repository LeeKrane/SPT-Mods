// Keep QuickSellFlea (com.lacyway.csf) from posting flea offers with an invalid price.
//
// QuickSellFlea posts every selected item at the market average it got from the server. For modded items
// without player offers that average can be NaN, Infinity, 0 or -1, and the multi-select path never checks
// it (the single-item path only checks "<= 0", which NaN passes). The server then stores the bad price in
// the profile and can no longer save it. Two Harmony prefixes on QuickSellFlea's private price callbacks
// drop such items before the button is built, i.e. before any item is removed from the stash or any offer is posted:
//   MultiSell.SetPrices                                    multi-select: invalid items are removed from the
//     sell list and its price table, the rest posts as usual and the button total only counts them. If none
//     are left the original is skipped; the button reads "NO VALID PRICE" and does nothing, and nothing is
//     left locked (CanFetch is restored, CanPost was never cleared, _priceReady stays false). It waits until
//     every item's price really arrived (PriceData.SetPrice is marked), so a late callback from an earlier
//     menu cannot trigger it early.
//   ItemUiContext_GetItemContextInteractions_Patch.SetPrices   single item: same check, original skipped.
//   Both ClickQuickOffer methods: the posting currency can change after the button was built (hotkey), so
//     the check runs again with the current currency right before posting; an invalid item aborts the click
//     with a warning and changes nothing.
// "Valid" means the count QuickSellFlea will post (price, or rounded USD/EUR amount) is finite and still
// above zero rounded to 2 decimals, which is what the server requires. Everything is reached by reflection; if QuickSellFlea is missing or a
// member was renamed, the affected patch is skipped with a log line and QuickSellFlea runs untouched.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using Comfort.Common;
using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.Ragfair;
using HarmonyLib;
using TMPro;

namespace Krane.FleaOfferGuard
{
    [BepInPlugin("krane.fleaofferguard", "Krane's FleaOfferGuard", "1.0.0")]
    [BepInDependency("com.lacyway.csf", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            if (!Chainloader.PluginInfos.ContainsKey("com.lacyway.csf"))
            {
                Log.LogInfo("QuickSellFlea not installed, nothing to guard");
                return;
            }

            Harmony harmony = new Harmony("krane.fleaofferguard");
            Type multiSell = AccessTools.TypeByName("QuickSellFlea.MultiSell");
            Type singlePatch = AccessTools.TypeByName("QuickSellFlea.Patches.ItemUiContext_GetItemContextInteractions_Patch");
            Type plugin = AccessTools.TypeByName("QuickSellFlea.CSF_Plugin");
            Guard.Init(plugin, multiSell, singlePatch);

            PatchPrefix(harmony, multiSell, "SetPrices", nameof(Guard.MultiPrefix), Guard.MultiReady);
            PatchPrefix(harmony, multiSell, "ClickQuickOffer", nameof(Guard.MultiClickPrefix), Guard.MultiReady);
            PatchPrefix(harmony, Guard.PriceDataType, "SetPrice", nameof(Guard.MarkReceived), Guard.MultiReady);
            PatchPrefix(harmony, singlePatch, "SetPrices", nameof(Guard.SinglePrefix), Guard.SingleReady);
            PatchPrefix(harmony, singlePatch, "ClickQuickOffer", nameof(Guard.SingleClickPrefix), Guard.SingleReady);
        }

        private static void PatchPrefix(Harmony harmony, Type type, string method, string prefix, bool ready)
        {
            MethodInfo target = type == null ? null : AccessTools.Method(type, method);
            if (target == null || !ready)
            {
                Log.LogWarning("QuickSellFlea " + (type?.Name ?? "type") + "." + method + " not found or changed, that path is not guarded");
                return;
            }
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(Guard), prefix));
        }
    }

    internal static class Guard
    {
        private static object _currencyEntry;
        private static PropertyInfo _currencyValue;
        private static FieldInfo _canFetch, _itemsToSell, _prices, _multiButton, _singleData, _singleReady;
        private static FieldInfo _average, _singleButton, _multiReady, _buttonText, _singleAverage;

        // PriceData instances whose SetPrice ran, i.e. whose Average is a real answer and not the default 0.
        private static readonly ConditionalWeakTable<object, object> Received = new ConditionalWeakTable<object, object>();

        internal static bool MultiReady, SingleReady;
        internal static Type PriceDataType;

        internal static void Init(Type plugin, Type multiSell, Type singlePatch)
        {
            _buttonText = AccessTools.Field(typeof(ContextMenuButton), "_text");
            PropertyInfo currencyProp = plugin == null ? null : AccessTools.Property(plugin, "PostingCurrency");
            _currencyEntry = currencyProp?.GetValue(null, null);
            _currencyValue = _currencyEntry?.GetType().GetProperty("Value");
            bool currencyOk = _currencyEntry != null && _currencyValue != null;

            if (multiSell != null)
            {
                _canFetch = AccessTools.Field(multiSell, "CanFetch");
                _itemsToSell = AccessTools.Field(multiSell, "_itemsToSell");
                _prices = AccessTools.Field(multiSell, "_prices");
                _multiButton = AccessTools.Field(multiSell, "_simpleContextMenuButton");
                _multiReady = AccessTools.Field(multiSell, "_priceReady");
                PriceDataType = AccessTools.Inner(multiSell, "PriceData");
                _average = PriceDataType == null ? null : AccessTools.Field(PriceDataType, "Average");
                MultiReady = currencyOk && _canFetch != null && _itemsToSell != null && _prices != null
                    && _multiButton != null && _multiReady != null && _average != null && PriceDataType != null;
            }

            if (singlePatch != null)
            {
                _singleData = AccessTools.Field(singlePatch, "_postPriceData");
                _singleReady = AccessTools.Field(singlePatch, "_priceReady");
                _singleButton = _singleData?.FieldType.GetField("SimpleContextMenuButton");
                _singleAverage = _singleData?.FieldType.GetField("AveragePrice");
                SingleReady = currencyOk && _singleData != null && _singleReady != null && _singleButton != null && _singleAverage != null;
            }
        }

        // QuickSellFlea's posting-currency enum values double as the divisor (RUB 1, USD 120, EUR 133).
        // Read on every call: the currency can be changed with a hotkey while a menu is open.
        private static int Divisor()
        {
            return Math.Max(1, Convert.ToInt32(_currencyValue.GetValue(_currencyEntry, null)));
        }

        // The count QuickSellFlea would post for this average, with its float math (ConvertToUSD/EUR:
        // MathF.Round(value / 120f or 133f, 0, AwayFromZero)). The server rounds counts to 2 decimals and
        // rejects zero, so the same bar applies here.
        private static bool IsValid(float avg, int divisor)
        {
            if (float.IsNaN(avg) || float.IsInfinity(avg) || avg <= 0f)
            {
                return false;
            }
            float count = divisor == 1 ? avg : (float)Math.Round((double)(avg / (float)divisor), MidpointRounding.AwayFromZero);
            return !float.IsInfinity(count) && Math.Round((double)count, 2) > 0;
        }

        private static void SetButtonText(object button, string text)
        {
            // Unity null check: the button may be a destroyed object.
            UnityEngine.Object unityButton = button as UnityEngine.Object;
            if (unityButton == null || _buttonText == null)
            {
                return;
            }
            TMP_Text tmp = _buttonText.GetValue(button) as TMP_Text;
            if (tmp != null)
            {
                tmp.text = text;
            }
        }

        private static void Notify(string message)
        {
            Plugin.Log.LogWarning("QuickSellFlea: " + message);
            if (Singleton<NotificationManager>.Instantiated)
            {
                NotificationManager.DisplayWarningNotification("FleaOfferGuard: " + message);
            }
        }

        private static string SkippedMessage(int skipped)
        {
            return skipped + " item(s) skipped, no valid flea price";
        }

        internal static void MarkReceived(object __instance)
        {
            if (__instance != null)
            {
                Received.GetValue(__instance, _ => new object());
            }
        }

        // Items whose price is invalid in the current currency (nothing is changed). Returns null while a
        // price is still missing (not an answer yet).
        private static List<Item> FindInvalid(bool requireAllReceived)
        {
            List<Item> items = (List<Item>)_itemsToSell.GetValue(null);
            IDictionary prices = (IDictionary)_prices.GetValue(null);
            if (requireAllReceived)
            {
                foreach (Item item in items)
                {
                    object data = prices.Contains(item.Id) ? prices[item.Id] : null;
                    if (data == null || !Received.TryGetValue(data, out _))
                    {
                        return null;
                    }
                }
            }

            int divisor = Divisor();
            List<Item> invalid = new List<Item>();
            foreach (Item item in items)
            {
                object data = prices.Contains(item.Id) ? prices[item.Id] : null;
                if (data != null && !IsValid((float)_average.GetValue(data), divisor))
                {
                    Plugin.Log.LogWarning("QuickSellFlea: no valid price for " + item.ShortName + " (" + item.TemplateId + "), average " + _average.GetValue(data));
                    invalid.Add(item);
                }
            }
            return invalid;
        }

        // Runs when the last price for the multi-select arrives, before the button text and total are built.
        // Everything is worked out first; state is only changed at the end.
        internal static bool MultiPrefix()
        {
            List<Item> invalid;
            try
            {
                invalid = FindInvalid(true);
                if (invalid == null)
                {
                    return false; // early trigger by a stale callback; the real last price calls SetPrices again
                }
                if (invalid.Count == 0)
                {
                    return true;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Multi-sell guard failed, QuickSellFlea runs unguarded: " + e);
                return true;
            }

            try
            {
                List<Item> items = (List<Item>)_itemsToSell.GetValue(null);
                IDictionary prices = (IDictionary)_prices.GetValue(null);
                foreach (Item item in invalid)
                {
                    items.Remove(item);
                    prices.Remove(item.Id);
                }
                Notify(SkippedMessage(invalid.Count));
                if (items.Count > 0)
                {
                    return true;
                }
                _canFetch.SetValue(null, true);
                _multiReady.SetValue(null, false);
                SetButtonText(_multiButton.GetValue(null), "NO VALID PRICE");
                return false;
            }
            catch (Exception e)
            {
                // Half-changed state must not reach QuickSellFlea: fail closed.
                Plugin.Log.LogError("Multi-sell guard failed while changing state, nothing will be posted: " + e);
                try { _multiReady.SetValue(null, false); _canFetch.SetValue(null, true); } catch (Exception) { }
                return false;
            }
        }

        // The posting currency may have changed since the button was built. The click is aborted with a
        // notification and nothing is changed, so the button keeps showing what it will post once the
        // currency is changed back (or the menu is reopened).
        internal static bool MultiClickPrefix()
        {
            try
            {
                if (!(bool)_multiReady.GetValue(null))
                {
                    return true; // original ignores the click
                }
                List<Item> invalid = FindInvalid(false);
                if (invalid == null || invalid.Count == 0)
                {
                    return true;
                }
                Notify(invalid.Count + " item(s) have no valid price in the current posting currency, nothing posted. Reopen the menu");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Multi-sell click guard failed, QuickSellFlea runs unguarded: " + e);
                return true;
            }
        }

        // Runs for the single-item (and Shift select-all) quick offer, before AveragePrice is stored.
        internal static bool SinglePrefix(ItemMarketPrices prices)
        {
            try
            {
                if (IsValid(prices.avg, Divisor()))
                {
                    return true;
                }
                Plugin.Log.LogWarning("QuickSellFlea: invalid average price " + prices.avg + ", offer not offered");
                Notify("Not listed, no valid flea price for this item");
                // A stale "ready" flag from an earlier menu would let a click post the empty price data.
                _singleReady.SetValue(null, false);
                SetButtonText(_singleButton.GetValue(_singleData.GetValue(null)), "NO VALID PRICE");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Single-sell guard failed, QuickSellFlea runs unguarded: " + e);
                return true;
            }
        }

        internal static bool SingleClickPrefix()
        {
            try
            {
                if (!(bool)_singleReady.GetValue(null))
                {
                    return true; // original ignores the click
                }
                float avg = (float)_singleAverage.GetValue(_singleData.GetValue(null));
                if (avg == 0f)
                {
                    // Ready flag left over from an earlier menu, this menu's price has not arrived: ignore the click
                    // (QuickSellFlea would post an empty price).
                    return false;
                }
                if (IsValid(avg, Divisor()))
                {
                    return true;
                }
                Notify("Not listed, no valid flea price in the current posting currency");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Single-sell click guard failed, QuickSellFlea runs unguarded: " + e);
                return true;
            }
        }
    }
}
