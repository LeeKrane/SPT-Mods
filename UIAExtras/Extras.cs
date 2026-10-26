// Pinned-item main wheel for UIA's quick-use wheel (Items mode).
//
// Layout: the wheel has MainPages (1-3) pages of SlotsPerPage (4-8) slots. A slot holds one pinned exact
// item template or nothing (blank, not selectable). UIA's own category machinery is not used any more:
// PopulateEntries (postfix) drops UIA's 8 category entries and puts the main entries first
//   [MainPages * SlotsPerPage main entries][UIA's normal item entries]
// and QuickUseWheelPages.Count/Range (prefixes) page over that list: in main mode only pages
// 0..MainPages-1 exist (SlotsPerPage entries each), in selection mode only the item pages exist (UIA's
// quick-select pages, reached by pressing the Pin Key on a main slot, never by plain scrolling).
//
// Selection mode (static Selecting): started by the Pin Key on a hovered main slot; the Pin Key or left
// click on an item pins its template to that slot and returns to the same main page; Esc / right click
// returns without pinning (UpdateOpenWheel prefix skips UIA's own Esc/RMB close); releasing the wheel key
// closes and never uses the selection (Close prefix). Every Open starts on main page 0.
//
// A pinned template is looked up fresh in UIA's wheel items every populate (examined, usable, UIA's
// grouped-selection rule). A carried but not usable pinned item (queued, ACCESSING, UNAVAILABLE) shows UIA's
// own entry with its real state; "not carried" and "carried but empty" fall back (see Fallback below).
//
// Fallback ("Category Fallback" = Auto): the pinned item's category is the set of interchangeable treatment
// categories its template belongs to (Medkits, light/heavy bleed, fracture, surgery, painkillers), computed
// from the item itself (meds template + damage effects, ignoring the remaining resource) the moment it is
// pinned or first seen carried, and persisted per template id in a hidden config entry so it is known when
// the item is not carried. A carried usable item replaces the pin if UIA's (resource-aware) classifier matches
// at least ONE of those categories; the candidate matching the most of them wins (ties: UIA's own order, so a
// pinned full kit prefers another full kit over a bandage). Stimulators and every non-medical item have an
// empty set and never fall back.
//
// Hover: UIA's UpdateSelectedIndex maps blank entries to "nothing selected" and its view only draws the
// highlight for usable entries, so a postfix on UpdateSelectedIndex selects the hovered main slot (blank
// included) and a postfix on QuickUseWheelView.UpdatePresentation draws the highlight for blank / unavailable
// ones. A selected blank or NONE CARRIED entry has no item, so releasing / clicking uses nothing.
//
// Cost: HasQuickUseCategorySlots is forced true (UIA keeps its category-page layout) except while
// QuickUseWheelInventory.Populate runs, where it reads false so UIA skips building category candidates (our
// Apply discards them). No UIA config is touched.
//
// Hot paths (populate every 0.15 s, per-frame hover) use compiled accessors and typed struct access:
// no reflection Invoke/GetValue, no boxing, no per-refresh string allocations.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using JsonType;
using UnityEngine;

namespace Krane.UIAExtras
{
    public enum CategoryFallback { Auto, Never }

    internal static class Slots
    {
        internal const int MaxPages = 3, MaxPerPage = 8, MaxPins = MaxPages * MaxPerPage, UiaSlots = 8;
        private const string Ns = "UseItemsAnywhere.QuickUseWheel.";

        internal static ConfigEntry<int> MainPagesEntry, SlotsPerPageEntry;
        internal static ConfigEntry<KeyboardShortcut> PinKey, ClearKey;
        internal static ConfigEntry<CategoryFallback> FallbackEntry;
        private static ConfigEntry<string> _classesEntry;
        internal static readonly ConfigEntry<string>[] PinItem = new ConfigEntry<string>[MaxPins];

        private static Type _categoryType;
        private static int _pseudoCategory;
        internal static int PseudoCategory => _pseudoCategory;

        // Interchangeable treatment categories: QuickUseCategory value, and (for classification by item)
        // the damage effect that puts a Meds item into it (null = Medkits, decided by the template type).
        private sealed class Treatment
        {
            public int Category;
            public EDamageEffectType? Effect;
        }
        private static readonly List<Treatment> _treatments = new List<Treatment>();

        // Reflection cache (UIA internals).
        internal static FieldInfo _fHasCat, _fMode, _fEntries, _fInventory, _fPlayer, _fCatChanged;
        private static FieldInfo _fSel, _fPage, _fPageStart, _fPageCount, _fIsOpen, _fSelVec, _fPending;
        internal static FieldInfo _fSelField => _fSel;
        internal static FieldInfo _fPageCountField => _fPageCount;
        internal static FieldInfo _fPageStartField => _fPageStart;
        internal static PropertyInfo _pEntryItem, _pWheelItem, _pWheelUsable, _pInvItems;
        private static ConstructorInfo _entryCtor;
        private static MethodInfo _mPopulate, _mRefresh, _mClose, _mPlaySound;
        internal static MethodInfo _mAddItemEntry, _mHasResource;
        internal static Func<Player, Item, Item, int> _comparePreferred;
        private static Func<Item, int, bool> _matches;

        // Compiled accessors for the per-frame / per-refresh paths.
        private static Func<object, bool> _gHasCat, _gIsOpen, _gPending;
        private static Func<object, int> _gMode, _gSel, _gPage, _gPageStart, _gPageCount;
        private static Func<object, Vector2> _gSelVec;
        private static Action<object, bool> _sCatChanged, _sPending;
        private static Action<object, int> _sPage;
        private static IPinApplier _applier;
        private static Action<object, int> _sSel;
        private static MethodInfo _mToggleFav;
        private static bool _allowFavorite, _inPopulate;

        // Selection mode state. _mainPage = main page to return to; _selectIdx = pin index being bound.
        internal static bool Selecting;
        private static int _selectIdx = -1, _mainPage;
        // Main-wheel slot the mouse currently points at (blank slots included), -1 = centre / none.
        private static int _hover = -1;
        private static bool _bindClick, _restorePending;

        // Pinned templates, parsed once per config change; template -> interchangeable category bitmask.
        private static readonly string[] _pins = new string[MaxPins];
        private static readonly MongoID[] _pinIds = new MongoID[MaxPins];
        private static readonly bool[] _pinValid = new bool[MaxPins];
        private static readonly Dictionary<string, int> _classes = new Dictionary<string, int>(StringComparer.Ordinal);
        private static readonly object[] _noneObj = new object[MaxPins];
        private static readonly string[] _nonePin = new string[MaxPins];
        private static object _blank;

        // Layout snapshot taken by the last successful Apply: paging (Count/Range) and slot lookups use it so they
        // always agree with the entries actually built, even if the config changed since.
        private static int _snapPages = 2, _snapPer = MaxPerPage;
        internal static void SetSnapshot(int pages, int per) { _snapPages = pages; _snapPer = per; }

        internal static int MainPages() => MainPagesEntry == null ? 2 : Mathf.Clamp(MainPagesEntry.Value, 1, MaxPages);
        internal static int SlotsPerPage() => SlotsPerPageEntry == null ? MaxPerPage : Mathf.Clamp(SlotsPerPageEntry.Value, 4, MaxPerPage);
        internal static int Idx(int page, int slot) => page * MaxPerPage + slot;

