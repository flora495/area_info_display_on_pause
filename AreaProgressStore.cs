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
                root = new XElement("AreaProgress");
                foreach (KeyValuePair<string, LevelEntry> levelPair in s_levels)
                {
                    XElement levelElement = new XElement("Level",
                        new XAttribute("key", levelPair.Key),
                        new XAttribute("nextOrder", levelPair.Value.NextOrder));
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
                s_dirty = false;
            }
            new XDocument(root).Save(path);
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
        /// bumps the attempt count when re-entering an already-known area whose first-visit Order
        /// is later than the area just left (i.e. climbing up into it, in visit-sequence terms) -
        /// climbing up into an area is a new attempt at it; falling down into one isn't. Also
        /// doubles as the level-start "quiet" resolve when called with previousStart: null, since
        /// a null previousStart never triggers the attempt-count bump on an already-known area.
        ///
        /// This compares Order (first-visit sequence), not raw screen number: this game has
        /// warps, so a screen number alone isn't reliable as "is this area higher up than the
        /// other one" (a warp can land the player on a high screen number that isn't genuinely
        /// further along) - but the actual sequence the player visited areas in still is.
        ///
        /// The very first area has no area below it to climb up from, so under the Order-
        /// comparison rule its attempt count would only ever be set once and never bumped again.
        /// isFirstScreenOfFirstArea carves out that one area: landing on its own literal first
        /// screen (from anywhere) counts as a new attempt too, since that's what "starting over"
        /// looks like for it.
        /// </summary>
        public static void OnEnterArea(string levelKey, int newStart, int? previousStart, bool isFirstScreenOfFirstArea, TimeSpan playTime)
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
                if (isFirstScreenOfFirstArea)
                {
                    newEntry.AttemptCount++;
                    s_dirty = true;
                    return;
                }
                if (levelEntry.Areas.TryGetValue(previousStart.Value, out AreaEntry previousEntry)
                    && previousEntry.Order < newEntry.Order)
                {
                    newEntry.AttemptCount++;
                    s_dirty = true;
                }
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
