using System;
using JumpKing;
using JumpKing.MiscSystems.LocationText;
using LanguageJK;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Coordinates per-level state: which locations are defined, which one is currently
    /// resolved, and the progress bookkeeping (order/attempts/cleared) that depends on it.
    /// OnLevelStart resets state for a (re)started playthrough; OnUpdate runs every unpaused
    /// frame to track attempts/clears; GetDisplayText is pulled by the pause-screen UI.
    /// </summary>
    internal static class AreaTracker
    {
        /// <summary>
        /// Shown instead of the area name/page/attempt count while in a "gap" between two
        /// defined Location ranges (pattern B), since none of those numbers refer to anything
        /// meaningful there.
        /// </summary>
        private const string PassageDisplayText = "On the way...";

        private static Location[] s_sortedLocations = new Location[0];
        private static string s_currentLevelKey = string.Empty;
        private static int? s_lastResolvedStart;
        private static Location? s_lastExactArea;

        public static void OnLevelStart()
        {
            s_currentLevelKey = LevelKeyResolver.GetCurrentLevelKey();
            Location[] locations = LocationSettingsAccessor.GetCurrentLocations();
            Array.Sort(locations, (a, b) => a.start.CompareTo(b.start));
            s_sortedLocations = locations;
            s_lastResolvedStart = null;
            s_lastExactArea = null;

            if (s_sortedLocations.Length == 0)
            {
                return;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;
            LocationResolver.Result resolved = LocationResolver.Resolve(s_sortedLocations, screenIndex1);
            if (resolved.Area == null)
            {
                return;
            }

            Location area = resolved.Area.Value;
            AreaProgressStore.OnEnterArea(s_currentLevelKey, area.start, null);
            AreaProgressStore.RestoreClearedOnResume(s_currentLevelKey, s_sortedLocations, screenIndex1);
            s_lastResolvedStart = area.start;
            if (resolved.IsExactMatch)
            {
                s_lastExactArea = area;
            }
        }

        public static void OnUpdate()
        {
            if (s_sortedLocations.Length == 0)
            {
                return;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;
            LocationResolver.Result resolved = LocationResolver.Resolve(s_sortedLocations, screenIndex1);

            if (resolved.IsExactMatch)
            {
                s_lastExactArea = resolved.Area;
            }
            else if (s_lastExactArea.HasValue && screenIndex1 > s_lastExactArea.Value.end)
            {
                AreaProgressStore.MarkCleared(s_currentLevelKey, s_lastExactArea.Value.start);
            }

            if (resolved.Area == null)
            {
                s_lastResolvedStart = null;
                return;
            }

            int newStart = resolved.Area.Value.start;
            if (s_lastResolvedStart != newStart)
            {
                AreaProgressStore.OnEnterArea(s_currentLevelKey, newStart, s_lastResolvedStart);
                s_lastResolvedStart = newStart;
            }
        }

        public static string GetDisplayText()
        {
            if (s_sortedLocations.Length == 0)
            {
                return string.Empty;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;
            LocationResolver.Result resolved = LocationResolver.Resolve(s_sortedLocations, screenIndex1);
            if (resolved.Area == null)
            {
                return string.Empty;
            }

            if (!resolved.IsExactMatch)
            {
                return PassageDisplayText;
            }

            Location area = resolved.Area.Value;
            string name = language.ResourceManager.GetString(area.name) ?? area.name;
            int current = screenIndex1 - area.start + 1;

            bool showTotal;
            switch (ModEntry.Settings.DisplayMode)
            {
                case TotalDisplayMode.Always:
                    showTotal = true;
                    break;
                case TotalDisplayMode.AfterClear:
                    showTotal = AreaProgressStore.IsCleared(s_currentLevelKey, area.start);
                    break;
                default:
                    showTotal = false;
                    break;
            }

            string text = showTotal
                ? $"{name} {current}/{area.end - area.start + 1}"
                : $"{name} {current}";

            if (ModEntry.Settings.AttemptCounterEnabled)
            {
                int attempts = AreaProgressStore.GetAttemptCount(s_currentLevelKey, area.start);
                if (attempts > 0)
                {
                    // The game's bitmap MenuFont has no Japanese glyphs (non-ASCII characters
                    // render as a fallback glyph), so this stays plain ASCII.
                    text += $" (x{attempts})";
                }
            }

            return text;
        }
    }
}
