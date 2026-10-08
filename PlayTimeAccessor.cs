using System;
using System.Reflection;
using HarmonyLib;
using JumpKing.MiscSystems.Achievements;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// JumpKing.MiscSystems.Achievements.AchievementManager is internal, so its static instance
    /// field and GetCurrentStats() method are read via reflection. PlayerStats (the struct it
    /// returns) is public, so its timeSpan can be read off it directly once we have an instance.
    /// </summary>
    internal static class PlayTimeAccessor
    {
        private static FieldInfo s_instanceField;
        private static MethodInfo s_getCurrentStatsMethod;
        private static PropertyInfo s_winStatsProperty;

        public static void Initialize()
        {
            Type achievementManagerType = AccessTools.TypeByName("JumpKing.MiscSystems.Achievements.AchievementManager");
            s_instanceField = AccessTools.Field(achievementManagerType, "instance");
            s_getCurrentStatsMethod = AccessTools.Method(achievementManagerType, "GetCurrentStats");
            s_winStatsProperty = AccessTools.Property(achievementManagerType, "WinStats");
        }

        /// <summary>
        /// The current run's elapsed play time (same value the vanilla session stats show), or
        /// TimeSpan.Zero if called before AchievementManager exists.
        /// </summary>
        public static TimeSpan GetCurrentPlayTime()
        {
            object instance = s_instanceField.GetValue(null);
            if (instance == null)
            {
                return TimeSpan.Zero;
            }
            var stats = (PlayerStats)s_getCurrentStatsMethod.Invoke(instance, null);
            return stats.timeSpan;
        }

        /// <summary>
        /// The play time of the run that was just won - the exact value the vanilla post-ending
        /// stats screen (StatsScreen) shows, since it reads the same AchievementManager.WinStats.
        /// Only meaningful once AchievementManager.OnVictory has run; GetCurrentPlayTime is no use
        /// by then, since OnVictory also re-snapshots the stats it's measured against (it reads
        /// as ~0 right after a win). Null if called before AchievementManager exists.
        /// </summary>
        public static TimeSpan? GetWinPlayTime()
        {
            object instance = s_instanceField.GetValue(null);
            if (instance == null)
            {
                return null;
            }
            var stats = (PlayerStats)s_winStatsProperty.GetValue(instance);
            return stats.timeSpan;
        }
    }
}
