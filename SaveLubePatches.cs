using System;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// SaveLube is internal, so its save-lifecycle methods are patched via reflection (same
    /// approach as MenuFactoryPatches). Hooking these keeps this mod's own area-progress file
    /// in sync with the game's own save/delete/load lifecycle instead of inventing a separate one.
    /// </summary>
    internal static class SaveLubePatches
    {
        private const string ProgressFileName = "F.AreaInfoDisplayOnPause.AreaProgress.xml";

        private static readonly string s_progressPath = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), ProgressFileName);

        public static void Apply(Harmony harmony)
        {
            Type saveLubeType = AccessTools.TypeByName("JumpKing.SaveThread.SaveLube");
            harmony.Patch(AccessTools.Method(saveLubeType, "SaveCombinedSaveFile"),
                postfix: new HarmonyMethod(typeof(SaveLubePatches), nameof(SaveCombinedSaveFilePostfix)));
            harmony.Patch(AccessTools.Method(saveLubeType, "DeleteSaves"),
                postfix: new HarmonyMethod(typeof(SaveLubePatches), nameof(DeleteSavesPostfix)));
        }

        /// <summary>
        /// SaveLube.ProgramStartInitialize() is called once, from Program.Run(), before Game1
        /// (and so before any mod's Harmony patches) even exists - so a postfix on it can never
        /// fire for that one real call. Load directly here instead; ModEntry.BeforeLevelLoad
        /// itself only runs once per process (JumpGame's constructor calls it once), which is
        /// early enough since it happens before LevelManager.LoadScreens()/OnLevelStart.
        /// </summary>
        public static void LoadProgress()
        {
            AreaProgressStore.Load(s_progressPath);
        }

        private static void SaveCombinedSaveFilePostfix()
        {
            AreaProgressStore.Save(s_progressPath);
        }

        private static void DeleteSavesPostfix()
        {
            AreaProgressStore.Clear();
            if (File.Exists(s_progressPath))
            {
                File.Delete(s_progressPath);
            }
        }
    }
}