        internal static bool HasPin(int idx) => (_pins[idx] ?? string.Empty).Length > 0;
        internal static bool PinValid(int idx) => _pinValid[idx];
        internal static MongoID PinId(int idx) => _pinIds[idx];
        internal static object Blank() => _blank;

        private static void RefreshPin(int idx)
        {
            var e = PinItem[idx];
            string v = e == null || e.Value == null ? string.Empty : e.Value.Trim();
            if (v != _pins[idx])
            {
                _pins[idx] = v;
                _noneObj[idx] = null;
            }
            _pinValid[idx] = false;
            if (v.Length > 0)
            {
                try { _pinIds[idx] = new MongoID(v); _pinValid[idx] = true; }
                catch (Exception) { }
            }
        }

        private static FieldInfo Field(Type t, string name) =>
            AccessTools.Field(t, name) ?? throw new InvalidOperationException("Field not found: " + t.Name + "." + name);

        private static PropertyInfo Prop(Type t, string name) =>
            AccessTools.Property(t, name) ?? throw new InvalidOperationException("Property not found: " + t.Name + "." + name);

        private static MethodInfo Meth(Type t, string name) =>
            AccessTools.Method(t, name) ?? throw new InvalidOperationException("Method not found: " + t.Name + "." + name);

        private static Type UiaType(string name) =>
            AccessTools.TypeByName(Ns + name) ?? throw new InvalidOperationException("Type not found: " + Ns + name);

        // ---- setup ---------------------------------------------------------------------------------

        internal static void Patch(Harmony h, ConfigFile cfg, Type controller)
        {
            _categoryType = UiaType("QuickUseCategory");
            var entryType = UiaType("QuickUseWheelEntry");
            var wheelItemType = UiaType("QuickUseWheelItem");
            var inventoryType = UiaType("QuickUseWheelInventory");
            var pagesType = UiaType("QuickUseWheelPages");
            var namesType = UiaType("QuickUseCategoryNames");
            var classifierType = UiaType("QuickUseCategoryClassifier");
            var viewStateType = UiaType("QuickUseWheelViewState");
            var configType = AccessTools.TypeByName("UseItemsAnywhere.Configuration") ?? throw new InvalidOperationException("UIA Configuration type not found");

            _pseudoCategory = Enum.GetValues(_categoryType).Cast<object>().Max(v => Convert.ToInt32(v)) + 1;
            var table = new[]
            {
                new KeyValuePair<string, EDamageEffectType?>("Medkits", null),
                new KeyValuePair<string, EDamageEffectType?>("LightBleedTreatment", EDamageEffectType.LightBleeding),
                new KeyValuePair<string, EDamageEffectType?>("HeavyBleedTreatment", EDamageEffectType.HeavyBleeding),
                new KeyValuePair<string, EDamageEffectType?>("FractureTreatment", EDamageEffectType.Fracture),
                new KeyValuePair<string, EDamageEffectType?>("Surgery", EDamageEffectType.DestroyedPart),
                new KeyValuePair<string, EDamageEffectType?>("Painkillers", EDamageEffectType.Pain),
            };
            foreach (var kv in table)
            {
                if (!Enum.IsDefined(_categoryType, kv.Key)) throw new InvalidOperationException("QuickUseCategory." + kv.Key + " not found.");
                _treatments.Add(new Treatment { Category = Convert.ToInt32(Enum.Parse(_categoryType, kv.Key)), Effect = kv.Value });
            }

            _fHasCat = Field(controller, "_hasCategoryPage");
            _fMode = Field(controller, "_mode");
            _fEntries = Field(controller, "_entries");
            _fInventory = Field(controller, "_inventory");
            _fPlayer = Field(controller, "_player");
            _fIsOpen = Field(controller, "_isOpen");
            _fSelVec = Field(controller, "_selectionVector");
            _fCatChanged = Field(controller, "_categorySelectionChangedThisUpdate");
            _fSel = Field(controller, "_selectedIndex");
            _fPage = Field(controller, "_page");
            _fPageStart = Field(controller, "_pageStartIndex");
            _fPageCount = Field(controller, "_pageItemCount");
            _fPending = Field(controller, "_openedFromPendingRequest");
            _pEntryItem = Prop(entryType, "Item");
            _pWheelItem = Prop(wheelItemType, "Item");
            _pWheelUsable = Prop(wheelItemType, "IsUsable");
            _pInvItems = Prop(inventoryType, "Items");
            var ctors = entryType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            _entryCtor = ctors.Length == 1 && ctors[0].GetParameters().Length == 13 ? ctors[0] : throw new InvalidOperationException("QuickUseWheelEntry constructor shape changed.");
            var vsCtors = viewStateType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (vsCtors.Length != 1 || vsCtors[0].GetParameters().Length != 5) throw new InvalidOperationException("QuickUseWheelViewState constructor shape changed.");
            _mPopulate = Meth(controller, "PopulateEntries");
            _mRefresh = Meth(controller, "RefreshWheel");
            _mClose = Meth(controller, "Close");
            _mPlaySound = Meth(controller, "PlaySound");
            _mAddItemEntry = Meth(controller, "AddItemEntry");
            _mHasResource = Meth(inventoryType, "HasResource");
            _comparePreferred = (Func<Player, Item, Item, int>)Delegate.CreateDelegate(typeof(Func<Player, Item, Item, int>), Meth(inventoryType, "ComparePreferredItems"));
            _matches = Fast.Matcher(Meth(classifierType, "Matches"));

            _gHasCat = Fast.Getter<bool>(_fHasCat);
            _gIsOpen = Fast.Getter<bool>(_fIsOpen);
            _gPending = Fast.Getter<bool>(_fPending);
            _gMode = Fast.Getter<int>(_fMode);
            _gSel = Fast.Getter<int>(_fSel);
            _gPage = Fast.Getter<int>(_fPage);
            _gPageStart = Fast.Getter<int>(_fPageStart);
            _gPageCount = Fast.Getter<int>(_fPageCount);
            _gSelVec = Fast.Getter<Vector2>(_fSelVec);
            _sCatChanged = Fast.Setter<bool>(_fCatChanged);
            _sPending = Fast.Setter<bool>(_fPending);
            _sPage = Fast.Setter<int>(_fPage);
            _sSel = Fast.Setter<int>(_fSel);
            _applier = (IPinApplier)Activator.CreateInstance(typeof(PinApplier<,>).MakeGenericType(entryType, wheelItemType));
            _blank = _entryCtor.Invoke(new object[]
            {
                "EMPTY SLOT", "Empty slot", string.Empty, string.Empty,
                false, false, false, false, false, null, null, Enum.ToObject(_categoryType, 0), true
            });

            BindConfig(cfg);
            _snapPages = MainPages();
            _snapPer = SlotsPerPage();

            // Paging: main pages / selection item pages instead of UIA's category page + item pages.
            var count = Meth(pagesType, "Count");
            h.Patch(count, prefix: new HarmonyMethod(typeof(Slots), nameof(CountPrefix)));
            Plugin.VerifyPatched(count, Plugin.ExtrasHarmonyId, prefix: true);
            var range = Meth(pagesType, "Range");
            h.Patch(range, prefix: new HarmonyMethod(typeof(Slots), nameof(RangePrefix)));
            Plugin.VerifyPatched(range, Plugin.ExtrasHarmonyId, prefix: true);

            h.Patch(_mPopulate, postfix: new HarmonyMethod(typeof(Slots), nameof(PopulatePostfix)));
            Plugin.VerifyPatched(_mPopulate, Plugin.ExtrasHarmonyId, postfix: true);

            var has = AccessTools.PropertyGetter(configType, "HasQuickUseCategorySlots") ?? throw new InvalidOperationException("HasQuickUseCategorySlots getter not found");
            h.Patch(has, prefix: new HarmonyMethod(typeof(Slots), nameof(HasSlotsPrefix)));
            Plugin.VerifyPatched(has, Plugin.ExtrasHarmonyId, prefix: true);

            var invPopulate = Meth(inventoryType, "Populate");
            h.Patch(invPopulate, prefix: new HarmonyMethod(typeof(Slots), nameof(InvPopulatePrefix)), finalizer: new HarmonyMethod(typeof(Slots), nameof(InvPopulateFinalizer)));
            Plugin.VerifyPatched(invPopulate, Plugin.ExtrasHarmonyId, prefix: true);

            var display = Meth(namesType, "DisplayName");
            h.Patch(display, postfix: new HarmonyMethod(typeof(DisplayNamePatch<>).MakeGenericType(_categoryType).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public)));
            Plugin.VerifyPatched(display, Plugin.ExtrasHarmonyId, postfix: true);

