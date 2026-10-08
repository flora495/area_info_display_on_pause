using System;
using HarmonyLib;
using JumpKing;
using JumpKing.SaveThread;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Records a cleared map into ClearedMapHistoryStore. SaveManager.AddTaskOnGameComplete is
    /// called exactly once per clear, from GameEnding.OnNewRun (the start of every ending -
    /// vanilla or custom level, any of the 3 Babes) and from nowhere else; Give Up/New Game go
    /// through AddTaskDeleteSaveFile instead, so they never land here.
    ///
    /// A prefix, not a postfix, purely for clarity: the method itself only queues the task, and
    /// the queued task (which ends in SaveLube.DeleteSaves, and so wipes AreaProgressStore via
    /// SaveLubePatches) only runs later on the SaveManager thread - either way this reads the
    /// in-progress data before it's gone.
    /// </summary>
    internal static class GameCompletePatches
    {
        public static void Apply(Harmony harmony)
        {
            harmony.Patch(AccessTools.Method(typeof(SaveManager), nameof(SaveManager.AddTaskOnGameComplete)),
                prefix: new HarmonyMethod(typeof(GameCompletePatches), nameof(AddTaskOnGameCompletePrefix)));
        }

        private static void AddTaskOnGameCompletePrefix()
        {
            // A failure here must never get in the way of the game's own ending/save handling.
            try
            {
                ClearedMapHistoryStore.Append(AreaTracker.BuildClearedRecord());
            }
            catch (Exception ex)
            {
                Program.crashLog.AddErrorMessage("AreaInfoDisplayOnPause: failed to record cleared map: " + ex);
            }
        }
    }
}
