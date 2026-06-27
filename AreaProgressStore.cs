using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using JumpKing.MiscSystems.LocationText;

namespace AreaInfoDisplayOnPause
{
    /// <summary>
    /// Per-area progress (first-visit order, attempt count, whether the area has been fully
    /// cleared at least once), keyed by level and by the area's start screen. Persisted to its
    /// own XML file kept in sync with the game's own save lifecycle via SaveLubePatches.
    /// </summary>
    internal static class AreaProgressStore
    {
        private sealed class AreaEntry
        {
            public int Order;
            public int AttemptCount;
            public bool HasFullyCleared;

            /// <summary>Elapsed play time at the moment this area was first reached.</summary>
            public TimeSpan LapTime;

            /// <summary>Deepest exact-matched screen ever reached within this specific area.</summary>
            public int BestScreenIndex;
        }

        private sealed class LevelEntry
        {
            public readonly Dictionary<int, AreaEntry> Areas = new Dictionary<int, AreaEntry>();
            public int NextOrder;

            /// <summary>
            /// Whether any of the level's 3 "Babe" ending screens has ever been reached this
            /// playthrough. Sticky for the rest of the playthrough once set (persisted, not reset
            /// by leaving the screen) - reaching a Babe ending is the deepest possible point, so
            /// PB display should keep showing "Babe" from then on rather than reverting to
            /// whatever real area the player happens to be standing in afterwards.
            /// </summary>
            public bool HasReachedBabe;
        }

        // SaveLube.SaveCombinedSaveFile() (and so AreaProgressStore.Save, via SaveLubePatches)
        // runs on the game's own dedicated SaveManager thread, not the main thread - but
        // AreaTracker.OnUpdate (which mutates s_levels through OnEnterArea/MarkCleared) runs on
        // the main thread every frame. Without locking, Save's enumeration of s_levels can race
        // against those mutations; SaveManager wraps the call in a try/catch that just logs to
        // the crash log, so a race here silently drops that save instead of crashing - exactly
        // the kind of "sometimes doesn't persist" symptom this was causing.
        private static readonly object s_lock = new object();

        private static Dictionary<string, LevelEntry> s_levels = new Dictionary<string, LevelEntry>();
        private static bool s_dirty;

        public static void Load(string path)
        {
            lock (s_lock)
            {
                LoadInternal(path);
            }
        }

        /// <summary>
        /// Same as Load, but also marks the result dirty so the next regular autosave (the
        /// SaveLube postfix calling Save(mainPath)) persists it into the main progress file too -
        /// otherwise the main file would stay stale until some unrelated change (e.g. moving to a
        /// new screen) happened to mark it dirty again. Used when restoring a per-save-slot
        /// snapshot (see MoreSavesPatches), since at that point the in-memory state has just
        /// diverged from whatever the main file currently holds on disk.
        /// </summary>
        public static void LoadSnapshot(string path)
        {
            lock (s_lock)
            {
                LoadInternal(path);
                s_dirty = true;
            }
        }

        private static void LoadInternal(string path)
        {
            s_levels = new Dictionary<string, LevelEntry>();
            s_dirty = false;
            if (!File.Exists(path))
            {
                return;
            }
            XElement root = XDocument.Load(path).Root;
            if (root == null)
            {
                return;
            }
            foreach (XElement levelElement in root.Elements("Level"))
            {
                string key = (string)levelElement.Attribute("key");
                if (string.IsNullOrEmpty(key))
                {
                    continue;
                }
                LevelEntry levelEntry = new LevelEntry
                {
                    NextOrder = (int?)levelElement.Attribute("nextOrder") ?? 0,
                    HasReachedBabe = (bool?)levelElement.Attribute("reachedBabe") ?? false,
                };
                foreach (XElement areaElement in levelElement.Elements("Area"))
                {
                    int start = (int?)areaElement.Attribute("start") ?? 0;
                    levelEntry.Areas[start] = new AreaEntry
                    {
                        Order = (int?)areaElement.Attribute("order") ?? 0,
                        AttemptCount = (int?)areaElement.Attribute("attempts") ?? 0,
                        HasFullyCleared = (bool?)areaElement.Attribute("cleared") ?? false,
                        LapTime = TimeSpan.FromTicks((long?)areaElement.Attribute("lapTicks") ?? 0),
                        BestScreenIndex = (int?)areaElement.Attribute("bestScreenIndex") ?? 0,
                    };
                }
                s_levels[key] = levelEntry;
            }
        }

