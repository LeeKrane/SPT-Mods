// VisitAPI's VISIT button loads a 300 MB to 1 GB 3D trader room (and .dlg trees may load a 3D scene).
// With SkipTraderRooms on, this plugin opens the plain dialog screen instead:
//  * NarrateEntry.Visit is skipped and the dialog screen is opened the way VisitAPI's own scene-less
//    fallback does (InvitePresenter: subtitles + cached voice clips, no animations), plus the
//    unlock/seed steps NarrateDialogEntryGuard and NarrateQuestGhostGuard do only while Narrating.Now.
//  * SceneLoader.Open is skipped outside raids.
// TraderVoiceVolume scales voice lines in all three paths (3D room lipsync, InvitePresenter, .dlg audio).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using EFT;
using EFT.Dialogs;
using EFT.Quests;
using EFT.UI;
using HarmonyLib;
using UnityEngine;
using VisitAPI.Native;

namespace Krane.InstantTraderDialogs
{
    [BepInPlugin("krane.instanttraderdialogs", "Krane's InstantTraderDialogs", "1.0.0")]
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
            Skip = Config.Bind("General", "SkipTraderRooms", true, "Skip 3D trader rooms and open the dialog directly");
            Volume = Config.Bind("General", "TraderVoiceVolume", 0.4f,
                new ConfigDescription("Volume of trader voice lines in trader dialogs (3D room and skipped-room mode)",
                    new AcceptableValueRange<float>(0f, 1f)));
            Volume.SettingChanged += (_, __) => VoiceSources.Apply();
            new Harmony("krane.instanttraderdialogs").PatchAll(typeof(Plugin).Assembly);
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
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Direct visit failed: " + e);
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
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Skipping the dialog scene failed: " + e);
                return true;
            }
        }
    }

    // Copy of NarrateDialogEntryGuard: its Prefix is private and gated on Narrating.Now (false in direct
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

    // Space skip refuses while the current dialog is blocked. A quit-type line (goodbye, open trading or quests
    // screen) has no switch action, so its dialog stays blocked until ForceQuitDialog runs after the animation.
    // Unblock that dialog for the vanilla call only, so Space ends the wait the same way as for other lines.
    [HarmonyPatch(typeof(ClientDialogController), nameof(ClientDialogController.TrySkipAnimation))]
    internal static class QuitLineSkipPatch
    {
        private static void Prefix(ClientDialogController __instance, out BaseTraderDialog __state)
        {
            __state = null;
            try
            {
                var dialog = __instance.CurrentDialog;
                if (dialog?.IsBlocked != true || __instance._animationTaskSource == null) return;
                if (!__instance.list_0.Any(a => a is DialogQuitAction || a is DialogTradingScreenAction || a is DialogQuestsScreenAction)) return;
                dialog.IsBlocked = false;
                __state = dialog;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("Unblocking the quit line for skipping failed: " + e);
            }
        }

        private static void Finalizer(BaseTraderDialog __state)
        {
            if (__state != null) __state.IsBlocked = true;
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

    // Direct mode voice: InvitePresenter.Play sets _src.volume per line.
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
