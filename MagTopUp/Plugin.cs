// Press a key over a magazine in the inventory/stash screen to fill it from loose ammo.
//
// Acts only on the item under the cursor (the game's own hover tracking,
// ItemUiContext.CurrentItemContext) and only when it is an examined, non-full magazine that is not inside a weapon.
// The key is not consumed, so UI Fixes' own "top up" (merges ammo stacks) keeps working.
//
// Ammo: examined loose stacks (grid items, so ammo boxes, magazines and chambers are never touched)
// from the player's equipment, plus the stash grid out of raid. Per load call the stack is chosen by:
//   1. the template of the magazine's top round (Cartridges.Last, the next one to fire),
//   2. magazine empty: top round of another magazine of the same model, then of the same caliber,
//   3. the strongest ammo (penetration, then damage) in the caliber of the first equipped weapon
//      the magazine fits (primary, secondary, holster), else any ammo the magazine accepts.
// Fill under (default on): the pick above is chosen once per run and remembered per magazine. If there is
// not enough of it to fill the magazine, the gap is filled with other ammo of its caliber: weaker or equal
// types first, closest in strength first, then stronger types (closest first) if still short. Load order
// bottom to top: weaker filler (weakest lowest), pick, stronger filler. Off: one pick per stack until full.
// Each stack goes through the controller's LoadMagazine, exactly like dragging ammo onto the
// magazine: in raid that is the timed load with Mag Drills XP (and Fika's wrapper), out of raid
// the plain transaction. One stack per call, awaited; the sequence stops on failure, on a short
// load (the timed process was cancelled), or when the key is pressed again.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Krane.MagTopUp
{
    [BepInPlugin("krane.magtopup", "Krane's MagTopUp", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<KeyboardShortcut> TopUpKey;
        internal static ConfigEntry<bool> FillUnder;

        private static readonly FieldInfo UiController = AccessTools.Field(typeof(ItemUiContext), "_inventoryController");
        private const int LoadTimeoutMs = 120000;

        private bool _running;
        private bool _cancel;
        private bool _loading;
        private int _gen;
        private ItemController _runController;
        private Magazine _runMag;

        private void Awake()
        {
            Log = Logger;
            TopUpKey = Config.Bind("General", "Top Up Magazine", new KeyboardShortcut(KeyCode.T),
                "Fill the magazine under the cursor from loose ammo (inventory screen only). Press again to cancel");
            FillUnder = Config.Bind("General", "Fill Under Picked Ammo", true,
                "When the picked ammo cannot fill the magazine, load weaker ammo of the same caliber under it "
                + "(closest in strength first), then stronger ammo above it if still short, so the magazine ends full "
                + "with the picked ammo above all weaker rounds");
        }

        private static bool TextboxActive()
        {
            var sel = EventSystem.current?.currentSelectedGameObject;
            return sel != null && sel.activeInHierarchy && sel.GetComponent<TMP_InputField>() != null;
        }

        // CurrentItemContext is not cleared when a screen closes, so also require an open inventory screen.
        private static bool ScreenOpen() => FindObjectOfType<InventoryScreen>() != null;

        // The player's own controller: the one the item UI was configured with, and in raid the live local player's.
        private static bool IsLocal(ItemController ctrl)
        {
            var ui = ItemUiContext.Instance;
            if (ctrl == null || ui == null || UiController?.GetValue(ui) != ctrl) return false;
            if (!(ctrl is Player.PlayerInventoryController)) return true;
            var me = GamePlayerOwner.MyPlayer;
            return me != null && me.InventoryController == ctrl && me.HealthController.IsAlive;
        }

        private void Reset()
        {
            _gen++;
            _running = _cancel = _loading = false;
            _runController = null;
            _runMag = null;
        }

        private void Update()
        {
            if (_running)
            {
                // Raid ended, player died, or the UI moved on: the controller is gone, drop the sequence.
                if (!IsLocal(_runController)) { Reset(); return; }
                // The timed process may not exist yet when the key lands (it waits for the previous one),
                // so retry until our load completes.
                if (_cancel && _loading) _runController.StopProcesses();
            }
            if (!TopUpKey.Value.IsDown() || TextboxActive() || !ScreenOpen()) return;
            var ctx = ItemUiContext.Instance?.CurrentItemContext;
            if (_running)
            {
                // Only our own magazine (or empty space) cancels; T over an ammo stack belongs to UI Fixes.
                if (ctx == null || ctx.Item == _runMag) _cancel = true;
                return;
            }
            if (ctx == null || !(ctx.Item is Magazine mag)) return;
            if (ctx.ViewType != EItemViewType.Inventory && ctx.ViewType != EItemViewType.InventoryWithoutDiscard) return;
            var ctrl = mag.Owner as ItemController;
            if (!IsLocal(ctrl) || !Eligible(mag, ctrl)) return;
            _ = Run(mag, ctrl);
        }

        private static bool Eligible(Magazine mag, ItemController ctrl)
        {
            // Cylinder magazines (revolvers) load through a different Apply path; not supported.
            if (mag is CylinderMagazine) return false;
            if (mag.Parent == null || mag.Owner != ctrl) return false;
            if (mag.Parent.Container is Slot slot && slot.ParentItem is Weapon) return false;
            return ctrl.Examined(mag) && mag.Count < mag.MaxCount;
        }

        private async Task Run(Magazine mag, ItemController ctrl)
        {
            Reset();
            int gen = _gen;
            _running = true;
            _runController = ctrl;
            _runMag = mag;
            try
            {
                if (FillUnder.Value) await RunFill(mag, ctrl, gen);
                else
                    while (gen == _gen && !_cancel && Eligible(mag, ctrl))
                    {
                        var ammo = PickAmmo(mag, ctrl);
                        if (ammo == null || !await LoadStack(ammo, mag, ctrl, int.MaxValue, gen)) break;
                    }
            }
            catch (Exception e) { Log.LogError("top-up failed: " + e); }
            finally { if (gen == _gen) Reset(); }
        }

        // Last fill-under pick per magazine id, plus every ammo type that run loaded. Reused while the top round
        // is one of those types, so a cancelled or shot-down magazine keeps its pick instead of adopting filler.
        private class Pick
        {
            public Ammo Primary;
            public readonly HashSet<string> Loaded = new HashSet<string>();
        }

        private static readonly Dictionary<string, Pick> Picks = new Dictionary<string, Pick>();

        private static string Tpl(Ammo a) => a.TemplateId.ToString();

        // Strength: penetration, then damage.
        private static int Compare(Ammo a, Ammo b)
        {
            int c = a.PenetrationPower.CompareTo(b.PenetrationPower);
            return c != 0 ? c : a.Damage.CompareTo(b.Damage);
        }

        // Bottom to top: weaker filler (weakest lowest; equal strength counts as weaker), the pick, stronger filler.
        // Weaker filler covers only what the pick cannot. Every load re-reads the inventory, so ammo that
        // vanished mid-run is made up by the later phases.
        private async Task RunFill(Magazine mag, ItemController ctrl, int gen)
        {
            var pool = Pool(ctrl);
            var loose = Loose(mag, ctrl, pool);
            var key = mag.Id.ToString();
            if (!Picks.TryGetValue(key, out var pick) || !(mag.FirstRealAmmo() is Ammo top && pick.Loaded.Contains(Tpl(top))))
            {
                var primary = PickAmmo(mag, ctrl, pool, loose);
                if (primary == null) return;
                Picks[key] = pick = new Pick { Primary = primary };
                pick.Loaded.Add(Tpl(primary));
            }
            var pt = Tpl(pick.Primary);

            async Task<bool> Load(Ammo ammo, int count)
            {
                pick.Loaded.Add(Tpl(ammo));
                if (!await LoadStack(ammo, mag, ctrl, count, gen)) return false;
                loose = Loose(mag, ctrl);
                return gen == _gen && !_cancel && Eligible(mag, ctrl);
            }

            List<Ammo> Filler(Func<int, bool> side) => loose
                .Where(a => a.Caliber == pick.Primary.Caliber && Tpl(a) != pt && side(Compare(a, pick.Primary))).ToList();

            while (true)
            {
                int gap = mag.MaxCount - mag.Count - loose.Where(a => Tpl(a) == pt).Sum(a => a.StackObjectsCount);
                if (gap <= 0) break;
                // Closest weaker types until the gap is covered; load the weakest of those first.
                var types = Filler(c => c <= 0).GroupBy(Tpl)
                    .OrderByDescending(g => g.First(), Comparer<Ammo>.Create(Compare)).ThenBy(g => g.Key).ToList();
                if (types.Count == 0) break;
                int covered = 0, i = 0;
                for (; i < types.Count - 1 && covered + types[i].Sum(a => a.StackObjectsCount) < gap; i++)
                    covered += types[i].Sum(a => a.StackObjectsCount);
                if (!await Load(types[i].First(), gap - covered)) return;
            }
            for (Ammo ammo; (ammo = loose.FirstOrDefault(a => Tpl(a) == pt)) != null;)
                if (!await Load(ammo, int.MaxValue)) return;
            for (Ammo ammo; (ammo = Filler(c => c > 0).OrderBy(a => a, Comparer<Ammo>.Create(Compare)).ThenBy(Tpl).FirstOrDefault()) != null;)
                if (!await Load(ammo, int.MaxValue)) return;
        }

        // Loads up to count rounds from one stack. False when the sequence must stop: cancelled, failed,
        // timed out, or a short load (the timed process was cancelled).
        private async Task<bool> LoadStack(Ammo ammo, Magazine mag, ItemController ctrl, int count, int gen)
        {
            int want = Math.Min(count, Math.Min(ammo.StackObjectsCount, mag.MaxCount - mag.Count));
            int before = mag.Count;
            var load = ctrl.LoadMagazine(ammo, mag, want, false);
            _loading = true;
            var done = await Task.WhenAny(load, Task.Delay(LoadTimeoutMs));
            if (gen != _gen) return false;
            _loading = false;
            if (done != load) { Log.LogWarning("load did not finish in time, stopping"); return false; }
            var result = await load;
            return !_cancel && result.Succeed && mag.Count - before >= want;
        }

        // Everything searched: equipment (all nested), plus the stash's own grids out of raid.
        private static List<Item> Pool(ItemController ctrl)
        {
            var inv = (ctrl as InventoryController)?.Inventory;
            var items = new List<Item>();
            if (inv == null) return items;
            items.AddRange(inv.GetPlayerItems(EPlayerItems.Equipment));
            if (!(ctrl is Player.PlayerInventoryController) && inv.Stash != null)
                foreach (var grid in inv.Stash.Grids) items.AddRange(grid.Items);
            return items;
        }

        // First equipped weapon (primary, secondary, holster) whose magazine slot accepts this magazine.
        private static Weapon FittingWeapon(Magazine mag, ItemController ctrl)
        {
            var equipment = (ctrl as InventoryController).Inventory.Equipment;
            foreach (var s in new[] { EquipmentSlot.FirstPrimaryWeapon, EquipmentSlot.SecondPrimaryWeapon, EquipmentSlot.Holster })
                if (equipment.GetSlot(s).ContainedItem is Weapon w && w.GetMagazineSlot()?.CheckCompatibility(mag) == true)
                    return w;
            return null;
        }

        // Examined loose stacks the magazine accepts, in grids only (never ammo boxes, magazines or chambers).
        private static List<Ammo> Loose(Magazine mag, ItemController ctrl, List<Item> pool = null) =>
            (pool ?? Pool(ctrl)).OfType<Ammo>().Where(a =>
                a.Parent.Container is Grid g && !(g.ParentItem is Weapon) && !(g.ParentItem is Mod)
                && a.StackObjectsCount > 0 && !a.IsUsed && ctrl.Examined(a) && mag.CheckCompatibility(a)).ToList();

        private static Ammo PickAmmo(Magazine mag, ItemController ctrl)
        {
            var pool = Pool(ctrl);
            return PickAmmo(mag, ctrl, pool, Loose(mag, ctrl, pool));
        }

        private static Ammo PickAmmo(Magazine mag, ItemController ctrl, List<Item> pool, List<Ammo> loose)
        {
            if (loose.Count == 0) return null;

            // 1. top round of this magazine
            if (mag.FirstRealAmmo() is Ammo top)
            {
                var same = loose.FirstOrDefault(a => a.TemplateId == top.TemplateId);
                if (same != null) return same;
            }

            // 2. empty magazine: copy what another magazine (same model, then same caliber) is loaded with
            if (mag.Count == 0)
            {
                var others = pool.OfType<Magazine>().Where(m => m != mag && m.Count > 0).ToList();
                var cal = FittingWeapon(mag, ctrl)?.AmmoCaliber;
                var sameCaliber = cal == null ? others : others.Where(m => m.FirstRealAmmo() is Ammo r && r.Caliber == cal).ToList();
                foreach (var group in new[] { others.Where(m => m.TemplateId == mag.TemplateId), sameCaliber })
                {
                    var best = group.Select(m => m.FirstRealAmmo() as Ammo).Where(a => a != null)
                        .Select(a => a.TemplateId).Distinct()
                        .Select(t => new { t, n = loose.Where(a => a.TemplateId == t).Sum(a => a.StackObjectsCount) })
                        .Where(x => x.n > 0).OrderByDescending(x => x.n).FirstOrDefault();
                    if (best != null) return loose.First(a => a.TemplateId == best.t);
                }
            }

            // 3. strongest ammo, in the caliber of the first equipped weapon this magazine fits
            var candidates = loose;
            var weapon = FittingWeapon(mag, ctrl);
            if (weapon != null)
            {
                var inCaliber = loose.Where(a => a.Caliber == weapon.AmmoCaliber).ToList();
                if (inCaliber.Count > 0) candidates = inCaliber;
            }
            return candidates.OrderByDescending(a => a.PenetrationPower).ThenByDescending(a => a.Damage).First();
        }
    }
}