            var fav = _mToggleFav = Meth(controller, "ToggleSelectedFavorite");
            h.Patch(fav, prefix: new HarmonyMethod(typeof(Slots), nameof(FavoritePrefix)));
            Plugin.VerifyPatched(fav, Plugin.ExtrasHarmonyId, prefix: true);

            // UpdateOpenWheel: prefix handles selection-mode Esc/RMB/LMB and remembers the selected item;
            // postfix tracks hover, binds, and handles the Pin / Clear keys.
            var open = Meth(controller, "UpdateOpenWheel");
            h.Patch(open, prefix: new HarmonyMethod(typeof(Slots), nameof(UpdateOpenPrefix)), postfix: new HarmonyMethod(typeof(Slots), nameof(UpdateOpenPostfix)));
            Plugin.VerifyPatched(open, Plugin.ExtrasHarmonyId, prefix: true, postfix: true);

            // Close: a close in selection mode (wheel key released) never uses the selected item.
            h.Patch(_mClose, prefix: new HarmonyMethod(typeof(Slots), nameof(ClosePrefix)));
            Plugin.VerifyPatched(_mClose, Plugin.ExtrasHarmonyId, prefix: true);

            // UpdateSelectedIndex: select blank main slots too (UIA maps them to -1) so they highlight.
            var selIdx = Meth(controller, "UpdateSelectedIndex");
            h.Patch(selIdx, postfix: new HarmonyMethod(typeof(Slots), nameof(SelectedIndexPostfix)));
            Plugin.VerifyPatched(selIdx, Plugin.ExtrasHarmonyId, postfix: true);

            // Selection mode: releasing the wheel key must not play UIA's "used" sound.
            h.Patch(_mPlaySound, prefix: new HarmonyMethod(typeof(Slots), nameof(SoundPrefix)));
            Plugin.VerifyPatched(_mPlaySound, Plugin.ExtrasHarmonyId, prefix: true);

            var viewType = UiaType("QuickUseWheelView");
            var viewPres = Meth(viewType, "UpdatePresentation");
            var vpPost = typeof(HighlightPatch<>).MakeGenericType(entryType).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
            h.Patch(viewPres, postfix: new HarmonyMethod(vpPost));
            Plugin.VerifyPatched(viewPres, Plugin.ExtrasHarmonyId, postfix: true);

            var hint = Meth(controller, "GetSelectionHint");
            h.Patch(hint, postfix: new HarmonyMethod(typeof(Slots), nameof(HintPostfix)));
            Plugin.VerifyPatched(hint, Plugin.ExtrasHarmonyId, postfix: true);

            var viewState = Meth(controller, "GetViewState");
            var vsPost = typeof(ViewStatePatch<>).MakeGenericType(viewStateType).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
            h.Patch(viewState, postfix: new HarmonyMethod(vsPost));
            Plugin.VerifyPatched(viewState, Plugin.ExtrasHarmonyId, postfix: true);
        }

