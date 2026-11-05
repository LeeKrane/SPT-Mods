using System.Reflection;
using Microsoft.Extensions.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Servers;

namespace StorylineCleanup4Server;

/// <summary>
/// Config option DisableStoryMapLock. VisitAPI's StoryLocks.OnNewProfile sets the profile variable 766973697461706900000001
/// to 1 on every new profile, and its client plugin locks maps behind Tour quests while it is 1. When enabled, this strips
/// the variable from every loaded profile (SaveServer loads at SaveCallbacks, before this PostLoad + 100 hook) and patches
/// OnNewProfile (resolved by reflection, VisitAPI is no compile-time reference) so new profiles never keep it.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.PostLoad + 100)]
public sealed class StoryMapLock(ILogger<StoryMapLock> logger, ModHelper modHelper, SaveServer saveServer, ProfileHelper profileHelper) : IOnLoad
{
    public static readonly MongoId FlagVariable = new("766973697461706900000001");

    internal static ProfileHelper? Profiles;

    public async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var folder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
            var cfg = modHelper.GetJsonDataFromFile<CleanupConfig>(folder, "config.json");
            if (cfg?.DisableStoryMapLock != true) return;

            Profiles = profileHelper;
            var changed = new List<MongoId>();
            foreach (var (id, profile) in saveServer.GetProfiles())
                if (profile.CharacterData?.PmcData?.Variables?.Remove(FlagVariable) == true) changed.Add(id);
            foreach (var id in changed) await saveServer.SaveProfileAsync(id, cancellationToken);
            logger.LogInformation("[StorylineCleanup4Server] Story map lock variable removed from {Count} existing profile(s)", changed.Count);

            EnablePatch();
        }
        catch (Exception e)
        {
            logger.LogWarning("[StorylineCleanup4Server] Story map lock removal failed: {Error}", e);
        }
    }

    private void EnablePatch()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VisitAPI.Server.StoryLocks", false)).FirstOrDefault(t => t != null);
        var method = type?.GetMethod("OnNewProfile", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method == null)
        {
            logger.LogWarning("[StorylineCleanup4Server] VisitAPI StoryLocks.OnNewProfile not found, new profiles keep the story map lock");
            return;
        }
        new OnNewProfilePatch(method).Enable();
        logger.LogInformation("[StorylineCleanup4Server] Patched VisitAPI StoryLocks.OnNewProfile to drop the story map lock on new profiles");
    }

    private sealed class OnNewProfilePatch(MethodBase target) : AbstractPatch
    {
        protected override MethodBase GetTargetMethod() => target;

        [PatchPostfix]
        public static void Postfix(MongoId sessionId)
        {
            try
            {
                Profiles?.GetPmcProfile(sessionId)?.Variables?.Remove(FlagVariable);
            }
            catch
            {
                // never break profile creation
            }
        }
    }
}
