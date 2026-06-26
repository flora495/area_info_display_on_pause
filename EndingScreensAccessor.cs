using System;
using System.Reflection;
using HarmonyLib;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// JumpKing.GameManager.MultiEnding.EndingManager is internal, so its static instance
    /// property and GetWinScreens0() method are read via reflection. It already resolves the 3
    /// "Babe" ending screens for whichever level is loaded - vanilla's hardcoded 42/99/153
    /// (matching settings/babe.xml) or a custom level's level_settings.xml
    /// ending_screen/ending_screen_second/ending_screen_third - so this mod doesn't need to parse
    /// either source itself.
    /// </summary>
    internal static class EndingScreensAccessor
    {
        private static PropertyInfo s_instanceProperty;
        private static MethodInfo s_getWinScreens0Method;

        public static void Initialize()
        {
            Type endingManagerType = AccessTools.TypeByName("JumpKing.GameManager.MultiEnding.EndingManager");
            s_instanceProperty = AccessTools.Property(endingManagerType, "instance");
            s_getWinScreens0Method = AccessTools.Method(endingManagerType, "GetWinScreens0");
        }

        /// <summary>
        /// The 3 "Babe" milestone screens for the current level, in Normal/New Babe Plus/Owl
        /// ending order, as screenIndex1 (1-based, matching this mod's Location.start/
        /// Camera.CurrentScreenIndex1 convention) rather than the engine's own 0-based
        /// Camera.CurrentScreen. Empty if EndingManager hasn't been set up yet (e.g. called too
        /// early).
        /// </summary>
        public static int[] GetBabeScreens()
        {
            object instance = s_instanceProperty.GetValue(null);
            if (instance == null)
            {
                return new int[0];
            }
            var screens0 = (int[])s_getWinScreens0Method.Invoke(instance, null);
            var result = new int[screens0.Length];
            for (int i = 0; i < screens0.Length; i++)
            {
                result[i] = screens0[i] + 1;
            }
            return result;
        }
    }
}
