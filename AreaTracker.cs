using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
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

        /// <summary>Shown for all 3 Babe areas alike (see s_babeLocations).</summary>
        private const string BabeDisplayName = "Babe";

        /// <summary>
        /// Location.name of the synthetic Babe Locations, marking them apart from the level's own
        /// ones (IsBabe). Never displayed - BabeDisplayName is - and not something a level's
        /// location_settings.xml would plausibly contain.
        /// </summary>
        private const string BabeLocationName = "\u0001AreaInfoDisplayOnPause.Babe";

        /// <summary>The level's own Locations only (never the Babe ones), sorted by start.</summary>
        private static Location[] s_sortedLocations = new Location[0];
        private static string s_currentLevelKey = string.Empty;
        private static Location? s_lastExactArea;

        /// <summary>The screen s_lastExactArea was last exact-matched on.</summary>
        private static int s_lastExactScreenIndex1;

        /// <summary>
        /// The level's 3 "Babe" ending screens (see EndingScreensAccessor), with duplicates and
        /// unset (non-positive) values dropped. Kept regardless of BabeAsAreaEnabled, so turning
        /// it on mid-level can rebuild s_babeLocations without going back to the engine.
        /// </summary>
        private static int[] s_babeScreens = new int[0];

        /// <summary>
        /// While BabeAsAreaEnabled is on, one single-screen Location per Babe screen, each its own
        /// area in every respect (Order, attempts, lap time, cleared history), displayed as
        /// BabeDisplayName. ResolveArea checks these before the level's own Locations, so a Babe
        /// screen always resolves to its Babe area even though it sits inside (or on the start of)
        /// one of them. Empty while the setting is off: Babe screens then just belong to whatever
        /// Location they're in, with no special handling at all.
        ///
        /// Stored in AreaProgressStore under GetAreaKey's negative key, not the screen number
        /// itself, so a Babe area never shares an entry with a level Location starting on that
        /// same screen.
        /// </summary>
        private static Location[] s_babeLocations = new Location[0];

        /// <summary>The BabeAsAreaEnabled value s_babeLocations was last built for.</summary>
        private static bool s_babeLocationsEnabled;

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
        /// screenIndex1 as of the last OnUpdate call that didn't skip via the unchanged-screen
        /// guard below. Null right after a level (re)load so the very first call always runs.
        /// </summary>
        private static int? s_lastScreenIndex1;

        /// <summary>
        /// Toggled by the Detail Shortcut while paused (see PauseInfoFrame); swaps GetDisplayText
        /// over to the visited-areas breakdown instead of the current area's own info. Lasts one
        /// pause only - MenuFactoryPatches.SetPausePostfix clears it each time pause opens.
        /// </summary>
        public static bool ShowProgressionDetail { get; set; }

        public static void OnLevelStart()
        {
            s_currentLevelKey = LevelKeyResolver.GetCurrentLevelKey();
            Location[] locations = LocationSettingsAccessor.GetCurrentLocations();
            Array.Sort(locations, (a, b) => a.start.CompareTo(b.start));
            s_sortedLocations = locations;
            s_babeScreens = GetDistinctBabeScreens();
            BuildBabeLocations();
            ResetTransitionState();
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
            foreach (Location location in s_sortedLocations)
            {
                AreaProgressStore.RestoreClearedOnResume(s_currentLevelKey, GetAreaKey(location), location.end, screenIndex1);
            }
            foreach (Location location in s_babeLocations)
            {
                AreaProgressStore.RestoreClearedOnResume(s_currentLevelKey, GetAreaKey(location), location.end, screenIndex1);
            }

            LocationResolver.Result resolved = ResolveArea(screenIndex1);

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
            AreaProgressStore.OnEnterArea(s_currentLevelKey, GetAreaKey(area), area.start, null, false, PlayTimeAccessor.GetCurrentPlayTime());
            AreaProgressStore.UpdateBestProgress(s_currentLevelKey, GetAreaKey(area), screenIndex1);
            // Covers resuming a save sitting exactly on a Babe screen - OnUpdate's own check
            // wouldn't see this until the next frame's transition, if ever (it only fires on
            // landing, and the player may already have been standing there before the reload).
            MarkBabeContainingAreaCleared(area);
            s_lastExactArea = area;
            s_lastExactScreenIndex1 = screenIndex1;
        }

        public static void OnUpdate()
        {
            if (s_sortedLocations.Length == 0)
            {
                return;
            }
            EnsureBabeLocationsCurrent();

            // Checked before the (pricier, entity/component-lookup-based) ground check below: if
            // the screen hasn't changed since the last frame that actually ran past both checks,
            // nothing about the resolved area, attempt/clear/Babe checks, or best-progress
            // tracking could have changed either - they're all pure functions of screenIndex1 plus
            // state this same method already updated last time it ran. Skipping re-derives nothing
            // different, just for free, and (this being first) also skips the ground check itself
            // on every such frame.
            int screenIndex1 = Camera.CurrentScreenIndex1;
            if (screenIndex1 == s_lastScreenIndex1)
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
            ProcessScreen(screenIndex1);
        }

        /// <summary>
        /// Called right before the ending's clear record is built. The ending can start on a Babe
        /// screen the player never landed on (touching the Babe mid-jump), which OnUpdate's ground
        /// check would otherwise leave unregistered - and the Babe area would then be missing from
        /// the cleared history. Only catches up a Babe screen, so nothing else is registered off
        /// wherever the camera happens to be when the ending starts.
        /// </summary>
        private static void CatchUpBabeScreenOnClear()
        {
            if (s_sortedLocations.Length == 0)
            {
                return;
            }
            EnsureBabeLocationsCurrent();
            int screenIndex1 = Camera.CurrentScreenIndex1;
            if (screenIndex1 != s_lastScreenIndex1 && TryGetBabeLocation(screenIndex1, out _))
            {
                ProcessScreen(screenIndex1);
            }
        }

        private static void ProcessScreen(int screenIndex1)
        {
            s_lastScreenIndex1 = screenIndex1;

            LocationResolver.Result resolved = ResolveArea(screenIndex1);

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
            int newKey = GetAreaKey(newExactArea);
            bool isFirstArea = newKey == GetAreaKey(s_sortedLocations[0]);
            bool areaChanged = !s_lastExactArea.HasValue || newKey != GetAreaKey(s_lastExactArea.Value);
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
                    if (nextLocation.HasValue && newKey == GetAreaKey(nextLocation.Value))
                    {
                        AreaProgressStore.MarkCleared(s_currentLevelKey, GetAreaKey(s_lastExactArea.Value));
                    }
                }

                // Climb-or-fall is normally judged by the two areas' starts (see OnEnterArea). A
                // Babe area usually sits inside another area, though, so the containing area's
                // start says nothing about which side of the Babe the player was on - e.g. falling
                // from the containing area's last page onto the Babe below it would look like a
                // climb. Whenever a Babe is on either side, the actual screens are compared instead.
                int? previousPosition = null;
                int newPosition = newExactArea.start;
                if (s_lastExactArea.HasValue)
                {
                    bool babeInvolved = IsBabe(s_lastExactArea.Value) || IsBabe(newExactArea);
                    previousPosition = babeInvolved ? s_lastExactScreenIndex1 : s_lastExactArea.Value.start;
                    newPosition = babeInvolved ? screenIndex1 : newExactArea.start;
                }
                AreaProgressStore.OnEnterArea(s_currentLevelKey, newKey, newPosition, previousPosition, returnedAfterLeaving, PlayTimeAccessor.GetCurrentPlayTime());

                if (s_lastExactArea.HasValue)
                {
                    // Warp-safe supplement to the FindNextLocation check above - see
                    // AreaProgressStore.MarkClearedIfOrderSequential for why this is needed (a
                    // warp's destination isn't necessarily "next by start" even when it genuinely
                    // is the next area visited) and why it's safe against side paths. Placed after
                    // OnEnterArea so newExactArea's Order is already registered if this is the
                    // first time it's ever been seen. Uses the area's last screen of its own
                    // (GetLastAreaScreen), not its raw end: a Babe on the end screen means
                    // BestScreenIndex can never reach that end, so a warp from the area's real
                    // last page would otherwise never count as clearing it.
                    AreaProgressStore.MarkClearedIfOrderSequential(s_currentLevelKey, GetAreaKey(s_lastExactArea.Value), GetLastAreaScreen(s_lastExactArea.Value), newKey);
                }
            }

            s_lastExactArea = newExactArea;
            s_lastExactScreenIndex1 = screenIndex1;
            s_awayFromLastArea = false;
            AreaProgressStore.UpdateBestProgress(s_currentLevelKey, newKey, screenIndex1);

            MarkBabeContainingAreaCleared(newExactArea);
        }

        /// <summary>
        /// If area is a Babe area, marks the level Location its screen sits in as cleared:
        /// reaching a Babe ending screen means the area it sits in has, by definition, been fully
        /// played through - the same as genuinely reaching the next area in sequence. Does nothing
        /// if that Location was never registered (MarkCleared is a no-op then), e.g. when a warp
        /// skipped it entirely.
        /// </summary>
        private static void MarkBabeContainingAreaCleared(Location area)
        {
            if (!IsBabe(area))
            {
                return;
            }
            LocationResolver.Result containing = LocationResolver.Resolve(s_sortedLocations, area.start);
            if (containing.IsExactMatch)
            {
                AreaProgressStore.MarkCleared(s_currentLevelKey, GetAreaKey(containing.Area.Value));
            }
        }

        /// <summary>
        /// The Location immediately following p_area in start order, or null if p_area is the
        /// last one. Used to recognise genuine forward progress into the next area, as opposed
        /// to a detour into some other area or gap.
        ///
        /// Only ever looks among the level's own Locations, so a Babe area sitting inside or
        /// right after p_area never counts as p_area's "next" - p_area is still only cleared by
        /// reaching the level Location after it (or the Babe itself - see
        /// MarkBabeContainingAreaCleared). A Babe on p_area's last screen is taken off its page
        /// count separately, in GetLastAreaScreen. For a Babe area itself, the "next" is whatever level Location the screen
        /// right above it exact-matches: climbing out of the top of a Babe screen is finishing it.
        /// </summary>
        private static Location? FindNextLocation(Location p_area)
        {
            if (IsBabe(p_area))
            {
                LocationResolver.Result above = LocationResolver.Resolve(s_sortedLocations, p_area.start + 1);
                return above.IsExactMatch ? above.Area : null;
            }
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
        /// The last screen that actually resolves to area itself. Normally area.end, except:
        /// - when the next Location's range starts at or before area.end (the two share a boundary
        ///   screen, e.g. Redcrown Woods end=6 / Colossal Drain start=6, unlock=6): once the next
        ///   Location's own unlock is reached, the exact-match resolver hands that shared screen
        ///   to it instead (see LocationResolver), so area's last real screen is one before that;
        /// - when a Babe area (while enabled) sits on area's last screen(s): ResolveArea hands
        ///   that screen to the Babe, so it isn't one of area's pages either. E.g. A = 1-10 with
        ///   the Babe on 10 is a 9-page area plus the Babe. A Babe in the middle of area doesn't
        ///   change this - area's pages keep their numbers around it.
        /// A Babe area itself is just its own single screen.
        /// </summary>
        private static int GetLastAreaScreen(Location area)
        {
            if (IsBabe(area))
            {
                return area.end;
            }
            int lastScreen = area.end;
            Location? nextLocation = FindNextLocation(area);
            if (nextLocation.HasValue && nextLocation.Value.start <= area.end)
            {
                lastScreen = Math.Min(lastScreen, nextLocation.Value.unlock - 1);
            }
            while (lastScreen > area.unlock && TryGetBabeLocation(lastScreen, out _))
            {
                lastScreen--;
            }
            return lastScreen;
        }

        /// <summary>Page count of area: unlock through GetLastAreaScreen.</summary>
        private static int GetEffectiveTotal(Location area)
        {
            return GetLastAreaScreen(area) - area.unlock + 1;
        }

        public static string GetDisplayText()
        {
            EnsureBabeLocationsCurrent();
            if (ShowProgressionDetail)
            {
                // Unlike the Show Progression Detail popup, the pause-info box can't be split into
                // pages, so the oldest areas are dropped instead once it would leave the screen.
                return PagedTextMenu.TruncateToHeight(GetProgressionDetailText(), MenuFactoryPatches.PauseInfoMaxTextHeight);
            }

            return AppendPersonalBest(GetCurrentAreaDisplayText());
        }

        private static int[] GetDistinctBabeScreens()
        {
            var screens = new List<int>();
            foreach (int screen in EndingScreensAccessor.GetBabeScreens())
            {
                if (screen > 0 && !screens.Contains(screen))
                {
                    screens.Add(screen);
                }
            }
            return screens.ToArray();
        }

        private static void BuildBabeLocations()
        {
            s_babeLocationsEnabled = ModEntry.Settings.BabeAsAreaEnabled;
            if (!s_babeLocationsEnabled)
            {
                s_babeLocations = new Location[0];
                return;
            }
            s_babeLocations = new Location[s_babeScreens.Length];
            for (int i = 0; i < s_babeScreens.Length; i++)
            {
                s_babeLocations[i] = new Location
                {
                    start = s_babeScreens[i],
                    end = s_babeScreens[i],
                    unlock = s_babeScreens[i],
                    name = BabeLocationName,
                };
            }
        }

        /// <summary>
        /// Picks up a BabeAsAreaEnabled change made since the Babe areas were last built (its
        /// toggle, or Reset to Default). The area the player was last in may stop existing (or
        /// start existing) with the change, so the transition state is dropped too: the next
        /// OnUpdate then re-registers the current screen as a quiet, no-attempt arrival - the
        /// same as a level start does.
        /// </summary>
        private static void EnsureBabeLocationsCurrent()
        {
            if (s_babeLocationsEnabled == ModEntry.Settings.BabeAsAreaEnabled)
            {
                return;
            }
            BuildBabeLocations();
            ResetTransitionState();
        }

        private static void ResetTransitionState()
        {
            s_lastExactArea = null;
            s_lastExactScreenIndex1 = 0;
            s_awayFromLastArea = false;
            s_lastScreenIndex1 = null;
        }

        private static bool IsBabe(Location location)
        {
            return location.name == BabeLocationName;
        }

        /// <summary>
        /// The key an area's progress is stored under in AreaProgressStore (and the cleared
        /// history): a level Location's own start, or minus the screen for a Babe area. Starts
        /// are screen numbers, which count from 1, so the two ranges can never collide - not even
        /// when a level Location starts on the Babe screen itself.
        /// </summary>
        private static int GetAreaKey(Location location)
        {
            return IsBabe(location) ? -location.start : location.start;
        }

        private static bool IsBabeKey(int key)
        {
            return key < 0;
        }

        private static bool TryGetBabeLocation(int screenIndex1, out Location babe)
        {
            foreach (Location location in s_babeLocations)
            {
                if (location.start == screenIndex1)
                {
                    babe = location;
                    return true;
                }
            }
            babe = default(Location);
            return false;
        }

        /// <summary>
        /// LocationResolver.Resolve against the level's own Locations, except that a Babe area
        /// (while enabled) always wins its own screen outright.
        /// </summary>
        private static LocationResolver.Result ResolveArea(int screenIndex1)
        {
            if (TryGetBabeLocation(screenIndex1, out Location babe))
            {
                return new LocationResolver.Result(babe, true);
            }
            return LocationResolver.Resolve(s_sortedLocations, screenIndex1);
        }

        private static string GetCurrentAreaDisplayText()
        {
            if (s_sortedLocations.Length == 0)
            {
                return string.Empty;
            }

            int screenIndex1 = Camera.CurrentScreenIndex1;

            LocationResolver.Result resolved = ResolveArea(screenIndex1);
            if (resolved.Area == null)
            {
                return string.Empty;
            }

            if (!resolved.IsExactMatch)
            {
                return PassageDisplayText;
            }

            Location area = resolved.Area.Value;
            if (IsBabe(area))
            {
                // A single screen, so no page number - just the name and the attempt count.
                return AppendAttemptCount(BabeDisplayName, GetAreaKey(area));
            }

            string name = GetAreaDisplayName(area.name);
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

            return AppendAttemptCount(text, GetAreaKey(area));
        }

        private static string AppendAttemptCount(string text, int areaKey)
        {
            if (ModEntry.Settings.AttemptCounterEnabled)
            {
                int attempts = AreaProgressStore.GetAttemptCount(s_currentLevelKey, areaKey);
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
        ///
        /// Just "Babe" when the PB area is a Babe area (a single screen, so no page). That's
        /// decided by the stored key alone, not by BabeAsAreaEnabled, so a Babe area reached
        /// before the setting was turned off still reads as one.
        /// </summary>
        private static string GetPersonalBestText()
        {
            AreaProgressStore.AreaSummary? pb = AreaProgressStore.GetPersonalBest(s_currentLevelKey);
            if (!pb.HasValue || pb.Value.BestScreenIndex == 0)
            {
                return null;
            }
            if (IsBabeKey(pb.Value.Start))
            {
                return BabeDisplayName;
            }
            Location? area = FindLocation(pb.Value.Start);
            if (!area.HasValue)
            {
                return null;
            }
            string name = GetAreaDisplayName(area.Value.name);
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
            string pb = GetPersonalBestText();
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
        internal static string GetProgressionDetailText()
        {
            EnsureBabeLocationsCurrent();
            List<AreaProgressStore.AreaSummary> areas = AreaProgressStore.GetVisitedAreas(s_currentLevelKey);
            if (areas.Count == 0)
            {
                return NoProgressionDetailText;
            }

            string pb = GetPersonalBestText();
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
                string line = FormatProgressionDetailLine(name, area.AttemptCount, area.LapTime);
                text = (text == null) ? line : text + "\n" + line;
            }
            return text;
        }

        /// <summary>
        /// One per-area line of the Progression Detail breakdown. Shared with ClearedMapsMenu so
        /// a cleared map's history reads exactly like the live in-game breakdown did.
        /// </summary>
        public static string FormatProgressionDetailLine(string name, int attemptCount, TimeSpan lapTime)
        {
            return $"{name} (#{attemptCount}) {FormatLapTime(lapTime)}";
        }

        private static string FormatLapTime(TimeSpan lapTime)
        {
            return $"{(int)lapTime.TotalHours}h {lapTime.Minutes}m {lapTime.Seconds}s";
        }

        /// <summary>
        /// Snapshot of the current level's progress for ClearedMapHistoryStore, taken the moment
        /// the ending starts (see GameCompletePatches) - before the game's own clear wipes the
        /// in-progress data. Area/level names are resolved now, while this level's Locations are
        /// still loaded, since the title screen (where the history is viewed) has none.
        /// Must run after AchievementManager.OnVictory (true for the ending, which only starts
        /// once GameLoop has called it), since that's what fills in the clear time.
        /// </summary>
        public static ClearedMapHistoryStore.ClearedRecord BuildClearedRecord()
        {
            CatchUpBabeScreenOnClear();
            var record = new ClearedMapHistoryStore.ClearedRecord
            {
                LevelKey = s_currentLevelKey,
                LevelName = LevelKeyResolver.GetCurrentLevelTitle(),
                ClearedAt = DateTime.Now,
                ClearTime = PlayTimeAccessor.GetWinPlayTime(),
            };
            foreach (AreaProgressStore.AreaSummary area in AreaProgressStore.GetVisitedAreas(s_currentLevelKey))
            {
                record.Areas.Add(new ClearedMapHistoryStore.ClearedArea
                {
                    Start = area.Start,
                    Order = area.Order,
                    AttemptCount = area.AttemptCount,
                    HasFullyCleared = area.HasFullyCleared,
                    LapTime = area.LapTime,
                    BestScreenIndex = area.BestScreenIndex,
                    Name = FindAreaName(area.Start),
                });
            }
            return record;
        }

        /// <summary>Display name for an AreaProgressStore key (see GetAreaKey).</summary>
        private static string FindAreaName(int start)
        {
            if (IsBabeKey(start))
            {
                return BabeDisplayName;
            }
            Location? location = FindLocation(start);
            if (!location.HasValue)
            {
                return null;
            }
            return GetAreaDisplayName(location.Value.name);
        }

        // Some custom levels' location names embed markup tags meant for other text-customisation
        // mods (e.g. "{color=\"#eb5886\"}Subspace Spire" for MoreTextOptions' colour markers).
        // Those mods' own renderers expect at most one such tag per drawn string; this mod's
        // display text can embed the same area name twice in one string (the "PB: ..." line and
        // the current-area line), which doubles up the tag and breaks their line-break handling
        // (observed on Workshop level 3397533142, caused by MoreTextOptions). Stripping any such
        // tag before using a name avoids the interaction entirely, regardless of which other mod
        // a level's author had in mind when writing it.
        private static readonly Regex MarkupTagRegex = new Regex("\\{[a-zA-Z]+=\"[^\"]*\"\\}");

        private static string GetAreaDisplayName(string nameKey)
        {
            string name = language.ResourceManager.GetString(nameKey) ?? nameKey;
            return MarkupTagRegex.Replace(name, string.Empty);
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
