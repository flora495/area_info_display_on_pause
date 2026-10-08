using JumpKing;
using JumpKing.SaveThread;
using LanguageJK;

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

        /// <summary>
        /// Same branching as GetCurrentLevelKey, but the human-readable title instead - a straight
        /// copy of MenuFactory.GetLevelTitle() (an instance method on an internal class, so not
        /// callable directly from here).
        /// </summary>
        public static string GetCurrentLevelTitle()
        {
            JumpKing.Workshop.Level level = Game1.instance.contentManager.level;
            if (level != null)
            {
                return level.Name;
            }
            if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedGhost))
            {
                return language.GAMETITLESCREEN_GHOST_OF_THE_BABE;
            }
            if (EventFlagsSave.ContainsFlag(StoryEventFlags.StartedNBP))
            {
                return language.GAMETITLESCREEN_NEW_BABE_PLUS;
            }
            return language.GAMETITLESCREEN_NEW_GAME;
        }
    }
}
