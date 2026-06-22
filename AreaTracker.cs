using System;
using System.Collections.Generic;
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

        private const string NoHistoryDisplayText = "No areas reached yet";

        private static Location[] s_sortedLocations = new Location[0];
        private static string s_currentLevelKey = string.Empty;
        private static int? s_lastResolvedStart;
        private static Location? s_lastExactArea;

        /// <summary>
        /// Toggled by AreaHistoryToggle in the pause menu; swaps GetDisplayText over to the
        /// visited-areas list instead of the current area's own info.
        /// </summary>
        public static bool ShowHistory { get; set; }

        public static void OnLevelStart()
        {
            s_currentLevelKey = LevelKeyResolver.GetCurrentLevelKey();
            Location[] locations = LocationSettingsAccessor.GetCurrentLocations();
            Array.Sort(locations, (a, b) => a.start.CompareTo(b.start));
            s_sortedLocations = locations;
            s_lastResolvedStart = null;
            s_lastExactArea = null;
            ShowHistory = false;

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
            bool isFirstArea = area.start == s_sortedLocations[0].start;
            AreaProgressStore.OnEnterArea(s_currentLevelKey, area.start, null, isFirstArea);
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

            // Only register area progress (order/attempts/cleared) once the player has actually
            // landed somewhere, not while merely passing through mid-air - same gate the engine
            // itself uses before updating its own current-location/new-area-discovered state.
            if (!PlayerGroundChecker.IsOnGround())
            {
                return;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;
            LocationResolver.Result resolved = LocationResolver.Resolve(s_sortedLocations, screenIndex1);

            if (resolved.IsExactMatch)
            {
                Location newExactArea = resolved.Area.Value;
                if (s_lastExactArea.HasValue && newExactArea.start != s_lastExactArea.Value.start)
                {
                    // Only count the previous area as "cleared" once the player has genuinely
                    // reached the specific area that follows it in sequence - not merely
                    // whichever area/screen they happen to be on next. A side path's screen
                    // numbers have no fixed relationship to the main route (they can be lower,
                    // higher, or reached via a warp to an arbitrary screen), so a plain "did the
                    // screen index move past this area's end" check flags side trips as clears.
                    Location? nextLocation = FindNextLocation(s_lastExactArea.Value);
                    if (nextLocation.HasValue && newExactArea.start == nextLocation.Value.start)
                    {
                        AreaProgressStore.MarkCleared(s_currentLevelKey, s_lastExactArea.Value.start);
                    }
                }
                s_lastExactArea = newExactArea;
            }

            if (resolved.Area == null)
            {
                s_lastResolvedStart = null;
                return;
            }

            int newStart = resolved.Area.Value.start;
            if (s_lastResolvedStart != newStart)
            {
                bool isFirstArea = newStart == s_sortedLocations[0].start;
                AreaProgressStore.OnEnterArea(s_currentLevelKey, newStart, s_lastResolvedStart, isFirstArea);
                s_lastResolvedStart = newStart;
            }
        }

        /// <summary>
        /// The Location immediately following p_area in start order, or null if p_area is the
        /// last one. Used to recognise genuine forward progress into the next area, as opposed
        /// to a detour into some other area or gap.
        /// </summary>
        private static Location? FindNextLocation(Location p_area)
        {
            for (int i = 0; i < s_sortedLocations.Length; i++)
            {
                if (s_sortedLocations[i].start == p_area.start)
                {
                    return (i + 1 < s_sortedLocations.Length) ? s_sortedLocations[i + 1] : (Location?)null;
                }
            }
            return null;
        }

        public static string GetDisplayText()
        {
            if (ShowHistory)
            {
                return GetHistoryDisplayText();
            }

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
            // Page numbers count from "unlock", not "start": when start < unlock (e.g. False
            // Kings Keep: start=10, unlock=11), screens before unlock belong to the previous
            // area as far as the exact-match resolver is concerned (see LocationResolver), so
            // they never reach this branch - current/total going negative for them is harmless.
            // When unlock == start (the common case) this is identical to the old formula.
            int current = screenIndex1 - area.unlock + 1;

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
                ? $"{name} {current}/{area.end - area.unlock + 1}"
                : $"{name} {current}/x";

            if (ModEntry.Settings.AttemptCounterEnabled)
            {
                int attempts = AreaProgressStore.GetAttemptCount(s_currentLevelKey, area.start);
                if (attempts > 0)
                {
                    // The game's bitmap MenuFont has no Japanese glyphs (non-ASCII characters
                    // render as a fallback glyph), so this stays plain ASCII.
                    text += $" (#{attempts})";
                }
            }

            return text;
        }

        /// <summary>
        /// One line per area that's been reached at least once, in first-visit order, each with
        /// its attempt count - e.g. "Bargainburg (#2)".
        /// </summary>
        private static string GetHistoryDisplayText()
        {
            List<AreaProgressStore.AreaSummary> areas = AreaProgressStore.GetVisitedAreas(s_currentLevelKey);
            if (areas.Count == 0)
            {
                return NoHistoryDisplayText;
            }

            string text = null;
            foreach (AreaProgressStore.AreaSummary area in areas)
            {
                string name = FindAreaName(area.Start) ?? area.Start.ToString();
                string line = $"{name} (#{area.AttemptCount})";
                text = (text == null) ? line : text + "\n" + line;
            }
            return text;
        }

        private static string FindAreaName(int start)
        {
            foreach (Location location in s_sortedLocations)
            {
                if (location.start == start)
                {
                    return language.ResourceManager.GetString(location.name) ?? location.name;
                }
            }
            return null;
        }
    }
}
