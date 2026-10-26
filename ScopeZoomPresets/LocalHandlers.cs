using System;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;

namespace Krane.ScopeZoomPresets
{
    // The ScopeZoomHandlers of the local player's weapon in hands. Bots and observed players run the same
    // handler code; every handler-level patch exits at once for any handler not listed here. Decided once
    // per UpdateModsVisualControllers (weapon draw, mod added or removed), never per frame. The registry
    // belongs to one Firearms: it is dropped when that Firearms returns to the pool (weapon switch, drop,
    // raid end) and when the local player dies (the weapon stays in hands on the corpse, and its handlers
    // must not keep the gates open), after which a pooled scope may be reused by a bot.
    internal static class LocalHandlers
    {
        // A registered (handler, sight) pair. A pooled handler reused by a bot gets the bot's sight in Init,
        // so it no longer matches its pair and is treated as not local.
        internal sealed class Entry
        {
            public readonly ScopeZoomHandler Handler;
            public readonly SightComponent Sight;
            // The handler's FieldOfViewCurve as a delegate, built once here (CameraData is fixed per handler,
            // ScopeZoomHandler.cs:156). Null when missing.
            public readonly Func<float, float> Curve;
            // False when the curve is not a usable 0..1 monotonic map: float_4 stays vanilla, the toggle too.
            public readonly bool CurveValid;
            // Last zoom (in tenths) the HUD handled. -1: none yet.
            public int Tenths = -1;

            public Entry(ScopeZoomHandler handler, SightComponent sight)
            {
                Handler = handler;
                Sight = sight;
                UnityEngine.AnimationCurve curve = handler.CameraData?.FieldOfViewCurve;
                if (curve != null)
                    Curve = curve.Evaluate;
                CurveValid = Curve != null && ZoomMath.IsCurveValid(Curve);
            }
        }

        private static readonly Entry[] None = new Entry[0];
        // Published with one reference write.
        private static Entry[] _entries = None;
        private static Firearms _owner;
        // The owner's ProceduralWeaponAnimation (the local player's, ObjectInHands.cs:36), so the HUD can ask
        // which sight is current without the GameWorld/MainPlayer chain. Dropped with the registry.
        private static EFT.Animations.ProceduralWeaponAnimation _animation;

        // The GameWorld the registry (and LocalDead) belongs to: Singleton<GameWorld>.Instance at Set, the dying
        // player's GameWorld at death, the started world at raid start; all event paths. A late Dispose of
        // another world (the previous raid's) must not wipe the current raid's registry. Survives Clear, since
        // LocalDead outlives the registry. Null: unknown, any Dispose resets.
        private static GameWorld _world;

        public static void SetWorld(GameWorld world)
        {
            _world = world;
        }

        // GameWorld.Dispose: reset only when it is the registry's own world (or the owner is unknown).
        public static void DisposeWorld(GameWorld world)
        {
            if (_world != null && (object)_world != world)
                return;
            _world = null;
            LocalDead = false;
            Clear();
        }

        // Set when the patch class could not be applied completely (no way to drop the registry): stay vanilla.
        public static bool Disabled;

        // Reference compares over one to three entries: no hash, no table, no allocation. Null: not local.
        // The sight is the handler's current _sightComponent (injected by the caller's patch).
        public static Entry Find(ScopeZoomHandler handler, SightComponent sight)
        {
            Entry[] entries = _entries;
            for (int i = 0; i < entries.Length; i++)
            {
                Entry entry = entries[i];
                if ((object)entry.Handler == handler)
                    return (object)entry.Sight == sight ? entry : null;
            }
            return null;
        }

        // True when a registered handler still drives this sight, i.e. the label for it can be drawn.
        public static bool HasSight(SightComponent sight)
        {
            Entry[] entries = _entries;
            for (int i = 0; i < entries.Length; i++)
            {
                if ((object)entries[i].Sight == sight && (object)CurveSync.HandlerSight(entries[i].Handler) == sight)
                    return true;
            }
            return false;
        }

        // True when this is the sight currently selected on the local player's weapon (aimed or not).
        // ProceduralWeaponAnimation.CurrentAimingMod is one property (EFT.Animations/ProceduralWeaponAnimation.cs:533).
        public static bool IsCurrent(SightComponent sight)
        {
            EFT.Animations.ProceduralWeaponAnimation animation = _animation;
            return (object)animation != null && sight != null && animation.CurrentAimingMod == sight;
        }

        public static void Clear()
        {
            _owner = null;
            _animation = null;
            _entries = None;
        }

        // Only if the returning Firearms is the one the registry belongs to (a new weapon may already be set).
        public static void ClearIfOwner(Firearms firearms)
        {
            if ((object)firearms == _owner)
                Clear();
        }

        // A controller's handler is registered only while it drives the controller's own, adjustable sight.
        private static bool TryGetLocal(SightModVisualControllers controller, out ScopeZoomHandler handler)
        {
            handler = null;
            if (controller == null || !controller.TryGetZoomHandler(out handler))
                return false;
            SightComponent sight = controller.SightMod;
            return (object)CurveSync.HandlerSight(handler) == sight && ScopeSight.IsAdjustable(sight);
        }

        // Reused by Set (UI thread only); cleared after each use so no game object stays referenced.
        private static ScopeZoomHandler[] _bufHandlers = new ScopeZoomHandler[4];
        private static SightComponent[] _bufSights = new SightComponent[4];

        // Set when the local player died: the weapon stays in hands on the corpse and a later
        // UpdateModsVisualControllers (e.g. a mod change on it) must not register handlers again. Cleared at
        // raid start and raid end.
        public static bool LocalDead;