        private static void BindConfig(ConfigFile cfg)
        {
            MainPagesEntry = cfg.Bind("Main Wheel", "Main Pages", 2,
                new ConfigDescription("Number of pages of pinned slots on the Items wheel (1-3). Scroll the mouse wheel to switch pages.",
                    new AcceptableValueRange<int>(1, MaxPages)));
            SlotsPerPageEntry = cfg.Bind("Main Wheel", "Slots Per Page", MaxPerPage,
                new ConfigDescription("Slots on each main page (4-8). Pins in slots beyond this count are kept but hidden.",
                    new AcceptableValueRange<int>(4, MaxPerPage)));

            PinKey = cfg.Bind("Wheel Pins", "Pin Key", new KeyboardShortcut(KeyCode.Mouse2),
                "Items wheel only. On a hovered main-wheel slot: opens the item list to choose what to pin there (scroll between the item pages). In the item list: pins the hovered item and returns to the main wheel; left click does the same, right click / Esc go back without pinning, releasing the wheel key closes the wheel. Replaces Use Items Anywhere's middle-mouse favorite toggle while the wheel is open. Other held keys (movement, sprint) are ignored; if the Clear Key uses the same key with more modifiers, the Clear Key wins when its modifiers are held.");
            ClearKey = cfg.Bind("Wheel Pins", "Clear Key", new KeyboardShortcut(KeyCode.Mouse2, KeyCode.LeftControl),
                "Items wheel only. While hovering a pinned main-wheel slot: removes its pin. Other held keys are ignored.");
            FallbackEntry = cfg.Bind("Wheel Pins", "Category Fallback", CategoryFallback.Auto,
                "What a pinned slot shows when its item is not carried or only carried empty (a carried item that is merely busy, queued or unavailable is shown with its real state instead). Auto = another carried usable item that shares at least one category with the pinned item, preferring the one that shares the most (a pinned Grizzly takes another full kit before a bandage). Only interchangeable treatment categories count (medkits, bleed/fracture treatment, surgery, painkillers; stimulators never); Never = the pinned item greyed out as NONE CARRIED.");

            PinKey.SettingChanged += (sender, args) => RefreshKeys();
            ClearKey.SettingChanged += (sender, args) => RefreshKeys();
            RefreshKeys();

            for (int p = 0; p < MaxPages; p++)
                for (int s = 0; s < MaxPerPage; s++)
                {
                    int idx = Idx(p, s);
                    // Hidden from the F12 menu: pins are only set and cleared in the wheel.
                    PinItem[idx] = cfg.Bind("Pinned Items", "Page " + (p + 1) + " Slot " + (s + 1) + " Item", string.Empty,
                        new ConfigDescription("Item template id pinned to main-wheel page " + (p + 1) + " slot " + (s + 1) + " (empty = none). Set in the wheel with the Pin Key.",
                            null, new ConfigurationManagerAttributes { Browsable = false }));
                    PinItem[idx].SettingChanged += (sender, args) => RefreshPin(idx);
                    RefreshPin(idx);
                }

            _classesEntry = cfg.Bind("Pinned Items", "Template Categories", string.Empty,
                new ConfigDescription("Interchangeable-category bitmask per pinned template id (id:mask;...), recorded by the mod so the fallback works while the item is not carried.",
                    null, new ConfigurationManagerAttributes { Browsable = false }));
            foreach (var part in (_classesEntry.Value ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int c = part.IndexOf(':');
                if (c > 0 && int.TryParse(part.Substring(c + 1), out int mask)) _classes[part.Substring(0, c)] = mask;
            }
        }

        // ---- paging (QuickUseWheelPages) -----------------------------------------------------------

        // Item pages of selection mode follow the main entries.
        private static int MainTotal() => _snapPages * _snapPer;

        private static bool CountPrefix(int entryCount, int itemsPerPage, bool hasCategoryPage, ref int __result)
        {
            if (!hasCategoryPage) return true;
            if (!Selecting) { __result = _snapPages; return false; }
            int items = Math.Max(0, entryCount - MainTotal());
            __result = Math.Max(1, (items + itemsPerPage - 1) / itemsPerPage);
            return false;
        }

        private static bool RangePrefix(int page, int entryCount, int itemsPerPage, bool hasCategoryPage, ref (int Start, int Count) __result)
        {
            if (!hasCategoryPage) return true;
            int per = _snapPer;
            if (!Selecting)
            {
                page = Math.Max(0, Math.Min(page, _snapPages - 1));
                int start = page * per;
                __result = (start, Math.Min(per, Math.Max(0, entryCount - start)));
                return false;
            }
            int total = MainTotal();
            int pages = Math.Max(1, (Math.Max(0, entryCount - total) + itemsPerPage - 1) / itemsPerPage);
            page = Math.Max(0, Math.Min(page, pages - 1));
            int s = total + page * itemsPerPage;
            __result = (s, Math.Min(itemsPerPage, Math.Max(0, entryCount - s)));
            return false;
        }

        // ---- simple postfixes ----------------------------------------------------------------------

        // UIA always builds its category-page layout; PopulateEntries' postfix swaps the page's content. Inside
        // QuickUseWheelInventory.Populate it reads false so UIA skips its (discarded) category-candidate scan.
        private static bool HasSlotsPrefix(ref bool __result)
        {
            __result = !_inPopulate;
            return false;
        }

        private static void InvPopulatePrefix() => _inPopulate = true;

        private static void InvPopulateFinalizer() => _inPopulate = false;

        // Selection mode: UIA plays its "used" ButtonClick on wheel-key release; nothing is used, so cancel instead.
        private static void SoundPrefix(ref EUISoundType soundType)
        {
            if (Selecting && soundType == EUISoundType.ButtonClick) soundType = EUISoundType.MenuEscape;
        }

        // Hovered main slot, blank ones included (UIA's UpdateSelectedIndex reports -1 for blanks).
        private static void SelectedIndexPostfix(object __instance)
        {
            if (Selecting || !ViewActive(__instance)) return;
            int count = _gPageCount(__instance);
            if (count <= 0) return;
            var v = _gSelVec(__instance);
            if (v.sqrMagnitude <= 104f * 104f) return;
            float angle = Mathf.Atan2(v.x, v.y) * 57.29578f;
            if (angle < 0f) angle += 360f;
            float slice = 360f / count;
            int idx = Mathf.FloorToInt((angle + slice * 0.5f) / slice) % count;
            _sSel(__instance, idx);
        }

        // ---- text ----------------------------------------------------------------------------------

        private static string KeyText(KeyboardShortcut k) =>
            k.MainKey == KeyCode.None ? "(unbound)" : k.ToString().Replace("Mouse2", "MMB").Replace("Mouse1", "RMB").Replace("Mouse0", "LMB")
                .Replace("LeftControl", "CTRL").Replace("LeftShift", "SHIFT").Replace("LeftAlt", "ALT").ToUpperInvariant();

        // Key texts / modifier arrays, recomputed when the Pin / Clear Key settings change (never per frame).
        private static KeyCode _pinMain, _clearMain;
        private static KeyCode[] _pinMods = new KeyCode[0], _clearMods = new KeyCode[0];
        private static string _pinText = string.Empty, _hintBind = string.Empty, _hintRebind = string.Empty, _hintBlank = string.Empty,
            _hintRemoveNext = string.Empty, _hintCenter = string.Empty, _ctrlSelect = string.Empty, _ctrlPending = string.Empty, _ctrlNormal = string.Empty;

        private static void RefreshKeys()
        {
            var pin = PinKey.Value;
            var clear = ClearKey.Value;
            _pinMain = pin.MainKey;
            _clearMain = clear.MainKey;
            _pinMods = pin.Modifiers.ToArray();
            _clearMods = clear.Modifiers.ToArray();
            _pinText = KeyText(pin);
            string clearText = KeyText(clear);
            _hintBind = "LMB / " + _pinText + ": PIN HERE";
            _hintRebind = _pinText + ": REBIND   \u2022   " + clearText + ": CLEAR";
            _hintBlank = _pinText + ": PIN AN ITEM HERE";
            _hintRemoveNext = _pinText + ": REMOVE NEXT ITEM";
            _hintCenter = "CENTER TO CANCEL   \u2022   " + _pinText + " ON A SLOT TO PIN";
            _ctrlSelect = "LMB / " + _pinText + " PIN HOVERED ITEM   \u2022   RMB / ESC BACK   \u2022   RELEASE TO CLOSE";
            string tail = _pinText + " PIN   \u2022   " + clearText + " CLEAR   \u2022   ESC / RIGHT CLICK TO CANCEL";
            _ctrlPending = "LMB CONFIRM   \u2022   " + tail;
            _ctrlNormal = "RELEASE TO USE   \u2022   " + tail;
        }

        internal static string HeaderText() => Selecting
            ? "PIN TO PAGE " + (_selectIdx / MaxPerPage + 1) + " SLOT " + (_selectIdx % MaxPerPage + 1)
            : "QUICK SLOTS";

        internal static string ControlsText(bool pending) => Selecting ? _ctrlSelect : pending ? _ctrlPending : _ctrlNormal;

        internal static bool ViewActive(object ctrl) => _gIsOpen(ctrl) && _gMode(ctrl) == 0 && _gHasCat(ctrl);
        internal static bool PendingOpen(object ctrl) => _gPending(ctrl);

        // UIA's pending-mode / favorite / "release to use" hints do not apply here: replaced per mode.
        private static void HintPostfix(object __instance, ref string __result)
        {
            if (__result == null || PinKey == null || !ViewActive(__instance)) return;
            if (Selecting)
            {
                // Item pages: LMB / Pin Key pins the hovered item; releasing uses nothing.
                int sel = _gSel(__instance);
                if (sel >= 0 && sel < _gPageCount(__instance)) __result = _hintBind;
                return;
            }
            switch (_applier.SelectedKind(__instance))
            {
                case EntryKind.Blank: __result = _hintBlank; return;
                case EntryKind.NoneCarried: __result = _hintRebind; return;
                case EntryKind.NextQueued: __result = _hintRemoveNext; return;
            }
            if (__result == "MMB: FAVORITE" || __result == "MMB: UNFAVORITE") __result = _hintRebind;
            else if (__result == "CENTER TO CANCEL") __result = _hintCenter;
        }

        // ---- hover ---------------------------------------------------------------------------------

        // The hovered main slot is whatever UIA's selected index says: SelectedIndexPostfix selects blank
        // slots too. Called from the UpdateOpenWheel postfix.
        private static void TrackHover(object ctrl)
        {
            _hover = -1;
            if (Selecting || !ViewActive(ctrl)) return;
            int sel = _gSel(ctrl);
            if (sel >= 0 && sel < _gPageCount(ctrl) && sel < _snapPer) _hover = sel;
        }

        // ---- populate ------------------------------------------------------------------------------

        private static void PopulatePostfix(object __instance, bool __result)
        {
            if (!__result || !ViewActiveForPopulate(__instance)) return;
            try { _applier.Apply(__instance); }
            catch (Exception e) { LogOnce("populate", e); }
        }

        // PopulateEntries runs before/while opening: _isOpen may still be false.
        private static bool ViewActiveForPopulate(object ctrl) => _gMode(ctrl) == 0 && _gHasCat(ctrl);

        private static readonly HashSet<string> _logged = new HashSet<string>();

        internal static void LogOnce(string key, Exception e)
        {
            if (_logged.Add(key))
                Plugin.Log.LogError("[UIA Extras] " + key + " failed (logged once): " + e);
        }

        private static string TemplateOf(Item item) => item.TemplateId.ToString();

        private static string Truncate(string text, int max) => text.Length <= max ? text : text.Substring(0, max - 2) + "…";

        // The NONE CARRIED entry (a boxed QuickUseWheelEntry) for a pin, built once per pin.
        internal static object NoneCarried(int idx)
        {
            if (_noneObj[idx] == null || _nonePin[idx] != _pins[idx])
            {
                _noneObj[idx] = MakeNoneCarried(_pins[idx]);
                _nonePin[idx] = _pins[idx];
            }
            return _noneObj[idx];
        }

        private static object MakeNoneCarried(string pin)
        {
            string shortName = pin, full = pin;
            try
            {
                var id = new MongoID(pin);
                string sn = id.LocalizedShortName(), nm = id.LocalizedName();
                bool snOk = !string.IsNullOrWhiteSpace(sn) && sn != pin + " ShortName";
                bool nmOk = !string.IsNullOrWhiteSpace(nm) && nm != pin + " Name";
                full = nmOk ? nm : (snOk ? sn : pin);
                shortName = snOk ? sn : full;
            }
            catch (Exception) { }
            // A non-Unassigned category is what makes UIA's view show the state line of an item-less entry.
            object category = Enum.ToObject(_categoryType, _pseudoCategory);
            return _entryCtor.Invoke(new object[]
            {
                Truncate(shortName, 18), full, "NONE CARRIED", string.Empty,
                false, false, false, true, false, null, null, category, false
            });
        }

        // ---- fallback classification ---------------------------------------------------------------

        // Interchangeable categories of an item as a bitmask over QuickUseCategory values, from its own
        // template (resource-independent). Stimulators and non-meds: 0.
        private static int ClassMask(Item item)
        {
            if (!(item is Meds meds) || item.Template is StimulatorTemplate) return 0;
            var fx = meds.HealthEffectsComponent?.DamageEffects;
            int mask = 0;
            foreach (var t in _treatments)
            {
                bool inCategory = t.Effect.HasValue ? fx != null && fx.ContainsKey(t.Effect.Value) : item.Template is MedKitTemplate;
                if (inCategory) mask |= 1 << t.Category;
            }
            return mask;
        }

        private static void SaveClasses()
        {
            var sb = new StringBuilder();
            foreach (var kv in _classes)
            {
                bool pinned = false;
                for (int i = 0; i < MaxPins && !pinned; i++) pinned = _pins[i] == kv.Key;
                if (!pinned) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
            }
            if (_classesEntry.Value != sb.ToString()) _classesEntry.Value = sb.ToString();
        }

        // Record the category set of a carried pinned item the first time it is seen.
        internal static void NoteSeen(int idx, Item item)
        {
            string tpl = _pins[idx];
            if (tpl == null || _classes.ContainsKey(tpl)) return;
            _classes[tpl] = ClassMask(item);
            SaveClasses();
        }

        internal static int FallbackMask(int idx)
        {
            if (FallbackEntry.Value != CategoryFallback.Auto) return 0;
            return _classes.TryGetValue(_pins[idx] ?? string.Empty, out int m) ? m : 0;
        }

        internal static int CategoryCount(int mask)
        {
            int n = 0;
            foreach (var t in _treatments)
                if ((mask & (1 << t.Category)) != 0) n++;
            return n;
        }

        // Number of the pinned item's categories (mask) that UIA's own (resource-aware) classifier matches for
        // the candidate; 0 = no fallback. Stimulators never.
        internal static int FallbackScore(Item candidate, int mask)
        {
            if (candidate.Template is StimulatorTemplate) return 0;
            int n = 0;
            foreach (var t in _treatments)
                if ((mask & (1 << t.Category)) != 0 && _matches(candidate, t.Category)) n++;
            return n;
        }

        // ---- keys ----------------------------------------------------------------------------------

        private enum KeyAct { None, Pin, Clear }

        // Main key just went down and the shortcut's own modifiers are held; other held keys are ignored
        // (same rule as the wheel key), so it works while moving or sprinting.
        private static bool Pressed(KeyCode main, KeyCode[] mods)
        {
            if (main == KeyCode.None || !Input.GetKeyDown(main)) return false;
            for (int i = 0; i < mods.Length; i++)
                if (!Input.GetKey(mods[i])) return false;
            return true;
        }

        // Both can match in one frame (Mouse2 and Ctrl+Mouse2): the one with more modifiers wins, ties go to Clear.
        private static KeyAct Poll()
        {
            if (PinKey == null || ClearKey == null) return KeyAct.None;
            bool p = Pressed(_pinMain, _pinMods), c = Pressed(_clearMain, _clearMods);
            if (p && c) return _pinMods.Length > _clearMods.Length ? KeyAct.Pin : KeyAct.Clear;
            return c ? KeyAct.Clear : p ? KeyAct.Pin : KeyAct.None;
        }

        // The wheel is open in Items mode (not the weapon-device wheel).
        private static bool InItemsMode(object ctrl) => _gIsOpen(ctrl) && _gMode(ctrl) == 0;

        // Selected main-wheel entry's item before UIA refreshes this frame; compared in the postfix to
        // raise UIA's _categorySelectionChangedThisUpdate guard (UIA only applies it to entries with a
        // category, which pinned item entries lack). Weak so a missed postfix cannot keep an item alive.
        private static int _prePage = -1, _preAbs = -1;
        private static readonly WeakReference _preItem = new WeakReference(null);

        private static void ClearPre()
        {
            _prePage = _preAbs = -1;
            _preItem.Target = null;
        }

        private static bool UpdateOpenPrefix(object __instance)
        {
            ClearPre();
            _bindClick = false;
            if (!_applier.CanTrack || !InItemsMode(__instance) || !_gHasCat(__instance)) return true;

            if (Selecting)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
                {
                    Sound(__instance, "MenuEscape");
                    ExitSelection(__instance);
                    return false; // UIA would close the wheel
                }
                if (Input.GetMouseButtonDown(0))
                {
                    _bindClick = true;
                    // A wheel opened from a pending-use request would use the clicked item; hide that for this call.
                    if (_gPending(__instance)) { _sPending(__instance, false); _restorePending = true; }
                }
                return true;
            }

            int sel = _gSel(__instance);
            if (sel < 0 || sel >= _gPageCount(__instance)) return true;
            _prePage = _gPage(__instance);
            _preAbs = _gPageStart(__instance) + sel;
            _preItem.Target = _applier.EntryItem(__instance, _preAbs);
            return true;
        }

