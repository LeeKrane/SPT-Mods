// VisitAPI's VISIT button loads a 300 MB to 1 GB 3D trader room (and .dlg trees may load a 3D scene).
// With SkipTraderRooms on, this plugin replaces that with a portrait:
//  * NarrateEntry.Visit is skipped and the dialog screen is opened the way VisitAPI's own scene-less
//    fallback does (InvitePresenter: subtitles + cached voice clips, no animations), plus the
//    unlock/seed steps NarrateDialogEntryGuard and NarrateQuestGhostGuard do only while Narrating.Now.
//  * SceneLoader.Open is skipped outside raids.
//  * A dark backdrop with the trader's avatar is drawn behind the dialog UI while the screen is open.
// TraderVoiceVolume scales voice lines in all three paths (3D room lipsync, InvitePresenter, .dlg audio).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using VisitAPI.Native;

namespace Krane.TraderPortraits
{
    [BepInPlugin("krane.traderportraits", "TraderPortraits", "1.0.0")]
    [BepInDependency("com.sora.visitapi", BepInDependency.DependencyFlags.HardDependency)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<bool> Skip;
        internal static ConfigEntry<float> Volume;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Skip = Config.Bind("General", "SkipTraderRooms", false, "Replace 3D trader rooms with a portrait");
            Volume = Config.Bind("General", "TraderVoiceVolume", 1f,
                new ConfigDescription("Volume of trader voice lines in trader dialogs (3D room and portrait mode)",
                    new AcceptableValueRange<float>(0f, 1f)));
            Volume.SettingChanged += (_, __) => VoiceSources.Apply();
            new Harmony("krane.traderportraits").PatchAll(typeof(Plugin).Assembly);
        }
    }

    // Live volume for .dlg voice sources (DialogAudio keeps its own AudioSource and only calls Play on it).
    internal static class VoiceSources
    {
        private static readonly List<AudioSource> Sources = new List<AudioSource>();

        internal static void Track(AudioSource s)
        {
            if (s == null) return;
            Sources.Add(s);
            s.volume = Plugin.Volume.Value;
        }

        internal static void Apply()
        {
            if (Sources.Count == 0) return;
            Sources.RemoveAll(s => s == null);
            var v = Plugin.Volume.Value;
            foreach (var s in Sources) s.volume = v;
        }
    }

    // True while the dialog screen opened by our Visit prefix is up (what Narrating.Now gates in VisitAPI).
    // Id identifies the session so a late End of an older one cannot clear a newer one.
    internal static class Session
    {
        internal static bool Ours;
        internal static int Id;

        // Always runs once per session: waits like InvitePresenter.Watch (10 s to open), then until the screen closes.
        internal static IEnumerator End(int id)
        {
            var t0 = Time.unscaledTime;
            while (!DialogScreenTracker.Open && Time.unscaledTime - t0 < 10f) yield return null;
            while (DialogScreenTracker.Open) yield return null;
            if (Id != id) yield break;
            Ours = false;
            TabRouter.UnwatchNarrate();
        }
    }

    internal class Backdrop : MonoBehaviour
    {
        private static readonly AccessTools.FieldRef<TraderDialogScreen, MongoID> TraderId =
            AccessTools.FieldRefAccess<TraderDialogScreen, MongoID>("_traderId");

        private static Backdrop _current;
        private int _waited;
        private Task<Sprite> _avatar;

        internal static void Open() => Plugin.Instance.StartCoroutine(Build());

        private static IEnumerator Build()
        {
            var t0 = Time.unscaledTime;
            while (!DialogScreenTracker.Open && Time.unscaledTime - t0 < 10f) yield return null;
            var screen = DialogScreenTracker.Live;
            if (screen == null)
            {
                Plugin.Log.LogWarning("Dialog screen did not open, no portrait");
                yield break;
            }

            Backdrop b = null;
            try
            {
                if (_current != null) Destroy(_current.gameObject);

                var session = ((ClientApplication<IEftSession>)GetApp()).Session;
                var id = TraderId(screen);
                var info = session.Profile.TradersInfo[id];

                var root = new GameObject("TraderPortraitRoot", typeof(RectTransform));
                var rt = (RectTransform)root.transform;
                rt.SetParent(screen.transform.parent, false);
                Stretch(rt);
                rt.SetSiblingIndex(0);
                b = root.AddComponent<Backdrop>();
                _current = b;

                var dark = root.AddComponent<Image>();
                dark.color = new Color(0.04f, 0.04f, 0.05f, 0.92f);
                dark.raycastTarget = false;

                b._avatar = info.Settings.GetAvatar();
                Plugin.Instance.StartCoroutine(AddPortrait(b, rt));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Building the portrait failed: " + e);
                if (b != null) Destroy(b.gameObject);
            }
        }

        private static TarkovApplication GetApp()
        {
            TarkovApplication.Exist(out var app);
            return app;
        }

        private static IEnumerator AddPortrait(Backdrop b, RectTransform root)
        {
            while (b != null && !b._avatar.IsCompleted) yield return null;
            if (b == null) yield break;
            if (b._avatar.IsFaulted || b._avatar.Result == null)
            {
                Plugin.Log.LogWarning("Trader avatar not available, backdrop only");
                yield break;
            }
            var go = new GameObject("Portrait", typeof(RectTransform));
            var prt = (RectTransform)go.transform;
            prt.SetParent(root, false);
            // Left of centre so the dialog window (right side) is not covered; not verified in game.
            prt.anchorMin = new Vector2(0.02f, 0.03f);
            prt.anchorMax = new Vector2(0.58f, 0.97f);
            prt.offsetMin = prt.offsetMax = Vector2.zero;
            var img = go.AddComponent<Image>();
            img.sprite = b._avatar.Result;
            img.preserveAspect = true;
            img.raycastTarget = false;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private void Update()
        {
            if (DialogScreenTracker.Open) { _waited = 0; return; }
            // Closed (VisitAPI clears the tracker on close). Short grace period for a reopen.
            if (++_waited > 5) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (_current == this) _current = null;
        }
    }

    [HarmonyPatch(typeof(NarrateEntry), nameof(NarrateEntry.Visit))]
    internal static class VisitPatch
    {
        private static readonly System.Reflection.FieldInfo Starting = AccessTools.Field(typeof(NarrateEntry), "_starting");
        private static readonly System.Reflection.FieldInfo StartingAt = AccessTools.Field(typeof(NarrateEntry), "_startingAt");
        private static readonly System.Reflection.FieldInfo MenuOp = AccessTools.Field(typeof(TarkovApplication), "_menuOperation");

        private static bool Prefix(string traderId)
        {
            if (!Plugin.Skip.Value || Raid.Now) return true;
            var started = false;
            var id = 0;
            try
            {
                if (!TarkovApplication.Exist(out var app) || app.NarrateControllerAccess == null) return true;
                // Cases VisitAPI handles itself (it logs, queues or ignores): leave them to the original.
                if (app.NarrateControllerAccess.GameExist || NarrateHideGuard.Unloading) return true;
                // The original ignores repeated Visit while a start is pending (for 300 s); do the same, never load the room.
                if ((bool)Starting.GetValue(null) && Time.unscaledTime - (float)StartingAt.GetValue(null) < 300f) return false;
                if (DialogScreenTracker.Open) return false;

                var op = MenuOp.GetValue(app) as MainMenuShowOperation;
                if (op?.DialogController == null) return true;
                var session = ((ClientApplication<IEftSession>)app).Session;
                if (session?.Profile == null || !session.Profile.TradersInfo.ContainsKey(new MongoID(traderId))) return true;

                id = ++Session.Id;
                Session.Ours = true;
                started = true;
                // Same as NarrateEntry.Fallback minus the MenuScreen wait and notification.
                WhitelistPatch.RegisteredTraders.Add(traderId);
                TabRouter.WatchNarrate(traderId);
                var presenter = new InvitePresenter(op.DialogController, traderId);
                try
                {
                    op.ShowTraderDialogScreen(new MongoID(traderId), null, presenter);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Opening the dialog screen failed: " + e);
                    if (!DialogScreenTracker.Open)
                    {
                        Session.Ours = false;
                        TabRouter.UnwatchNarrate();
                        return true;
                    }
                }
                // The screen is up: from here on failures only log, the session stays consistent.
                Start(presenter.Watch());
                Start(Session.End(id));
                Backdrop.Open();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Portrait visit failed: " + e);
                if (!started) return !DialogScreenTracker.Open;
                // Failed before the screen was shown (screen failures are handled above) or in the setup steps.
                if (!DialogScreenTracker.Open)
                {
                    Session.Ours = false;
                    TabRouter.UnwatchNarrate();
                    return true;
                }
                Start(Session.End(id));
                return false;
            }
        }

        private static void Start(IEnumerator routine)
        {
            try { Plugin.Instance.StartCoroutine(routine); }
            catch (Exception e) { Plugin.Log.LogError("Starting a watcher failed: " + e); }
        }
    }

    [HarmonyPatch(typeof(SceneLoader), nameof(SceneLoader.Open))]
    internal static class SceneOpenPatch
    {
        private static bool Prefix()
        {
            if (!Plugin.Skip.Value || Raid.Now) return true;
            try
            {
                Backdrop.Open();
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Skipping the dialog scene failed: " + e);
                return true;
            }
        }
    }

    // Copy of NarrateDialogEntryGuard: its Prefix is private and gated on Narrating.Now (false in portrait
    // mode, no visit game exists), so it cannot be invoked for our session.
    [HarmonyPatch(typeof(BaseTraderDialogController), "InitNewDialog")]
    internal static class DialogEntryPatch
    {
        private static void Prefix(BaseTraderDialogController __instance, MongoID dialogId)
        {
            if (!Session.Ours) return;
            try
            {
                if (DialogStorage.Instance != null && DialogStorage.Instance.TryGetTemplate(dialogId, out var t))
                    t.CanBeFirstDialog = true;
                RetailDialogs.MarkAcquainted(__instance, __instance?.Trader?.Id);
                RetailDialogs.SeedVariables(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Unlock / seeding before dialog {dialogId} failed: {e}");
            }
        }
    }

    // Copy of NarrateQuestGhostGuard (private Prefix, gated on Narrating.Now): quests the profile does not
    // have count as locked.
    [HarmonyPatch(typeof(QuestStatusCondition), "Test")]
    internal static class QuestGhostPatch
    {
        private static bool Prefix(QuestStatusCondition __instance, IDialogContext context, ref bool __result)
        {
            if (!Session.Ours) return true;
            if (context?.QuestsData != null && context.QuestsData.Any(q => q.Id == __instance.QuestId)) return true;
            __result = __instance.Statuses != null && __instance.Statuses.Contains(EQuestStatus.Locked);
            return false;
        }
    }

    // 3D room voice: PlayAudioSource sets audioSource.volume = volume. `volume` also drives the lip sync
    // amplitude, so only the AudioSource is scaled.
    [HarmonyPatch(typeof(uLipSync.uLipSyncBakedDataPlayer), "PlayAudioSource")]
    internal static class LipSyncVolumePatch
    {
        private static void Postfix(uLipSync.uLipSyncBakedDataPlayer __instance)
        {
            if (__instance.audioSource != null) __instance.audioSource.volume *= Plugin.Volume.Value;
        }
    }

    // Portrait mode voice: InvitePresenter.Play sets _src.volume per line.
    [HarmonyPatch(typeof(InvitePresenter), "Play")]
    internal static class InviteVolumePatch
    {
        private static readonly AccessTools.FieldRef<InvitePresenter, AudioSource> Src =
            AccessTools.FieldRefAccess<InvitePresenter, AudioSource>("_src");

        private static void Postfix(InvitePresenter __instance)
        {
            var s = Src(__instance);
            if (s != null) s.volume *= Plugin.Volume.Value;
        }
    }

    // .dlg voice: DialogAudio adds its voice AudioSource first, then the music one, to the screen object.
    [HarmonyPatch(typeof(DialogAudio), nameof(DialogAudio.Attach))]
    internal static class DialogAudioPatch
    {
        private static readonly AccessTools.FieldRef<DialogAudio, AudioSource> Voice =
            AccessTools.FieldRefAccess<DialogAudio, AudioSource>("_voice");

        private static void Postfix(GameObject host)
        {
            try
            {
                var audios = host.GetComponents<DialogAudio>();
                if (audios.Length > 0) VoiceSources.Track(Voice(audios[audios.Length - 1]));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Hooking .dlg voice volume failed: " + e);
            }
        }
    }
}