        // Rebuilds the registry from a local Firearms, then corrects float_4 on its handlers. This is the
        // one place a handler's sight and curve are validated (adjustable, supported range, usable curve);
        // CurveSync, the toggle and the HUD rely on it through the pair gate. Every UpdateModsVisualControllers
        // re-Inits the handlers (Init resets float_4 = _filedOfView, ScopeZoomHandler.cs:165-179,
        // Firearms.cs:897), so the correction runs each time; the registry itself is only rebuilt when the pairs
        // changed. It is published first: one failing correction must not leave the handlers unregistered.
        public static void Set(Firearms firearms)
        {
            if (Disabled || LocalDead)
                return;
            _world = Singleton<GameWorld>.Instance;
            var controllers = firearms._sightModVisualControllers;
            if (_bufHandlers.Length < controllers.Length)
            {
                _bufHandlers = new ScopeZoomHandler[controllers.Length];
                _bufSights = new SightComponent[controllers.Length];
            }

            try
            {
                // One pass: collect the local pairs in order. A pair that throws is skipped, not the rest.
                int count = 0;
                for (int i = 0; i < controllers.Length; i++)
                {
                    try
                    {
                        if (!TryGetLocal(controllers[i], out ScopeZoomHandler handler))
                            continue;
                        _bufHandlers[count] = handler;
                        _bufSights[count] = controllers[i].SightMod;
                        count++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError(e);
                    }
                }

                Entry[] current = _entries;
                bool same = (object)_owner == firearms && count == current.Length;
                for (int i = 0; same && i < count; i++)
                    same = (object)current[i].Handler == _bufHandlers[i] && (object)current[i].Sight == _bufSights[i];

                if (!same)
                {
                    Entry[] built = new Entry[count];
                    int n = 0;
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            built[n] = new Entry(_bufHandlers[i], _bufSights[i]);
                            n++;
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogError(e);
                        }
                    }
                    if (n != count)
                        Array.Resize(ref built, n);
                    _owner = firearms;
                    _animation = firearms.ProceduralWeaponAnimation;
                    _entries = n == 0 ? None : built;
                    current = _entries;
                }

                for (int i = 0; i < current.Length; i++)
                {
                    try
                    {
                        CurveSync.Correct(current[i]);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError(e);
                    }
                }
            }
            finally
            {
                // No game object stays referenced, whatever happened above.
                Array.Clear(_bufHandlers, 0, _bufHandlers.Length);
                Array.Clear(_bufSights, 0, _bufSights.Length);
            }
        }
    }

    // UpdateModsVisualControllers runs from AfterGetFromPoolInit (weapon draw, with _isYourPlayer already
    // set by ObjectInHands.AfterGetFromPoolInit from Player.IsYourPlayer, independent of MainPlayer) and
    // after every mod add or remove. It ends with handler.Init for each handler, so the postfix sees the
    // handlers after Init and replaces a separate Init patch. Firearms.OnReturnToPool is invoked by
    // AssetPoolObject.ReturnToPool for every pooled component. Both patches live in this class, and the
    // plugin disables the registry if they did not all apply. The death patch lives here too, so its failure
    // disables the registry.
    internal static class LocalHandlersPatch
    {
        [HarmonyPatch(typeof(Firearms), nameof(Firearms.UpdateModsVisualControllers))]
        [HarmonyPostfix]
        private static void SetPostfix(Firearms __instance)
        {
            try
            {
                if (__instance._isYourPlayer)
                    LocalHandlers.Set(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError(e);
            }
        }

        // The local player's death: the corpse keeps the weapon in hands, so ReturnPostfix would not fire for
        // it. Player.OnDead(EDamageType) is virtual (EFT/Player.cs:30152); LocalPlayer and ClientPlayer overrides
        // call base.OnDead (EFT/LocalPlayer.cs:198-207, EFT/ClientPlayer.cs:3557-3560). A prefix runs before the
        // vanilla body, so the flag is set even if that body throws. Fika's observed players have IsYourPlayer
        // false (EFT/Player.cs:25009).
        [HarmonyPatch(typeof(Player), nameof(Player.OnDead))]
        [HarmonyPrefix]
        private static void DeadPrefix(Player __instance)
        {
            if (__instance.IsYourPlayer)
            {
                LocalHandlers.SetWorld(__instance.GameWorld);
                LocalHandlers.LocalDead = true;
                LocalHandlers.Clear();
            }
        }

        // Raid end: do not hold the Player graph (Firearms) in a static across raids. ClientGameWorld.Dispose
        // calls base.Dispose (EFT/ClientGameWorld.cs:282) and ClientLocalGameWorld does not override it. Only the
        // world the registry belongs to resets it (Player.GameWorld, EFT/Player.cs:24513; Singleton<GameWorld>
        // is the same instance Dispose runs on).
        [HarmonyPatch(typeof(GameWorld), nameof(GameWorld.Dispose))]
        [HarmonyPostfix]
        private static void DisposePostfix(GameWorld __instance)
        {
            LocalHandlers.DisposeWorld(__instance);
        }

        // Rare event (weapon switch, drop, death, raid end), one reference compare for bots' weapons.
        [HarmonyPatch(typeof(Firearms), nameof(Firearms.OnReturnToPool))]
        [HarmonyPostfix]
        private static void ReturnPostfix(Firearms __instance)
        {
            LocalHandlers.ClearIfOwner(__instance);
        }
    }
}
