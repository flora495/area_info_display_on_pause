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

        private const string NoProgressionDetailText = "No areas reached yet";

        private static Location[] s_sortedLocations = new Location[0];
        private static string s_currentLevelKey = string.Empty;
        private static Location? s_lastExactArea;

        /// <summary>
        /// The level's 3 "Babe" ending screens (see EndingScreensAccessor), in Normal/New Babe
        /// Plus/Owl order. Purely a display-time override (GetBabeDisplayText) - standing exactly
        /// on one of these screens shows "Babe N" instead of the normal area/PB text, but doesn't
        /// touch AreaProgressStore at all, so the real Location underneath keeps tracking normally
        /// and the override disappears the moment the player moves off that one screen.
        /// </summary>
        private static int[] s_babeScreens = new int[0];

        /// <summary>
        /// Set whenever the player isn't exact-matched to s_lastExactArea (a "gap", or briefly a
        /// different area) since it was last set. Lets OnUpdate recognise "left, then came back to
        /// the very same area" as a fresh attempt too, not just "climbed into a different, later
        /// area" - see the returnedAfterLeaving check below.
        /// </summary>
        private static bool s_awayFromLastArea;

        /// <summary>Whether the player was on the level's literal first screen last frame; see isOnFirstScreen below.</summary>
        private static bool s_wasOnFirstScreen;

        /// <summary>
        /// Toggled by ProgressionDetailToggle in the pause menu; swaps GetDisplayText over to the
        /// visited-areas breakdown instead of the current area's own info.
        /// </summary>
        public static bool ShowProgressionDetail { get; set; }

        public static void OnLevelStart()
        {
            s_currentLevelKey = LevelKeyResolver.GetCurrentLevelKey();
            Location[] locations = LocationSettingsAccessor.GetCurrentLocations();
            Array.Sort(locations, (a, b) => a.start.CompareTo(b.start));
            s_sortedLocations = locations;
            s_babeScreens = EndingScreensAccessor.GetBabeScreens();
            s_lastExactArea = null;
            s_awayFromLastArea = false;
            ShowProgressionDetail = false;

            if (s_sortedLocations.Length == 0)
            {
                return;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;
            // Initialised from the resume screen (not unconditionally false) so a save that just
            // happens to resume sitting on the literal first screen doesn't look like a fresh
            // "landed here" transition to OnUpdate's isOnFirstScreen check on its very next frame.
            s_wasOnFirstScreen = screenIndex1 == s_sortedLocations[0].start;
            AreaProgressStore.RestoreClearedOnResume(s_currentLevelKey, s_sortedLocations, screenIndex1);

            LocationResolver.Result resolved = LocationResolver.Resolve(s_sortedLocations, screenIndex1);

            // Covers resuming a save sitting exactly on a Babe screen - OnUpdate's own check
            // wouldn't see this until the next frame's transition, if ever (it only fires on
            // landing, and the player may already have been standing there before the reload).
            bool isOnBabeScreen = GetBabeDisplayText(screenIndex1) != null;
            if (isOnBabeScreen)
            {
                AreaProgressStore.MarkBabeReached(s_currentLevelKey);
            }

            if (!resolved.IsExactMatch)
            {
                // Started in a gap ("on the way") - nothing genuine to register yet. Registering
                // off a gap's best-guess fallback Location (see LocationResolver's pattern B) can
                // pick an area completely unrelated to the path actually taken, on custom levels
                // where some other Location's start just happens to be the closest one below the
                // current screen.
                return;
            }

            Location area = resolved.Area.Value;
            AreaProgressStore.OnEnterArea(s_currentLevelKey, area.start, null, false, PlayTimeAccessor.GetCurrentPlayTime());
            AreaProgressStore.UpdateBestProgress(s_currentLevelKey, area.start, screenIndex1);
            if (isOnBabeScreen)
            {
                // Only after OnEnterArea above, so area is guaranteed to already be registered -
                // MarkCleared is a no-op against an area with no entry yet.
                AreaProgressStore.MarkCleared(s_currentLevelKey, area.start);
            }
            s_lastExactArea = area;
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

            bool isOnBabeScreen = GetBabeDisplayText(screenIndex1) != null;
            if (isOnBabeScreen)
            {
                AreaProgressStore.MarkBabeReached(s_currentLevelKey);
            }

            // The very first area has no area below it to register a "left, then came back"
            // transition through in the usual sense - the only thing below it is the literal
            // bottom of the level (its own start screen, which for areas where start < unlock is
            // itself a gap, never exact-matched as the area itself - see LocationResolver). So this
            // is tracked independently of exact-match resolution, as its own one-shot transition.
            bool isOnFirstScreen = screenIndex1 == s_sortedLocations[0].start;
            if (isOnFirstScreen && !s_wasOnFirstScreen)
            {
                AreaProgressStore.BumpAttempt(s_currentLevelKey, s_sortedLocations[0].start, PlayTimeAccessor.GetCurrentPlayTime());
            }
            s_wasOnFirstScreen = isOnFirstScreen;

            if (!resolved.IsExactMatch)
            {
                // "On the way" - never registers/clears/tracks anything. A gap's resolved
                // Location is only a best-guess fallback (closest preceding Location by start
                // among *all* of them - see LocationResolver), which can be a totally unrelated
                // area on custom levels, not necessarily the one the player actually just left.
                // Still marks that the player has left whatever area they were last confirmed in,
                // so returning to that *same* area later is recognised as a fresh attempt too.
                s_awayFromLastArea = true;
                return;
            }

            Location newExactArea = resolved.Area.Value;
            bool isFirstArea = newExactArea.start == s_sortedLocations[0].start;
            bool areaChanged = !s_lastExactArea.HasValue || newExactArea.start != s_lastExactArea.Value.start;
            // Returning to the very same area after being away (a gap, or a side trip) is just as
            // much a fresh attempt as climbing into a different, later area - except for the first
            // area, which uses its own isOnFirstScreen-based rule above instead.
            bool returnedAfterLeaving = !areaChanged && s_awayFromLastArea && !isFirstArea;

            if (areaChanged || returnedAfterLeaving)
            {
                if (s_lastExactArea.HasValue)
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

                int? previousStart = s_lastExactArea?.start;
                AreaProgressStore.OnEnterArea(s_currentLevelKey, newExactArea.start, previousStart, returnedAfterLeaving, PlayTimeAccessor.GetCurrentPlayTime());
            }

            s_lastExactArea = newExactArea;
            s_awayFromLastArea = false;
            AreaProgressStore.UpdateBestProgress(s_currentLevelKey, newExactArea.start, screenIndex1);

            if (isOnBabeScreen)
            {
                // Only after OnEnterArea above (inside the areaChanged/returnedAfterLeaving branch,
                // or from an earlier visit), so newExactArea is guaranteed to already be registered
                // - MarkCleared is a no-op against an area with no entry yet. Reaching a Babe ending
                // screen means the area it sits in has, by definition, been fully played through -
                // mark it cleared too, the same as genuinely reaching the next area in sequence.
                AreaProgressStore.MarkCleared(s_currentLevelKey, newExactArea.start);
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

        /// <summary>
        /// area.end - area.unlock + 1, except when the next Location's range starts at or before
        /// area.end (the two share a boundary screen, e.g. Redcrown Woods end=6 / Colossal Drain
        /// start=6, unlock=6): once the next Location's own unlock is reached, the exact-match
        /// resolver hands that shared screen to it instead (see LocationResolver), so area's last
        /// real screen is one before that, not its own end.
        /// </summary>
        private static int GetEffectiveTotal(Location area)
        {
            int effectiveEnd = area.end;
            Location? nextLocation = FindNextLocation(area);
            if (nextLocation.HasValue && nextLocation.Value.start <= area.end)
            {
                effectiveEnd = Math.Min(effectiveEnd, nextLocation.Value.unlock - 1);
            }
            return effectiveEnd - area.unlock + 1;
        }

        public static string GetDisplayText()
        {
            if (ShowProgressionDetail)
            {
                return GetProgressionDetailText();
            }

            return AppendPersonalBest(GetCurrentAreaDisplayText());
        }

        /// <summary>
        /// "Babe" if screenIndex1 is exactly one of the level's 3 ending screens (see
        /// s_babeScreens; the 3 aren't distinguished from each other), overriding whatever the
        /// normal Location-based resolution would otherwise show for that single screen. Null
        /// everywhere else.
        /// </summary>
        private static string GetBabeDisplayText(int screenIndex1)
        {
            for (int i = 0; i < s_babeScreens.Length; i++)
            {
                if (s_babeScreens[i] == screenIndex1)
                {
                    return "Babe";
                }
            }
            return null;
        }

        private static string GetCurrentAreaDisplayText()
        {
            if (s_sortedLocations.Length == 0)
            {
                return string.Empty;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;

            string babeText = GetBabeDisplayText(screenIndex1);
            if (babeText != null)
            {
                return babeText;
            }

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
                ? $"{name} {current}/{GetEffectiveTotal(area)}"
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
        /// "AreaName N" for the PB area (the most recently first-discovered one, by Order - see
        /// AreaProgressStore.GetPersonalBest for why Order rather than raw screen number) and the
        /// deepest page ever reached within it - e.g. "Colossal Drain 4" if the player got to page
        /// 4 of it but never reached page 5. Null if nothing's been reached yet.
        /// </summary>
        private static string GetPersonalBestText()
        {
            AreaProgressStore.AreaSummary? pb = AreaProgressStore.GetPersonalBest(s_currentLevelKey);
            if (!pb.HasValue || pb.Value.BestScreenIndex == 0)
            {
                return null;
            }
            Location? area = FindLocation(pb.Value.Start);
            if (!area.HasValue)
            {
                return null;
            }
            string name = language.ResourceManager.GetString(area.Value.name) ?? area.Value.name;
            int page = pb.Value.BestScreenIndex - area.Value.unlock + 1;
            return $"{name} {page}";
        }

        /// <summary>
        /// Appends a "PB: <area> <page>" line for the furthest point reached so far, when
        /// PersonalBestEnabled is on and at least one area has been reached. Shown on top of
        /// every other display state (current area, "On the way...", empty) since it's meant to
        /// stay visible regardless of where the player currently is.
        /// </summary>
        private static string AppendPersonalBest(string text)
        {
            if (!ModEntry.Settings.PersonalBestEnabled)
            {
                return text;
            }
            // Once any Babe screen has ever been reached this playthrough, it's the deepest
            // possible point - the PB line keeps showing "Babe" from then on rather than reverting
            // to whatever real area the player is currently standing in (unlike the current-area
            // line, which always reflects the live screen - see GetBabeDisplayText/HasReachedBabe).
            string pb = AreaProgressStore.HasReachedBabe(s_currentLevelKey) ? "Babe" : GetPersonalBestText();
            if (pb == null)
            {
                return text;
            }
            string pbLine = $"PB: {pb}";
            return string.IsNullOrEmpty(text) ? pbLine : pbLine + "\n" + text;
        }

        /// <summary>
        /// A "pb: ..." summary line, then a "current: ..." line with the same info the normal
        /// display would show, then one line per area that's been reached at least once, each
        /// with its attempt count and the play time it was first reached at. Areas are listed
        /// most-recently-discovered first (the first area at the bottom).
        /// </summary>
        private static string GetProgressionDetailText()
        {
            List<AreaProgressStore.AreaSummary> areas = AreaProgressStore.GetVisitedAreas(s_currentLevelKey);
            if (areas.Count == 0)
            {
                return NoProgressionDetailText;
            }

            string pb = AreaProgressStore.HasReachedBabe(s_currentLevelKey) ? "Babe" : GetPersonalBestText();
            string text = pb != null ? $"pb: {pb}" : null;

            string current = GetCurrentAreaDisplayText();
            if (!string.IsNullOrEmpty(current))
            {
                string currentLine = $"current: {current}";
                text = (text == null) ? currentLine : text + "\n" + currentLine;
            }

            for (int i = areas.Count - 1; i >= 0; i--)
            {
                AreaProgressStore.AreaSummary area = areas[i];
                string name = FindAreaName(area.Start) ?? area.Start.ToString();
                string line = $"{name} (#{area.AttemptCount}) {FormatLapTime(area.LapTime)}";
                text = (text == null) ? line : text + "\n" + line;
            }
            return text;
        }

        private static string FormatLapTime(TimeSpan lapTime)
        {
            return $"{(int)lapTime.TotalHours}h {lapTime.Minutes}m {lapTime.Seconds}s";
        }

        private static string FindAreaName(int start)
        {
            Location? location = FindLocation(start);
            if (!location.HasValue)
            {
                return null;
            }
            return language.ResourceManager.GetString(location.Value.name) ?? location.Value.name;
        }

        private static Location? FindLocation(int start)
        {
            foreach (Location location in s_sortedLocations)
            {
                if (location.start == start)
                {
                    return location;
                }
            }
            return null;
        }
    }
}
