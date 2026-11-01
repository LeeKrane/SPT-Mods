// VisitAPI adds a VISIT button above the trader cards in TraderScreensGroup.SelectTrader and
// places it from the top edge of _traderCardsContainer. Kaeno-TraderScrolling turns that
// container into ScrollRect content and re-anchors it and its parent (in a Show postfix, so
// after the first SelectTrader). From then on VisitAPI measures the re-anchored rect and puts
// the button off screen.
//
// After both mods have run, place the button again from the top edge of a visible trader card,
// which keeps its size and on-screen position while scrolling.
using System.Collections.Generic;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Krane.VisitButtonFix
{
    [BepInPlugin("krane.visitbuttonfix", "VisitButtonFix", "1.0.0")]
    [BepInDependency("com.sora.visitapi", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInDependency("com.kaeno.TraderScrolling", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            // Without TraderScrolling, VisitAPI's own placement is correct.
            if (!Chainloader.PluginInfos.ContainsKey("com.kaeno.TraderScrolling"))
            {
                Log.LogInfo("Kaeno-TraderScrolling not loaded, nothing to fix");
                return;
            }
            new Harmony("krane.visitbuttonfix").PatchAll(typeof(Plugin).Assembly);
        }
    }

    internal static class ButtonPlacer
    {
        private const string ButtonName = "VisitTalkButton";

        private static readonly AccessTools.FieldRef<TraderScreensGroup, List<TraderCard>> SpawnedCards =
            AccessTools.FieldRefAccess<TraderScreensGroup, List<TraderCard>>("_spawnedCards");

        private static bool _logged;

        internal static void Place(TraderScreensGroup screen)
        {
            try
            {
                var parent = screen._closeButton?.transform.parent as RectTransform;
                var button = parent?.Find(ButtonName) as RectTransform;
                if (button == null || !button.gameObject.activeSelf) return;

                RectTransform card = null;
                foreach (var c in SpawnedCards(screen))
                    if (c != null && c.gameObject.activeInHierarchy) { card = (RectTransform)c.transform; break; }
                if (card == null) return;

                // TraderScrolling re-anchors the cards in the same frame; lay them out before measuring.
                if (card.parent is RectTransform cards) LayoutRebuilder.ForceRebuildLayoutImmediate(cards);

                // Same rule as VisitAPI (bottom pivot on the cards' top edge), measured on a card.
                var corners = new Vector3[4];
                card.GetWorldCorners(corners);
                var before = button.anchoredPosition.y;
                var topGap = parent.InverseTransformPoint(corners[1]).y - parent.rect.yMax;
                button.anchoredPosition = new Vector2(0f, topGap);

                if (!_logged)
                {
                    _logged = true;
                    Plugin.Log.LogInfo($"VISIT button moved from y={before:0.#} to y={topGap:0.#} (parent height {parent.rect.height:0.#})");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError("Placing the VISIT button failed: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.SelectTrader))]
    internal static class SelectTraderPatch
    {
        [HarmonyAfter("com.sora.visitapi")]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TraderScreensGroup __instance) => ButtonPlacer.Place(__instance);
    }

    // TraderScrolling re-anchors the cards in its own Show postfix, after the first SelectTrader.
    [HarmonyPatch(typeof(TraderScreensGroup), nameof(TraderScreensGroup.Show))]
    internal static class ShowPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(TraderScreensGroup __instance) => ButtonPlacer.Place(__instance);
    }
}
