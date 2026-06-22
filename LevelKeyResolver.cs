using JumpKing;
using JumpKing.SaveThread;

namespace AreaInfoDisplayOnPause
{
    internal static class LevelKeyResolver
    {
        /// <summary>
        /// Mirrors MenuFactory.GetLevelTitle()'s branching, but returns a stable identifier
        /// instead of a display string, so area progress can be tracked per playthrough.
        /// </summary>
        public static string GetCurrentLevelKey()
        {
            JumpKing.Workshop.Level level = Game1.instance.contentManager.level;
            if (level != null)
            {
                return level.HasDetails ? "ugc:" + level.ID : "ugc-root:" + level.Root;
            }
            if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedGhost))
            {
                return "vanilla:ghost";
            }
            if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedNBP))
            {
                return "vanilla:nbp";
            }
            return "vanilla:base";
        }
    }
}