        public static void Save(string path)
        {
            // The lock only needs to cover building the (in-memory) XElement tree from
            // s_levels - the slow part, writing it to disk, happens after the lock is
            // released so it can't make the main thread wait on file I/O.
            XElement root;
            lock (s_lock)
            {
                if (!s_dirty)
                {
                    return;
                }
                root = BuildXml();
                s_dirty = false;
            }
            new XDocument(root).Save(path);
        }

        /// <summary>
        /// Unconditionally writes the current in-memory progress to path, regardless of whether
        /// anything has changed since the last regular Save (s_dirty). Used for snapshotting
        /// progress alongside a specific save slot (see MoreSavesPatches) - a slot snapshot needs
        /// writing every time that slot is saved, not just when the main file happens to be dirty.
        /// </summary>
        public static void SaveSnapshot(string path)
        {
            XElement root;
            lock (s_lock)
            {
                root = BuildXml();
            }
            new XDocument(root).Save(path);
        }

        private static XElement BuildXml()
        {
            var root = new XElement("AreaProgress");
            foreach (KeyValuePair<string, LevelEntry> levelPair in s_levels)
            {
                XElement levelElement = new XElement("Level",
                    new XAttribute("key", levelPair.Key),
                    new XAttribute("nextOrder", levelPair.Value.NextOrder),
                    new XAttribute("reachedBabe", levelPair.Value.HasReachedBabe));
                foreach (KeyValuePair<int, AreaEntry> areaPair in levelPair.Value.Areas)
                {
                    levelElement.Add(new XElement("Area",
                        new XAttribute("start", areaPair.Key),
                        new XAttribute("order", areaPair.Value.Order),
                        new XAttribute("attempts", areaPair.Value.AttemptCount),
                        new XAttribute("cleared", areaPair.Value.HasFullyCleared),
                        new XAttribute("lapTicks", areaPair.Value.LapTime.Ticks),
                        new XAttribute("bestScreenIndex", areaPair.Value.BestScreenIndex)));
                }
                root.Add(levelElement);
            }
            return root;
        }

        public static void Clear()
        {
            lock (s_lock)
            {
                s_levels = new Dictionary<string, LevelEntry>();
                s_dirty = true;
            }
        }

