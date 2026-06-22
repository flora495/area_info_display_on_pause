using HarmonyLib;
using JumpKing.Level;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// LevelManager is public, so this can be patched directly without reflection. Update runs
    /// every unpaused frame (it's skipped entirely while the pause menu is open), which is the
    /// same gate the engine itself uses to decide when gameplay should keep progressing.
    /// </summary>
    internal static class LevelManagerPatches
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(LevelManager), nameof(LevelManager.Update)),
                postfix: new HarmonyMethod(typeof(LevelManagerPatches), nameof(UpdatePostfix)));
        }

        private static void UpdatePostfix()
        {
            if (!ModEntry.Settings.IsEnabled)
            {
                return;
            }
            AreaTracker.OnUpdate();
        }
    }
}
