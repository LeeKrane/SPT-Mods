// Move the first-person camera closer to or farther from the sight while aiming, per weapon + sight pair.
//
// The main camera FOV never changes: a small-looking scope fills more of the screen when the
// eye sits closer. The offset (metres, negative = closer) is subtracted from the camera's z target after
// vanilla has computed it (CameraDistancePatch), and EFT's own LerpCamera smooths toward it. Adjusting uses modifier
// keys + mouse wheel while aiming (Update below); the offsets are stored in BepInEx/config.
using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Krane.AdsCameraDistance
{
    [BepInPlugin("krane.adscameradistance", "Krane's AdsCameraDistance", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static OffsetStore Store = new OffsetStore();

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _modifier1;
        private static ConfigEntry<KeyCode> _modifier2;
        private static ConfigEntry<float> _step;
        private static ConfigEntry<float> _min;
        private static ConfigEntry<float> _max;
        private static ConfigEntry<bool> _invert;
        private static ConfigEntry<bool> _lensFov;
        private static ConfigEntry<int> _opticResolution;
        private static ConfigEntry<float> _reticleGrowth;
        private static ConfigEntry<ReticleScaler.EdgeFill> _reticleFill;
        internal static ConfigEntry<bool> ShowLabel;

        // Plain statics for the per-frame hook (no ConfigEntry access there).
        internal static bool Enabled;
        internal static float Min = -0.03f;
        internal static float Max = 0f;
        // +1: the stored offset is subtracted from the camera z target (+z points toward the sight, so negative = closer). -1 flips it, see "Invert direction".
        internal static float ZSign = 1f;

        private static string _path;
        private static bool _active;
        private float _lastChange;
        private float _scrollRest;
        private readonly Fault _inputFault = new Fault("Input handling");
        private const float SaveDelay = 1f;

        private void Awake()
        {
            Log = Logger;
            _enabled = Config.Bind("General", "Enabled", true, "Master switch. Off: vanilla camera, no input handling.");
            _modifier1 = Config.Bind("Input", "Modifier 1", KeyCode.LeftControl, "First key to hold while scrolling.");
            _modifier2 = Config.Bind("Input", "Modifier 2", KeyCode.LeftShift, "Second key to hold while scrolling.");
            _step = Config.Bind("Adjust", "Step", 0.005f, new ConfigDescription("Metres per scroll notch.", new AcceptableValueRange<float>(0.001f, 0.05f)));
            _min = Config.Bind("Adjust", "Min", -0.03f, "Closest offset in metres (negative = closer to the sight). Very close can clip the camera into the weapon or scope. Stored offsets beyond it are applied clamped, not rewritten.");
            _max = Config.Bind("Adjust", "Max", 0f, "Farthest offset in metres (positive = farther from the sight).");
            _invert = Config.Bind("Adjust", "Invert direction", false, "Flip the offset sign if negative values move the camera away from the sight on your setup.");
            _lensFov = Config.Bind("Adjust", "Compensate lens FOV", true, "Widen the optic camera's view by the ratio the lens grew, so a closer eye does not leave black edges in the scope. The target then keeps its vanilla size while the lens and reticle get larger.");
            _lensFov.SettingChanged += (_, __) => Refresh();
            _opticResolution = Config.Bind("Adjust", "Optic resolution", 1536, new ConfigDescription("Size of the scope image texture while the drawn weapon has an offset on any of its sights; otherwise vanilla (1024). Switches only when drawing a weapon, so scoping in never stutters. The widened scope view spreads the image over more of the lens, so a higher size keeps it sharp; costs GPU time while aiming through a scope.", new AcceptableValueList<int>(1024, 1152, 1280, 1408, 1536, 1664, 1792, 1920, 2048)));
            _opticResolution.SettingChanged += (_, __) => Refresh();
            _reticleGrowth = Config.Bind("Adjust", "Reticle growth", 0f, new ConfigDescription("How much the reticle grows with the lens. 0 = vanilla on-screen size, 1 = with the lens, 0.5 = halfway.", new AcceptableValueRange<float>(0f, 1f)));
            _reticleGrowth.SettingChanged += (_, __) => Refresh();
            _reticleFill = Config.Bind("Adjust", "Reticle edge fill", ReticleScaler.EdgeFill.Extend, "Margin around a shrunk reticle. Extend: lines reaching the texture edge continue to the lens edge (edge rings or tints smear). Transparent: nothing smears, but such lines end before the lens edge.");
            _reticleFill.SettingChanged += (_, __) => Refresh();
            ShowLabel = Config.Bind("HUD", "Show label", true, "Show the offset (e.g. Eye dist -0.035) on each adjustment.");
            _enabled.SettingChanged += (_, __) => Refresh();
            _min.SettingChanged += (_, __) => Refresh();
            _max.SettingChanged += (_, __) => Refresh();
            _invert.SettingChanged += (_, __) => Refresh();
            Refresh();

            _path = Path.Combine(BepInEx.Paths.ConfigPath, "krane.AdsCameraDistance.json");
            string problem = Store.Load(_path);
            if (problem != null)
                Log.LogWarning($"{Path.GetFileName(_path)}: {problem}");

            var harmony = new Harmony("krane.adscameradistance");
            // Resolve first: without the private field the camera hook must not be installed at all.
            _active = CameraDistancePatch.Resolve()
                && Apply(harmony, typeof(LocalPwaPatch))
                && Apply(harmony, typeof(CameraDistancePatch));
            // Optional: without it a closer eye only shows black lens edges, the distance itself still works.
            if (_active)
                Apply(harmony, typeof(LensFovPatch));
            HudPatch.Active = Apply(harmony, typeof(HudPatch));
            if (!_active)
                Log.LogError("AdsCameraDistance disabled: a required game member is missing.");
        }

        private static void Refresh()
        {
            float min = _min.Value, max = _max.Value;
            OffsetMath.Normalize(ref min, ref max);
            Min = min;
            Max = max;
            ZSign = _invert.Value ? -1f : 1f;
            Enabled = _enabled.Value;
            LensFovPatch.Enabled = _lensFov.Value;
            LensFovPatch.Resolution = _opticResolution.Value;
            ReticleScaler.Growth = _reticleGrowth.Value;
            ReticleScaler.Fill = _reticleFill.Value;
        }

        private void Update()
        {
            if (_active && Enabled)
                HandleInput();
            if (Store.Dirty && Time.unscaledTime - _lastChange > SaveDelay)
                Save();
        }

        private void HandleInput()
        {
            float scroll = Input.mouseScrollDelta.y;
            bool reset = Input.GetMouseButtonDown(2);
            if (!Input.GetKey(_modifier1.Value) || !Input.GetKey(_modifier2.Value))
            {
                _scrollRest = 0f;
                return;
            }
            if (scroll == 0f && !reset)
                return;
            if (_inputFault.Blocked)
                return;

            try
            {
                // Nothing to adjust while the camera hook is backing off or the file could not be read.
                string key = CameraDistancePatch.Fault.Blocked || Store.ReadFailed ? null : CameraDistancePatch.CurrentKey();
                if (key == null)
                {
                    // A partial notch must not carry over to a later adjustment, possibly on another sight.
                    _scrollRest = 0f;
                    return;
                }

                float value;
                if (reset)
                {
                    value = 0f;
                    _scrollRest = 0f;
                }
                else
                {
                    // Accumulate, so smooth-scrolling devices reporting fractions of a notch still step.
                    _scrollRest += scroll;
                    int notches = (int)_scrollRest;
                    if (notches == 0)
                        return;
                    _scrollRest -= notches;
                    value = OffsetMath.Adjust(OffsetMath.Clamp(Store.Get(key), Min, Max), notches, _step.Value, Min, Max);
                }
                Store.Set(key, value);
                _lastChange = Time.unscaledTime;
                if (ShowLabel.Value)
                    HudPatch.Show(OffsetMath.FormatLabel(value));
            }
            catch (Exception e)
            {
                _inputFault.Hit(e);
                return;
            }
            _inputFault.Ok();
        }

        private void Save()
        {
            try
            {
                Store.Save(_path);
            }
            catch (Exception e)
            {
                Log.LogError($"Saving {_path} failed: {e.Message}");
                // Dirty stays set; retry in ~30 s so a read-only file does not spam the log every frame.
                _lastChange = Time.unscaledTime + 30f;
            }
        }

        private void OnApplicationQuit()
        {
            if (Store.Dirty)
                Save();
        }

        private void OnDestroy()
        {
            if (Store.Dirty)
                Save();
        }

        // A missing game member makes only that patch class fail; the rest of the game stays vanilla.
        internal static bool Apply(Harmony harmony, Type patchClass)
        {
            try
            {
                harmony.PatchAll(patchClass);
                return true;
            }
            catch (Exception e)
            {
                Log.LogError($"{patchClass.Name} not applied: {e}");
                return false;
            }
        }
    }
}