        private static void RaiseChangedGuard(object ctrl) => _sCatChanged(ctrl, true);

        // Runs after UIA handled the open wheel's input for this frame (selection already updated).
        private static void UpdateOpenPostfix(object __instance)
        {
            bool open = _gIsOpen(__instance);
            if (_restorePending)
            {
                _restorePending = false;
                if (open) _sPending(__instance, true);
            }
            if (_preAbs >= 0 && open && !Selecting && _gPage(__instance) == _prePage
                && !ReferenceEquals(_applier.EntryItem(__instance, _preAbs), _preItem.Target))
                RaiseChangedGuard(__instance);
            ClearPre();
            TrackHover(__instance);

            if (!open || !InItemsMode(__instance)) { _bindClick = false; return; }
            try
            {
                if (_bindClick && Selecting)
                {
                    _bindClick = false;
                    BindHovered(__instance);
                    return;
                }
                _bindClick = false;
                var act = Poll();
                if (act != KeyAct.None) HandleKey(__instance, act);
            }
            catch (Exception e) { LogOnce("pin key", e); }
        }

        private static void HandleKey(object ctrl, KeyAct act)
        {
            if (Selecting)
            {
                if (act == KeyAct.Pin) BindHovered(ctrl);
                return;
            }
            if (_hover < 0 || _hover >= _snapPer)
            {
                Sound(ctrl, "MenuEscape");
                return;
            }
            int idx = Idx(_gPage(ctrl), _hover);
            if (act == KeyAct.Clear)
            {
                if (!HasPin(idx)) { Sound(ctrl, "MenuEscape"); return; }
                PinItem[idx].Value = string.Empty;
                Sound(ctrl, "MenuEscape");
                RaiseChangedGuard(ctrl);
                Repopulate(ctrl);
                return;
            }
            // Pin Key on UIA's queued "next" item: UIA's remove-next action (what its favorite button does there).
            if (_applier.SelectedKind(ctrl) == EntryKind.NextQueued)
            {
                _allowFavorite = true;
                try { _mToggleFav.Invoke(ctrl, null); }
                finally { _allowFavorite = false; }
                return;
            }
            // Pin Key on a main slot: show UIA's item pages to choose from.
            Selecting = true;
            _selectIdx = idx;
            _mainPage = _gPage(ctrl);
            _sPage(ctrl, 0);
            Sound(ctrl, "MenuDropdownSelect");
            RaiseChangedGuard(ctrl);
            Repopulate(ctrl);
        }

