// EFT's decal mip streaming thread (TextureQuadTree.WorkThread) holds _treesLock while it
// sleeps through every tree, and takes it again right after letting go. Monitor is not fair,
// so TextureQuadTree.Dispose on the main thread (StaticDeferredDecalRenderer.OnDestroy, after
// each raid) can wait on that lock for minutes. Under Wine the waiter rarely wins.
//
// While trees are being disposed, ThreadSkipJob makes the thread sleep without the lock. The
// renderer calls SetThreadParams every frame it draws, so the job resumes on the next frame.
using BepInEx;
using HarmonyLib;

namespace Krane.DecalLockFix
{
    [BepInPlugin("krane.decallockfix", "DecalLockFix", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake()
        {
            new Harmony("krane.decallockfix").PatchAll(typeof(Plugin).Assembly);
        }
    }

    [HarmonyPatch(typeof(TextureQuadTree), nameof(TextureQuadTree.Dispose))]
    internal static class DisposePatch
    {
        private static void Prefix() => TextureQuadTree.ThreadSkipJob = true;
    }

    [HarmonyPatch(typeof(TextureQuadTree), nameof(TextureQuadTree.SetThreadParams))]
    internal static class SetThreadParamsPatch
    {
        private static void Postfix() => TextureQuadTree.ThreadSkipJob = false;
    }
}