        /// <summary>
        /// Called whenever the resolved area changes during play. Registers brand-new areas, or
        /// bumps the attempt count when re-entering an already-known area from a physically lower
        /// screen (i.e. climbing up into it) - climbing up into an area is a new attempt at it;
        /// falling down into one isn't. Also doubles as the level-start "quiet" resolve when called
        /// with previousStart: null, since a null previousStart never triggers the attempt-count
        /// bump on an already-known area.
        ///
        /// This compares the raw screen number (start), not first-visit Order: attempt-counting is
        /// about whether *this specific transition* was a climb or a fall, which is inherently a
        /// spatial question, not a "have I been here before" one. A side path branching off below
        /// the main area is always discovered chronologically *after* the main area (so it always
        /// has a later Order), even though it's spatially lower - so comparing Order here would
        /// wrongly treat "climbing back up out of the side path" the same as "falling into it",
        /// and never bump the main area's attempt count. Comparing actual screen position avoids
        /// that, at the cost of the (accepted, documented) warp caveat: a warp can land the player
        /// on a screen number that doesn't reflect genuine further progress. PB/Progression Detail
        /// still use Order for that reason - only this attempt-direction check needs real position.
        ///
        /// forceBump lets the caller flag a transition as a fresh attempt even when newStart isn't
        /// physically higher than previousStart - used for re-entering the *same* area after a trip
        /// through a "gap" (AreaTracker.OnUpdate's s_awayFromLastArea), since previousStart equals
        /// newStart in that case and the plain comparison below would never fire. Not used for the
        /// very first area - see AreaTracker's isOnFirstScreen/BumpAttempt instead, since it has no
        /// area below it to register a "gap departure" from in the first place.
        /// </summary>
        public static void OnEnterArea(string levelKey, int newStart, int? previousStart, bool forceBump, TimeSpan playTime)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                if (!levelEntry.Areas.TryGetValue(newStart, out AreaEntry newEntry))
                {
                    levelEntry.Areas[newStart] = new AreaEntry { Order = levelEntry.NextOrder++, AttemptCount = 1, HasFullyCleared = false, LapTime = playTime };
                    s_dirty = true;
                    return;
                }
                if (!previousStart.HasValue)
                {
                    return;
                }
                if (forceBump || previousStart.Value < newStart)
                {
                    newEntry.AttemptCount++;
                    s_dirty = true;
                }
            }
        }

        /// <summary>
        /// Unconditionally bumps start's attempt count (registering it first, at count 1, if this
        /// is the very first time it's been seen). Used solely for the very first area landing back
        /// on the literal first screen of the level (AreaTracker.OnUpdate's isOnFirstScreen check):
        /// unlike every other area, it has no area below it to register a "left, then came back"
        /// transition through via OnEnterArea, since there's nothing below the start of the level to
        /// physically fall into - the first screen itself is the only signal that it's being
        /// attempted again.
        /// </summary>
        public static void BumpAttempt(string levelKey, int start, TimeSpan playTime)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                if (!levelEntry.Areas.TryGetValue(start, out AreaEntry entry))
                {
                    levelEntry.Areas[start] = new AreaEntry { Order = levelEntry.NextOrder++, AttemptCount = 1, HasFullyCleared = false, LapTime = playTime };
                }
                else
                {
                    entry.AttemptCount++;
                }
                s_dirty = true;
            }
        }

        public static void MarkCleared(string levelKey, int start)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                if (!levelEntry.Areas.TryGetValue(start, out AreaEntry entry))
                {
                    return;
                }
                if (!entry.HasFullyCleared)
                {
                    entry.HasFullyCleared = true;
                    s_dirty = true;
                }
            }
        }

        /// <summary>
        /// Warp-safe supplement to AreaTracker's FindNextLocation-based clear check: marks
        /// previousStart cleared if (a) it has ever reached its own last screen
        /// (BestScreenIndex >= previousAreaEnd) and (b) newStart's Order is exactly one more than
        /// previousStart's - i.e. newStart really was the next area the player visited, by actual
        /// visit sequence rather than by start position. A warp's destination can have any screen
        /// number (see NOTES.md's TeleportLink findings), so it isn't necessarily "next by start"
        /// even when it genuinely is the next area visited - but Order still reflects that.
        ///
        /// Condition (a) is what keeps this from misfiring on a side path: a side path discovered
        /// immediately after leaving previousStart would also get Order == previousStart's
        /// Order + 1, but the player is very unlikely to have already reached previousStart's own
        /// last screen if they detoured into the side path before finishing it.
        /// </summary>
        public static void MarkClearedIfOrderSequential(string levelKey, int previousStart, int previousAreaEnd, int newStart)
        {
            lock (s_lock)
            {
                if (!s_levels.TryGetValue(levelKey, out LevelEntry levelEntry))
                {
                    return;
                }
                if (!levelEntry.Areas.TryGetValue(previousStart, out AreaEntry previousEntry)
                    || !levelEntry.Areas.TryGetValue(newStart, out AreaEntry newEntry))
                {
                    return;
                }
                if (previousEntry.HasFullyCleared || previousEntry.BestScreenIndex < previousAreaEnd)
                {
                    return;
                }
                if (newEntry.Order != previousEntry.Order + 1)
                {
                    return;
                }
                previousEntry.HasFullyCleared = true;
                s_dirty = true;
            }
        }

        /// <summary>
        /// Catches up an area's cleared flag if it already has an entry (so this mod was already
        /// tracking it earlier this playthrough) but HasFullyCleared is still false despite the
        /// resume screen being past its end - e.g. it was registered but MarkCleared never fired
        /// for it (a warp skipped past the literal "next area" exact-match it relies on).
        ///
        /// This does *not* retroactively create entries for areas that have no entry at all (e.g.
        /// ones passed before this mod was ever installed) - those are simply left unrecorded
        /// until the player visits them again with the mod active.
        /// </summary>
        public static void RestoreClearedOnResume(string levelKey, Location[] sortedLocations, int screenIndex1)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                foreach (Location location in sortedLocations)
                {
                    if (location.end < screenIndex1
                        && levelEntry.Areas.TryGetValue(location.start, out AreaEntry entry)
                        && !entry.HasFullyCleared)
                    {
                        entry.HasFullyCleared = true;
                        s_dirty = true;
                    }
                }
            }
        }

        public static int GetAttemptCount(string levelKey, int start)
        {
            lock (s_lock)
            {
                if (s_levels.TryGetValue(levelKey, out LevelEntry levelEntry)
                    && levelEntry.Areas.TryGetValue(start, out AreaEntry entry))
                {
                    return entry.AttemptCount;
                }
                return 0;
            }
        }

        public static bool IsCleared(string levelKey, int start)
        {
            lock (s_lock)
            {
                return s_levels.TryGetValue(levelKey, out LevelEntry levelEntry)
                    && levelEntry.Areas.TryGetValue(start, out AreaEntry entry)
                    && entry.HasFullyCleared;
            }
        }

        /// <summary>Marks levelKey as having reached one of its 3 "Babe" ending screens. See LevelEntry.HasReachedBabe.</summary>
        public static void MarkBabeReached(string levelKey)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                if (!levelEntry.HasReachedBabe)
                {
                    levelEntry.HasReachedBabe = true;
                    s_dirty = true;
                }
            }
        }

        public static bool HasReachedBabe(string levelKey)
        {
            lock (s_lock)
            {
                return s_levels.TryGetValue(levelKey, out LevelEntry levelEntry) && levelEntry.HasReachedBabe;
            }
        }

        public readonly struct AreaSummary
        {
            public AreaSummary(int start, int order, int attemptCount, TimeSpan lapTime, int bestScreenIndex)
            {
                Start = start;
                Order = order;
                AttemptCount = attemptCount;
                LapTime = lapTime;
                BestScreenIndex = bestScreenIndex;
            }

            public int Start { get; }
            public int Order { get; }
            public int AttemptCount { get; }
            public TimeSpan LapTime { get; }

            /// <summary>Deepest exact-matched screen ever reached within this area.</summary>
            public int BestScreenIndex { get; }
        }

        /// <summary>
        /// Every area in levelKey that's been reached at least once, in first-visit order.
        /// </summary>
        public static List<AreaSummary> GetVisitedAreas(string levelKey)
        {
            lock (s_lock)
            {
                var result = new List<AreaSummary>();
                if (s_levels.TryGetValue(levelKey, out LevelEntry levelEntry))
                {
                    foreach (KeyValuePair<int, AreaEntry> pair in levelEntry.Areas)
                    {
                        result.Add(new AreaSummary(pair.Key, pair.Value.Order, pair.Value.AttemptCount, pair.Value.LapTime, pair.Value.BestScreenIndex));
                    }
                }
                result.Sort((a, b) => a.Order.CompareTo(b.Order));
                return result;
            }
        }

        /// <summary>
        /// Records screenIndex1 as areaStart's new deepest screen if it's higher than whatever was
        /// recorded before for that area. Callers should only pass exact-matched screens ("on the
        /// way" gaps excluded). Does nothing if areaStart hasn't been registered via OnEnterArea
        /// yet (callers should call that first).
        /// </summary>
        public static void UpdateBestProgress(string levelKey, int areaStart, int screenIndex1)
        {
            lock (s_lock)
            {
                LevelEntry levelEntry = GetOrCreateLevel(levelKey);
                if (levelEntry.Areas.TryGetValue(areaStart, out AreaEntry entry) && screenIndex1 > entry.BestScreenIndex)
                {
                    entry.BestScreenIndex = screenIndex1;
                    s_dirty = true;
                }
            }
        }

        /// <summary>
        /// The most recently first-discovered area in levelKey (i.e. the one with the highest
        /// Order), or null if none have been reached yet. "On the way" gap screens never have
        /// their own area entry here, so they're naturally excluded.
        ///
        /// This picks the PB area by Order (first-visit sequence), not by raw screen number:
        /// warps mean a screen number alone isn't reliable as a progress indicator (a warp can
        /// land the player on a high screen number that isn't genuinely "further" in the run), but
        /// the sequence the player actually visited areas in still is.
        /// </summary>
        public static AreaSummary? GetPersonalBest(string levelKey)
        {
            lock (s_lock)
            {
                if (!s_levels.TryGetValue(levelKey, out LevelEntry levelEntry) || levelEntry.Areas.Count == 0)
                {
                    return null;
                }
                int bestStart = 0;
                AreaEntry bestEntry = null;
                foreach (KeyValuePair<int, AreaEntry> pair in levelEntry.Areas)
                {
                    if (bestEntry == null || pair.Value.Order > bestEntry.Order)
                    {
                        bestStart = pair.Key;
                        bestEntry = pair.Value;
                    }
                }
                return new AreaSummary(bestStart, bestEntry.Order, bestEntry.AttemptCount, bestEntry.LapTime, bestEntry.BestScreenIndex);
            }
        }

        private static LevelEntry GetOrCreateLevel(string levelKey)
        {
            if (!s_levels.TryGetValue(levelKey, out LevelEntry levelEntry))
            {
                levelEntry = new LevelEntry();
                s_levels[levelKey] = levelEntry;
            }
            return levelEntry;
        }
    }
}