        private static void BindHovered(object ctrl)
        {
            int sel = _gSel(ctrl);
            var item = sel < 0 || sel >= _gPageCount(ctrl) ? null : _applier.EntryItem(ctrl, _gPageStart(ctrl) + sel);
            if (item == null)
            {
                Sound(ctrl, "MenuEscape");
                return;
            }
            int idx = _selectIdx;
            if (idx < 0 || idx >= MaxPins) { ExitSelection(ctrl); return; }
            string tpl = TemplateOf(item);
            PinItem[idx].Value = tpl;
            _classes[tpl] = ClassMask(item);
            SaveClasses();
            Plugin.Log.LogInfo("Pinned " + tpl + " to page " + (idx / MaxPerPage + 1) + " slot " + (idx % MaxPerPage + 1));
            Sound(ctrl, "MenuCheckBox");
            ExitSelection(ctrl);
        }

        // Back to the main wheel, same main page, wheel stays open.
        private static void ExitSelection(object ctrl)
        {
            Selecting = false;
            _selectIdx = -1;
            _sPage(ctrl, _mainPage);
            RaiseChangedGuard(ctrl);
            Repopulate(ctrl);
        }

        internal static void OnOpen()
        {
            Selecting = false;
            _selectIdx = -1;
            _mainPage = 0;
            _hover = -1;
            _bindClick = _restorePending = false;
            ClearPre();
        }

        // Releasing the wheel key in selection mode must not use the hovered item (UIA's Close(true)).
        private static void ClosePrefix(ref bool useSelection)
        {
            if (!Selecting) return;
            useSelection = false;
            Selecting = false;
            _selectIdx = -1;
        }

        // UIA toggles favorite on middle mouse inside UpdateOpenWheel, before our postfix. When the Pin or
        // Clear key fires this frame in the items wheel, skip it; HandleKey re-invokes it (with _allowFavorite)
        // for the one case UIA's method is wanted: the Pin Key on the queued next item (remove next).
        private static bool FavoritePrefix(object __instance) => _allowFavorite || !(InItemsMode(__instance) && Poll() != KeyAct.None);

        private static void Sound(object ctrl, string type) =>
            _mPlaySound.Invoke(ctrl, new object[] { (EUISoundType)Enum.Parse(typeof(EUISoundType), type) });

