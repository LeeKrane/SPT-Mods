// Two independent, individually switchable helpers for slow raid/menu loads:
//
// Timing: BepInEx's LogOutput.log has no timestamps, so load phases can't be attributed.
// This logs "HH:mm:ss.fff [LoadSpeedFix] <phase> start/end Xms" around the heavy steps.
//
// Throttle lifter: while the loading-screen phases run (map scene load, Fika raid pool loads), the
// background loading thread runs at low priority and the frame rate limiter caps the loop. Raise
// them for the duration and restore afterwards (ref-counted, 10 min watchdog per hold). The
// bundle-retain batch size is raised only inside the same holds. Which effects are active is fixed
// at Awake (config changes need a game restart).
//
// Everything is patched by name through reflection: a method that does not exist in this game
// version logs a warning and is skipped instead of breaking the chainloader.
// Only non-generic methods on non-generic types are patched. Under Unity Mono, patching a method of
// a generic type or a generic method hits the code shared by all reference-type instantiations
// (patching HTTPTransportManager.ExtractResponseResult<object> broke every backend response
// parse and hung the boot); Hook refuses such targets.
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace Krane.LoadSpeedFix
{
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.SoftDependency)]
    [BepInPlugin("krane.loadspeedfix", "Krane's LoadSpeedFix", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> Timing, Throttle, ItemCache, MenuLoad, LeanGc, FastShaders;
        internal static ConfigEntry<int> RetainBatch;

        // Decided once at Awake: the hooks installed match these, so live config edits cannot desync them.
        internal static bool ThrottleOn { get; private set; }
        internal static bool RetainOn { get; private set; }
        internal static bool MenuOn { get; private set; }
        internal static bool LeanGcOn { get; private set; }
        internal static bool FastShadersOn { get; private set; }
        internal static FieldInfo RetainField { get; private set; }

        private static int _mainThread;
        internal static bool IsMain => Thread.CurrentThread.ManagedThreadId == _mainThread;
        private static readonly ConcurrentQueue<Action> _mainQueue = new ConcurrentQueue<Action>();

        private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        private void Awake()
        {
            Log = Logger;
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            Timing = Config.Bind("General", "TimingLog", true, "Log timestamped durations of load phases (needs a game restart)");
            Throttle = Config.Bind("General", "LoadThrottleLifter", true,
                "Raise the loading thread priority and uncap FPS while the map scene and raid pools load (needs a game restart)");
            ItemCache = Config.Bind("General", "ItemTemplateCache", true,
                "Keep the parsed /client/items templates in memory and skip the fetch+parse on later loads, post-raid (needs a game restart)");
            RetainBatch = Config.Bind("General", "RetainBatch", 2000,
                "Bundle-graph visits per frame while retaining bundles during loading holds (needs a game restart). 0 = leave the game default (55)");
            MenuLoad = Config.Bind("General", "MenuLoadLifter", true,
                "Also lift the throttle while the menu data job (first menu and every post-raid return) runs (needs a game restart)");
            LeanGc = Config.Bind("General", "LeanGC", true,
                "Turn off the game's AggressiveGC for its memory collects, except the one right before a raid: skips the finalizer wait, the compacting second collect and the 2nd full pass (needs a game restart)");
            FastShaders = Config.Bind("General", "FastShaderLookup", true,
                "Fill ShadersFinder's shader dictionary in one frame instead of one Shader.Find per frame (needs a game restart)");
            ThrottleOn = Throttle.Value;
            MenuOn = MenuLoad.Value;
            LeanGcOn = LeanGc.Value;
            FastShadersOn = FastShaders.Value;
            try
            {
                using (var proc = Process.GetCurrentProcess())
                    Log.LogInfo("plugin Awake +" + (DateTime.Now - proc.StartTime).TotalSeconds.ToString("F1") + "s since process start");
            }
            catch (Exception e) { Log.LogInfo("startup time unavailable: " + e.Message); }
            RetainOn = RetainBatch.Value > 0;
            if (RetainOn) RetainField = FindRetainField();
            if (RetainField == null) RetainOn = false;
            var h = new Harmony("krane.loadspeedfix");

            if (ItemCache.Value)
                // Non-generic static class; the GetTemplates targets live on ClientBackend<T> (shared code).
                Hook(h, "EFT.ItemFactoryCreateOperation", "CreateItemFactory",
                    m => m.GetParameters()[0].ParameterType.Name.StartsWith("IClientBackEnd"),
                    nameof(Hooks.Items_Pre), nameof(Hooks.Items_Post));

            if (Timing.Value)
            {
                Hook(h, "EFT.ClientApplicationInitOperation", "WarmupBundleShaders", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                foreach (var c in new[] { "ClientGameController", "HostGameController" })
                    foreach (var n in new[] { "WaitForHostToStart", "WaitForOtherPlayersToLoad", "InitializeLoot" })
                        Hook(h, "Fika.Core.Main.GameMode." + c, n, null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "Fika.Core.Main.GameMode.ClientGameController", "WaitForHostToLoad", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "Fika.Core.Main.GameMode.CoopGame", "InitPlayer", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));

                // Mid-raid bot/corpse/airdrop loads go through here too; Pools_Post logs only slow ones.
                Hook(h, "EFT.ObjectsFactory", "LoadBundlesAndCreatePools",
                    m => m.GetParameters()[0].ParameterType.Name == "Pools", nameof(Hooks.Pools_Pre), nameof(Hooks.Pools_Post));

                // Post-raid: OnGameEnd is async void (start marker only), the rest is awaited or sync. ComebackToMainMenu: see below.
                Hook(h, "EFT.EftClientBackendSession", "LocalRaidEnded", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "EFT.TarkovApplication", "OnGameEnd", null, nameof(Hooks.Mark_Pre), null);
                Hook(h, "EFT.TarkovApplication", "TryUnloadGame", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Sync_Post));

                // Where the first-menu / post-raid startup waits go (diagnosis only).
                Hook(h, "EFTHardSettings", "Load", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "EFT.Settings.SettingsManager", "InstantiateSingleton", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "EFT.ClientApplicationInitOperation", "LoadUserSettings", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                // Init() and Init(YieldDelegate): hook both by parameter count (Init() just calls the other).
                foreach (int n in new[] { 0, 1 })
                    Hook(h, "EFT.UI.UiPools", "Init", m => m.GetParameters().Length == n, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "EFT.TarkovApplication", "LoadHandbook", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                Hook(h, "PlayerVoiceLoader", "LoadVoice", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Task_Post));
                // The Handbook constructor is covered by LoadHandbook (Hook patches methods only).
            }

            // Collect timing, plus the force-collect lean switch (Collect_Pre/Post); see Hooks.Collect_Pre.
            if (LeanGcOn || Timing.Value)
                Hook(h, "InGameMemoryManagement", "Collect", m => m.GetParameters().Length == 5, nameof(Hooks.Collect_Pre), nameof(Hooks.Collect_Post));
            // The game assigns Settings (a struct) here from the server's ClientSettings; flip AggressiveGC after each Init.
            if (LeanGcOn)
                Hook(h, "InGameMemoryManagement", "Init", null, null, nameof(Hooks.MemInit_Post));

            if (FastShadersOn)
                Hook(h, "ShadersFinder", "InitShadersDictionary", null, nameof(Hooks.Shaders_Pre), null);

            if (MenuOn || Timing.Value)
            {
                // First menu and every post-raid return. Held only when MenuLoadLifter is on; otherwise just timed.
                Hook(h, "EFT.TarkovApplication", "PrepareDataJob", null, nameof(Hooks.Sync_Pre), MenuOn ? nameof(Hooks.Load_Post) : nameof(Hooks.Task_Post));
                // Post-raid, after the last result screen's "Next": scene unload, MenuUIScene load and PrepareGameJob
                // up to PrepareDataJob, which starts as this completes. Never spans a user-facing screen.
                Hook(h, "EFT.TarkovApplication", "ComebackToMainMenu", null, nameof(Hooks.Sync_Pre), MenuOn ? nameof(Hooks.Load_Post) : nameof(Hooks.Task_Post));
            }

            bool holds = ThrottleOn || RetainOn || MenuOn;
            if (holds || Timing.Value)
                // Map scene load; a hold only when an effect is on, otherwise just timed.
                Hook(h, "EFT.LoadScenesFromPresetOperation", "LoadPresetAsync", null, nameof(Hooks.Sync_Pre), nameof(Hooks.Load_Post));
            if (holds)
            {
                // Fika's CoopGame.InitPlayer awaits WaitForHostToStart (a lobby wait of unbounded length), then
                // InitializeLoot -> SpawnLoot (loot pools; BaseLocalGame<T>, generic, not patchable) -> vmethod_1
                // = controller.StartBotSystemsAndCountdown. On the host that loads bot pools (InitializeBotsSystem)
                // and then calls WaitForOtherPlayersToLoad (another unbounded wait; Fika controllers, non-generic).
                // So the hold opens when WaitForHostToStart completes and closes on entry to
                // WaitForOtherPlayersToLoad (verified against the Fika decomp). If the end marker is missed
                // (reconnect, exception), the per-hold watchdog releases it.
                foreach (var c in new[] { "ClientGameController", "HostGameController" })
                {
                    Hook(h, "Fika.Core.Main.GameMode." + c, "WaitForHostToStart", null, null, nameof(Hooks.RaidPools_Open_Post));
                    Hook(h, "Fika.Core.Main.GameMode." + c, "WaitForOtherPlayersToLoad", null, nameof(Hooks.RaidPools_Close_Pre), null);
                }
            }
            if (ThrottleOn)
                // ChangeFramerate resets targetFrameRate on screen/setting changes.
                Hook(h, "EFT.Settings.Graphics.GraphicsSettingsController", "ChangeFramerate", null, null, nameof(Hooks.Framerate_Post));
        }

        // DependencyGraph<IEasyBundle>.RetainRecursiveAsync awaits Task.Yield (one frame) every
        // RETAIN_COUNT_FOR_AWAIT node visits, which paces bundle loads. A plain static field, set per
        // closed type, so no patch on the shared generic code. Lifter sets it only while a hold is active.
        private static FieldInfo FindRetainField()
        {
            try
            {
                var graph = AccessTools.TypeByName("Diz.DependencyManager.DependencyGraph`1");
                var bundle = AccessTools.TypeByName("Diz.Resources.IEasyBundle");
                var field = graph?.MakeGenericType(bundle).GetField("RETAIN_COUNT_FOR_AWAIT", BindingFlags.Public | BindingFlags.Static);
                if (bundle == null || field == null) Log.LogWarning("RETAIN_COUNT_FOR_AWAIT not found, skipped");
                return field;
            }
            catch (Exception e) { Log.LogWarning("RETAIN_COUNT_FOR_AWAIT lookup failed: " + e.Message); return null; }
        }

        // Unity APIs are main-thread only; Task continuations run elsewhere. Drained every frame.
        private void Update()
        {
            while (_mainQueue.TryDequeue(out var a)) a();
            Lifter.Tick();
        }

        internal static void OnMain(Action a)
        {
            if (Thread.CurrentThread.ManagedThreadId == _mainThread) a(); else _mainQueue.Enqueue(a);
        }

        private static void Hook(Harmony h, string type, string method, Func<MethodInfo, bool> pick, string pre, string post)
            => Hook(h, AccessTools.TypeByName(type), method, pick, pre, post);

        private static void Hook(Harmony h, Type type, string method, Func<MethodInfo, bool> pick, string pre, string post)
        {
            string label = (type?.Name ?? "?") + "." + method;
            try
            {
                var m = type?.GetMethods(Declared).FirstOrDefault(x => x.Name == method && (pick == null || pick(x)));
                if (m == null) { Log.LogWarning("not found, skipped: " + label); return; }
                if (m.IsGenericMethod || m.DeclaringType.IsGenericType) { Log.LogWarning("generic target (shared code), skipped: " + label); return; }
                h.Patch(m,
                    pre == null ? null : new HarmonyMethod(typeof(Hooks), pre),
                    post == null ? null : new HarmonyMethod(typeof(Hooks), post));
                Log.LogInfo("patched " + label);
            }
            catch (Exception e) { Log.LogWarning("patch failed, skipped: " + label + ": " + e.Message); }
        }
    }

    // One timed phase. Start and end are logged with wall-clock time (no timestamps in LogOutput.log).
    internal class Phase
    {
        private readonly string _name;
        private readonly Stopwatch _sw = Stopwatch.StartNew();

        public Phase(string name)
        {
            _name = name;
            Say(name + " start");
        }

        public void End() => Say(_name + " end " + _sw.ElapsedMilliseconds + "ms");

        private static void Say(string s)
            => Plugin.Log.LogInfo(DateTime.Now.ToString("HH:mm:ss.fff") + " [LoadSpeedFix] " + s);

        public static Phase Begin(string name) => Plugin.Timing.Value ? new Phase(name) : null;
        public static Phase Begin(MethodBase m) => Begin(m.DeclaringType.Name.Split('`')[0] + "." + m.Name);
    }

    // Ref-counted so nested/parallel loads restore only when the last one finishes. Main thread only,
    // except Begin (any thread; it marshals Acquire/Release to the main thread).
    internal static class Lifter
    {
        private static readonly TimeSpan Watchdog = TimeSpan.FromMinutes(10);
        private static int _count;
        private static UnityEngine.ThreadPriority _prio;
        private static int _fps;
        private static bool _throttled, _retained;
        private static int _retainOrig;
        private static Action _raidPools;

        public static bool Active => _count > 0;

        // Starts a hold and returns its idempotent release, or null when no effect is installed.
        public static Action Begin()
        {
            if (!Plugin.ThrottleOn && !Plugin.RetainOn && !Plugin.MenuOn) return null;
            int released = 0;
            var cts = new CancellationTokenSource();
            Action release = () =>
            {
                if (Interlocked.Exchange(ref released, 1) != 0) return;
                cts.Cancel();
                cts.Dispose();
                Plugin.OnMain(Release);
            };
            Plugin.OnMain(Acquire);
            // So a load that never completes cannot leave the FPS cap off forever.
            Task.Delay(Watchdog, cts.Token).ContinueWith(_ =>
            {
                Plugin.Log.LogWarning("load did not finish within " + Watchdog.TotalMinutes + " min, releasing hold");
                release();
            }, TaskContinuationOptions.OnlyOnRanToCompletion);
            return release;
        }

        // Hooks a running task: hold until it completes or faults.
        public static void Hold(Task t)
        {
            if (t == null) return;
            var release = Begin();
            if (release != null) t.ContinueWith(_ => release());
        }

        // Fika raid pool phase (loot + bot pools) spans two hook calls; see Plugin.Awake.
        public static void OpenRaidPools()
            => Interlocked.Exchange(ref _raidPools, Begin())?.Invoke();

        public static void CloseRaidPools()
            => Interlocked.Exchange(ref _raidPools, null)?.Invoke();

        private static void Acquire()
        {
            if (_count++ > 0) return;
            if (Plugin.ThrottleOn)
            {
                _prio = Application.backgroundLoadingPriority;
                _fps = Application.targetFrameRate;
                Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
                Application.targetFrameRate = -1;
                _throttled = true;
            }
            int batch = Plugin.RetainOn ? Plugin.RetainBatch.Value : 0;
            if (batch > 0)
                try
                {
                    _retainOrig = (int)Plugin.RetainField.GetValue(null);
                    Plugin.RetainField.SetValue(null, batch);
                    _retained = true;
                    Plugin.Log.LogInfo("RETAIN_COUNT_FOR_AWAIT " + _retainOrig + " -> " + batch);
                }
                catch (Exception e) { Plugin.Log.LogWarning("RETAIN_COUNT_FOR_AWAIT not set: " + e.Message); }
        }

        private static void Release()
        {
            if (_count == 0 || --_count > 0) return;
            if (_throttled)
            {
                // Restore only what is still ours; if the game changed a value meanwhile, keep its choice.
                if (Application.backgroundLoadingPriority == UnityEngine.ThreadPriority.High) Application.backgroundLoadingPriority = _prio;
                if (Application.targetFrameRate == -1) Application.targetFrameRate = _fps;
                _throttled = false;
            }
            if (_retained)
            {
                try { Plugin.RetainField.SetValue(null, _retainOrig); }
                catch (Exception e) { Plugin.Log.LogWarning("RETAIN_COUNT_FOR_AWAIT not restored: " + e.Message); }
                _retained = false;
            }
        }

        // Streamer.LoadSceneCoroutine sets the priority to Low on every scene load; win it back each frame.
        public static void Tick()
        {
            if (_throttled && Application.backgroundLoadingPriority != UnityEngine.ThreadPriority.High)
                Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        }

        // The game just chose a new limit; keep it for the restore and stay uncapped meanwhile.
        public static void Reapply()
        {
            if (!_throttled) return;
            _fps = Application.targetFrameRate;
            Application.targetFrameRate = -1;
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.High;
        }
    }

    // Item templates cache, hooked on ItemFactoryCreateOperation.CreateItemFactory (non-generic).
    // The game's own contract: if GetTemplates returns the very instance the current ItemFactory
    // holds, reuse that factory and skip Init(). The post-raid reload (TarkovApplication.PrepareGameJob
    // -> GetItemFactoryCreationTask()) passes a null factory, so we substitute the current
    // Singleton<ItemFactory>.Instance and, on a hit, return it directly (the same thing the original
    // same-instance branch returns; PrepareGameJob then Release()s and Create()s that singleton) and
    // skip the /client/items fetch+parse. Init() is NOT idempotent (AddChild would duplicate children),
    // so a hit is served ONLY when the factory's templates are reference-equal to the cached instance
    // for the same backend; any other case falls through to the real request.
    internal static class ItemsCache
    {
        private static readonly object Lock = new object();
        private static JsonType.ItemTemplates _value;
        private static string _key;

        public static string KeyOf(object session)
        {
            try
            {
                var t = Traverse.Create(session);
                string url = t.Field("_backendUrl").GetValue<string>();
                // URL only: templates are per server, and /client/items loads before the profile is
                // selected at startup, so a profile id would differ between store and post-raid lookup.
                return string.IsNullOrEmpty(url) ? null : url.TrimEnd('/');
            }
            catch (Exception e) { Plugin.Log.LogWarning("items cache key failed: " + e.Message); return null; }
        }

        public static bool TryGet(string key, JsonType.ItemTemplates current)
        {
            lock (Lock) return key != null && _value != null && _key == key && ReferenceEquals(_value, current);
        }

        public static JsonType.ItemTemplates Value { get { lock (Lock) return _value; } }

        public static void Set(string key, JsonType.ItemTemplates v) { lock (Lock) { _key = key; _value = v; } }
        public static void Clear() { lock (Lock) { _key = null; _value = null; } }
    }

    // Harmony injects by parameter name, so these stay string-addressable from Plugin.Hook.
    internal static class Hooks
    {
        public static void Sync_Pre(MethodBase __originalMethod, out Phase __state) => __state = Phase.Begin(__originalMethod);
        public static void Sync_Post(Phase __state) => __state?.End();
        public static void Mark_Pre(MethodBase __originalMethod) => Phase.Begin(__originalMethod);

        public static void Task_Post(Phase __state, Task __result)
        {
            if (__state != null) __result?.ContinueWith(_ => __state.End());
        }

        public static void Pools_Pre(out Stopwatch __state)
            => __state = Plugin.Timing.Value ? Stopwatch.StartNew() : null;

        public static void Pools_Post(Stopwatch __state, Task __result, object resources)
        {
            if (__state == null || __result == null) return;
            int n = (resources as System.Collections.ICollection)?.Count ?? -1;
            __result.ContinueWith(_ =>
            {
                if (__state.ElapsedMilliseconds > 2000)
                    Plugin.Log.LogInfo(DateTime.Now.ToString("HH:mm:ss.fff") + " [LoadSpeedFix] ObjectsFactory.LoadBundlesAndCreatePools("
                        + n + " resources) took " + __state.ElapsedMilliseconds + "ms");
            });
        }

        // Loading-screen phase: timed, and the throttle is lifted until it completes.
        public static void Load_Post(Phase __state, Task __result)
        {
            Task_Post(__state, __result);
            Lifter.Hold(__result);
        }

        public static void Framerate_Post() => Lifter.Reapply();

        // InGameMemoryManagement.Collect(generation, mode, isBlocking, compacting, force) always does
        // GC.Collect() (plus GetTotalMemory(true) before and after, which collect too). Only when
        // Settings.AggressiveGC is set does it additionally log "GC::Collect Aggressive", run
        // WaitForPendingFinalizers, set LOH CompactOnce and do a second GC.Collect(generation, ...)
        // (InGameMemoryManagement.cs:121-127). MemInit_Post clears that flag for every Init, so what
        // remains is the single collect and the two GetTotalMemory(true) stat reads.
        // Exception: BaseLocalGame.PrepareSession's Collect(force: true) runs right before the game
        // disables the GC for the raid, so finalizers must be drained there. That call is the
        // Collect(bool) shorthand = (2, Optimized, blocking, !compacting, force); the other forced callers
        // (TarkovApplication) pass Forced+compacting. Only that shape gets AggressiveGC back for the call.
        // (Hooked on the 5-arg overload: Collect(bool) is small enough for the JIT to inline past a patch.)
        private static bool _aggressiveForCall;

        public static void MemInit_Post()
        {
            try
            {
                var s = InGameMemoryManagement.Settings;
                s.AggressiveGC = false;
                InGameMemoryManagement.Settings = s;
            }
            catch (Exception e) { Plugin.Log.LogWarning("LeanGC: could not clear AggressiveGC: " + e.Message); }
        }

        public static void Collect_Pre(MethodBase __originalMethod, out Phase __state, GCCollectionMode gcMode, bool compacting, bool force)
        {
            __state = Phase.Begin(__originalMethod);
            if (!Plugin.LeanGcOn || !force || gcMode != GCCollectionMode.Optimized || compacting) return;
            try
            {
                var s = InGameMemoryManagement.Settings;
                if (s.AggressiveGC) return;
                s.AggressiveGC = true;
                InGameMemoryManagement.Settings = s;
                _aggressiveForCall = true;
            }
            catch (Exception e) { Plugin.Log.LogWarning("LeanGC: pre-raid collect left lean: " + e.Message); }
        }

        public static void Collect_Post(Phase __state)
        {
            __state?.End();
            if (!_aggressiveForCall) return;
            _aggressiveForCall = false;
            try
            {
                var s = InGameMemoryManagement.Settings;
                s.AggressiveGC = false;
                InGameMemoryManagement.Settings = s;
            }
            catch (Exception e) { Plugin.Log.LogWarning("LeanGC: could not restore: " + e.Message); }
        }

        // One Shader.Find per frame (~3.4 s) -> same fill in one go. Main thread only (Shader.Find);
        // any doubt or failure falls back to the original, which refills everything anyway.
        public static bool Shaders_Pre(ref Task __result)
        {
            if (!Plugin.IsMain) return true;
            try
            {
                var keys = ShadersFinder.SHADERS.Keys.ToArray();
                foreach (string key in keys)
                    ShadersFinder.SHADERS[key] = Shader.Find(key);
                ShadersFinder._isInited = true;
                Plugin.Log.LogInfo("fast shader lookup: " + keys.Length + " shaders");
                __result = Task.CompletedTask;
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("fast shader lookup failed, using original: " + e.Message);
                return true;
            }
        }

        // Runs before the caller awaits, so (ExecuteSynchronously) the hold opens before InitPlayer resumes.
        public static void RaidPools_Open_Post(Task __result)
        {
            __result?.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) Lifter.OpenRaidPools(); },
                TaskContinuationOptions.ExecuteSynchronously);
        }

        public static void RaidPools_Close_Pre() => Lifter.CloseRaidPools();

        public static bool Items_Pre(object backendSession, ItemFactory itemFactory,
            ref Task<ItemFactory> __result, out Stopwatch __state)
        {
            __state = null;
            try
            {
                string key = ItemsCache.KeyOf(backendSession);
                // Post-raid reload passes null; the live factory is the singleton.
                var current = itemFactory ?? (Singleton<ItemFactory>.Instantiated ? Singleton<ItemFactory>.Instance : null);
                if (ItemsCache.TryGet(key, current?.ItemTemplates))
                {
                    __result = Task.FromResult(current);
                    Plugin.Log.LogInfo(DateTime.Now.ToString("HH:mm:ss.fff") + " [LoadSpeedFix] items cache hit, skipped fetch+parse");
                    return false;
                }
                __state = Stopwatch.StartNew();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("items cache lookup failed, using original: " + e.Message);
                ItemsCache.Clear();
            }
            return true;
        }

        public static void Items_Post(object backendSession, Stopwatch __state, ref Task<ItemFactory> __result)
        {
            if (__state == null || __result == null) return; // hit, or lookup failed
            try
            {
                string key = ItemsCache.KeyOf(backendSession);
                var task = __result;
                task.ContinueWith(t =>
                {
                    var templates = t.Status == TaskStatus.RanToCompletion ? t.Result?.ItemTemplates : null;
                    if (templates != null && key != null)
                    {
                        ItemsCache.Set(key, templates);
                        Plugin.Log.LogInfo(DateTime.Now.ToString("HH:mm:ss.fff") + " [LoadSpeedFix] items cached after "
                            + __state.ElapsedMilliseconds + "ms factory create");
                    }
                    else ItemsCache.Clear();
                });
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("items cache store failed: " + e.Message);
                ItemsCache.Clear();
            }
        }
    }
}