        private static void Repopulate(object ctrl)
        {
            if (!(bool)_mPopulate.Invoke(ctrl, null))
                _mClose.Invoke(ctrl, new object[] { false });
            else
                _mRefresh.Invoke(ctrl, null);
        }
    }

    internal interface IPinApplier
    {
        bool CanTrack { get; }
        void Apply(object ctrl);
        Item EntryItem(object ctrl, int index);
        EntryKind SelectedKind(object ctrl);
    }

    internal enum EntryKind { None, Blank, NoneCarried, NextQueued }

    // "Pinned item" label for the NONE CARRIED pseudo category (typed: no per-call args array / boxing).
    internal static class DisplayNamePatch<TCat> where TCat : struct
    {
        private static readonly TCat Pseudo = (TCat)Enum.ToObject(typeof(TCat), Slots.PseudoCategory);

        public static void Postfix(TCat category, ref string __result)
        {
            if (EqualityComparer<TCat>.Default.Equals(category, Pseudo)) __result = "Pinned item";
        }
    }

    // UIA's view only highlights the hovered slot when it is a usable item (blank entries are always drawn
    // normal, unavailable ones red). Re-colours the hovered main slot after UIA's UpdatePresentation: blank and
    // unavailable (NONE CARRIED, queued-less busy) entries get the same selected look as usable ones.
    internal static class HighlightPatch<TEntry> where TEntry : struct
    {
        private static readonly Func<TEntry, bool> IsBlank = Fast.StructGetter<TEntry, bool>(AccessTools.PropertyGetter(typeof(TEntry), "IsBlank"));
        private static readonly Func<TEntry, bool> IsUsable = Fast.StructGetter<TEntry, bool>(AccessTools.PropertyGetter(typeof(TEntry), "IsUsable"));
        private static readonly Func<TEntry, bool> IsQueued = Fast.StructGetter<TEntry, bool>(AccessTools.PropertyGetter(typeof(TEntry), "IsQueued"));
        private static readonly FieldInfo ViewsField = AccessTools.Field(AccessTools.TypeByName("UseItemsAnywhere.QuickUseWheel.QuickUseWheelView"), "_views");
        private static readonly Func<object, System.Collections.IList> Views = Fast.Getter<System.Collections.IList>(ViewsField);
        private static Func<object, UnityEngine.UI.Graphic> _segment, _frame;
        private static Func<object, TMPro.TMP_Text> _name;

        private static readonly Color Segment = new Color(0.22f, 0.25f, 0.26f, 0.96f);
        private static readonly Color SegmentUnavail = new Color(0.20f, 0.085f, 0.065f, 0.95f);
        private static readonly Color Frame = new Color(0.45f, 0.26f, 0.22f, 0.96f);
        private static readonly Color Name = new Color(0.62f, 0.63f, 0.6f, 1f);

        public static void Postfix(object __instance, IReadOnlyList<TEntry> items, int pageStartIndex, int pageItemCount, int selectedIndex)
        {
            try
            {
                if (selectedIndex < 0 || selectedIndex >= pageItemCount || pageStartIndex + selectedIndex >= items.Count) return;
                var e = items[pageStartIndex + selectedIndex];
                bool blank = IsBlank(e);
                if (!blank && (Slots.Selecting || IsUsable(e) || IsQueued(e))) return;
                var views = Views(__instance);
                if (selectedIndex >= views.Count) return;
                var v = views[selectedIndex];
                if (_segment == null)
                {
                    var vt = v.GetType();
                    _segment = Fast.PropGetter<UnityEngine.UI.Graphic>(AccessTools.Property(vt, "Segment"));
                    _frame = Fast.PropGetter<UnityEngine.UI.Graphic>(AccessTools.Property(vt, "IconFrame"));
                    _name = Fast.PropGetter<TMPro.TMP_Text>(AccessTools.Property(vt, "Name"));
                }
                _segment(v).color = blank ? Segment : SegmentUnavail;
                if (blank) return;
                _frame(v).color = Frame;
                _name(v).color = Name;
            }
            catch (Exception ex) { Slots.LogOnce("highlight", ex); }
        }
    }

    // Replaces the wheel header / control hints of the Items wheel (QuickUseWheelViewState is an internal struct).
    internal static class ViewStatePatch<TVS> where TVS : struct
    {
        private static readonly ConstructorInfo Ctor = typeof(TVS).GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)[0];
        private static readonly Func<TVS, string> EmptyTitle = Get("EmptyTitle"), EmptyHint = Get("EmptyHint"), Status = Get("Status");

        private static Func<TVS, string> Get(string name) => Fast.StructGetter<TVS, string>(AccessTools.PropertyGetter(typeof(TVS), name));

        public static void Postfix(object __instance, ref TVS __result)
        {
            try
            {
                if (!Slots.ViewActive(__instance)) return;
                var r = __result;
                __result = (TVS)Ctor.Invoke(new object[] { Slots.HeaderText(), EmptyTitle(r), EmptyHint(r), Slots.ControlsText(Slots.PendingOpen(__instance)), Status(r) });
            }
            catch (Exception e) { Slots.LogOnce("view state", e); }
        }
    }

    // Typed (no boxing) access to UIA's QuickUseWheelEntry / QuickUseWheelItem structs.
    internal sealed class PinApplier<TEntry, TWheel> : IPinApplier where TEntry : struct where TWheel : struct
    {
        private readonly Func<object, List<TEntry>> _entries = Fast.Getter<List<TEntry>>(Slots._fEntries);
        private readonly Func<object, object> _inventory = Fast.Getter<object>(Slots._fInventory);
        private readonly Func<object, IReadOnlyList<TWheel>> _items = Fast.PropGetter<IReadOnlyList<TWheel>>(Slots._pInvItems);
        private readonly Func<object, Player> _player = Fast.Getter<Player>(Slots._fPlayer);
        private readonly Func<object, bool> _hasCat = Fast.Getter<bool>(Slots._fHasCat);
        private readonly Func<object, int> _mode = Fast.Getter<int>(Slots._fMode);
        private readonly Func<TEntry, TWheel?> _entryItem = Fast.StructGetter<TEntry, TWheel?>(Slots._pEntryItem.GetGetMethod(true));
        private readonly Func<TWheel, Item> _wheelItem = Fast.StructGetter<TWheel, Item>(Slots._pWheelItem.GetGetMethod(true));
        private readonly Func<TWheel, bool> _usable = Fast.StructGetter<TWheel, bool>(Slots._pWheelUsable.GetGetMethod(true));
        private readonly Action<object, TWheel, int> _add = Fast.AddItemEntry<TWheel>(Slots._mAddItemEntry);
        private readonly Func<Item, bool> _hasResource = (Func<Item, bool>)Delegate.CreateDelegate(typeof(Func<Item, bool>), Slots._mHasResource);
        private readonly Func<object, int> _sel = Fast.Getter<int>(Slots._fSelField), _pageCount = Fast.Getter<int>(Slots._fPageCountField), _pageStart = Fast.Getter<int>(Slots._fPageStartField);
        private readonly Func<TEntry, bool> _isBlank = Fast.StructGetter<TEntry, bool>(AccessTools.PropertyGetter(typeof(TEntry), "IsBlank"));
        private readonly Func<TEntry, int> _category = Fast.StructGetter<TEntry, int>(AccessTools.PropertyGetter(typeof(TEntry), "Category"));
        private readonly Func<TWheel, bool> _nextQueued = Fast.StructGetter<TWheel, bool>(AccessTools.PropertyGetter(typeof(TWheel), "IsNextQueued"));
        private readonly List<TEntry> _main = new List<TEntry>();

        public bool CanTrack => true;

        public Item EntryItem(object ctrl, int index)
        {
            var list = _entries(ctrl);
            if (index < 0 || index >= list.Count) return null;
            var wi = _entryItem(list[index]);
            return wi.HasValue ? _wheelItem(wi.Value) : null;
        }

        public EntryKind SelectedKind(object ctrl)
        {
            int sel = _sel(ctrl);
            if (sel < 0 || sel >= _pageCount(ctrl)) return EntryKind.None;
            var list = _entries(ctrl);
            int abs = _pageStart(ctrl) + sel;
            if (abs >= list.Count) return EntryKind.None;
            var e = list[abs];
            if (_isBlank(e)) return EntryKind.Blank;
            if (_category(e) == Slots.PseudoCategory) return EntryKind.NoneCarried;
            var wi = _entryItem(e);
            return wi.HasValue && _nextQueued(wi.Value) ? EntryKind.NextQueued : EntryKind.None;
        }

        // UIA just built [8 category entries][item entries]; swap the 8 for the main entries.
        public void Apply(object ctrl)
        {
            if (!_hasCat(ctrl) || _mode(ctrl) != 0) return;
            var entries = _entries(ctrl);
            if (entries.Count < Slots.UiaSlots) return;

            // Build the whole main list first (Added appends to and takes back off the list's end); only then
            // swap it in, so a failure leaves UIA's entry list intact.
            int pages = Slots.MainPages(), per = Slots.SlotsPerPage();
            int origCount = entries.Count;
            var player = _player(ctrl);
            var inv = _inventory(ctrl);
            var items = inv == null ? null : _items(inv);
            var main = _main;
            main.Clear();
            try
            {
                for (int p = 0; p < pages; p++)
                    for (int s = 0; s < per; s++)
                    {
                        int idx = Slots.Idx(p, s);
                        if (!Slots.HasPin(idx)) { main.Add((TEntry)Slots.Blank()); continue; }

                        int best = player == null || items == null ? -1 : Find(player, items, idx);
                        if (best >= 0)
                        {
                            Slots.NoteSeen(idx, _wheelItem(items[best]));
                            main.Add(Added(ctrl, items[best], entries));
                            continue;
                        }

                        int mask = Slots.FallbackMask(idx);
                        int fb = mask == 0 || items == null ? -1 : FindFallback(items, mask);
                        main.Add(fb >= 0 ? Added(ctrl, items[fb], entries) : (TEntry)Slots.NoneCarried(idx));
                    }
            }
            catch (Exception)
            {
                main.Clear();
                while (entries.Count > origCount) entries.RemoveAt(entries.Count - 1);
                throw;
            }
            entries.RemoveRange(0, Slots.UiaSlots);
            entries.InsertRange(0, main);
            main.Clear();
            Slots.SetSnapshot(pages, per);
        }

        // AddItemEntry appends an ordinary item entry (label = item name); take it back off the list.
        private TEntry Added(object ctrl, TWheel item, List<TEntry> entries)
        {
            _add(ctrl, item, 0);
            var e = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            return e;
        }

        // Wheel item for the pinned template. UIA's list holds only examined candidates from its
        // Meds/FoodDrink/Grenade/... sources. Preference: usable first, then carried-with-resource but not
        // usable right now (queued, ACCESSING, UNAVAILABLE: shown with UIA's own state, the selection
        // just cannot be committed). Carried-only-empty (no resource) or not carried at all is "missing"
        // (-1). Duplicates (grouping off) within a tier use UIA's grouped-item selection rule.
        private int Find(Player player, IReadOnlyList<TWheel> items, int idx)
        {
            if (!Slots.PinValid(idx)) return -1;
            var id = Slots.PinId(idx);
            int best = -1, bestTier = 0;
            Item bestItem = null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = _wheelItem(items[i]);
                if (item == null || !item.TemplateId.Equals(id)) continue;
                int tier = _usable(items[i]) ? 0 : (_hasResource(item) ? 1 : 2);
                if (tier == 2) continue;
                if (best < 0 || tier < bestTier || (tier == bestTier && Slots._comparePreferred(player, item, bestItem) < 0))
                {
                    best = i;
                    bestTier = tier;
                    bestItem = item;
                }
            }
            return best;
        }

        // Usable wheel item sharing at least one category with the pinned item; the one matching the most of
        // them wins, ties go to the earlier entry (UIA's list is already sorted favorites / fastest first).
        private int FindFallback(IReadOnlyList<TWheel> items, int mask)
        {
            int full = Slots.CategoryCount(mask), best = -1, bestScore = 0;
            for (int i = 0; i < items.Count; i++)
            {
                if (!_usable(items[i])) continue;
                var item = _wheelItem(items[i]);
                if (item == null) continue;
                int score = Slots.FallbackScore(item, mask);
                if (score > bestScore)
                {
                    best = i;
                    bestScore = score;
                    if (score >= full) break;
                }
            }
            return best;
        }
    }

    // Compiled accessors for UIA's internal members (skip visibility checks, no boxing / MethodBase.Invoke).
    internal static class Fast
    {
        private static DynamicMethod Dm(string name, Type ret, params Type[] args) =>
            new DynamicMethod("UIAExtras_" + name, ret, args, typeof(Fast).Module, true);

        internal static Func<object, T> Getter<T>(FieldInfo f)
        {
            var dm = Dm("get_" + f.Name, typeof(T), typeof(object));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, f.DeclaringType);
            il.Emit(OpCodes.Ldfld, f);
            il.Emit(OpCodes.Ret);
            return (Func<object, T>)dm.CreateDelegate(typeof(Func<object, T>));
        }

        internal static Action<object, T> Setter<T>(FieldInfo f)
        {
            var dm = Dm("set_" + f.Name, typeof(void), typeof(object), typeof(T));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, f.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Stfld, f);
            il.Emit(OpCodes.Ret);
            return (Action<object, T>)dm.CreateDelegate(typeof(Action<object, T>));
        }

        // Instance property getter on a class.
        internal static Func<object, T> PropGetter<T>(PropertyInfo p)
        {
            var dm = Dm("get_" + p.Name, typeof(T), typeof(object));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, p.DeclaringType);
            il.Emit(OpCodes.Callvirt, p.GetGetMethod(true));
            il.Emit(OpCodes.Ret);
            return (Func<object, T>)dm.CreateDelegate(typeof(Func<object, T>));
        }

        // Instance getter on a struct, taking the struct by value.
        internal static Func<TS, TR> StructGetter<TS, TR>(MethodInfo getter) where TS : struct
        {
            var dm = Dm("sget_" + getter.Name, typeof(TR), typeof(TS));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarga_S, (byte)0);
            il.Emit(OpCodes.Call, getter);
            il.Emit(OpCodes.Ret);
            return (Func<TS, TR>)dm.CreateDelegate(typeof(Func<TS, TR>));
        }

        // QuickUseCategoryClassifier.Matches(item, (QuickUseCategory)category) with the enum passed as int.
        internal static Func<Item, int, bool> Matcher(MethodInfo m)
        {
            var dm = Dm("Matches", typeof(bool), typeof(Item), typeof(int));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Call, m);
            il.Emit(OpCodes.Ret);
            return (Func<Item, int, bool>)dm.CreateDelegate(typeof(Func<Item, int, bool>));
        }

        // controller.AddItemEntry(item, (QuickUseCategory)category)
        internal static Action<object, TW, int> AddItemEntry<TW>(MethodInfo m)
        {
            var dm = Dm("AddItemEntry", typeof(void), typeof(object), typeof(TW), typeof(int));
            var il = dm.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Castclass, m.DeclaringType);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Call, m);
            il.Emit(OpCodes.Ret);
            return (Action<object, TW, int>)dm.CreateDelegate(typeof(Action<object, TW, int>));
        }
    }
}

// Read by the F12 ConfigurationManager by member name; only the fields used here.
internal sealed class ConfigurationManagerAttributes
{
    public bool? Browsable;
}
